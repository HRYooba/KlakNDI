using UnityEngine;
using UnityEngine.Rendering;

namespace Klak.Ndi {

[ExecuteInEditMode]
public sealed partial class NdiSender : MonoBehaviour
{
    #region Sender objects

    Interop.Send _send;
    ReadbackPool _pool;
    FormatConverter _converter;
    System.Action<AsyncGPUReadbackRequest> _onReadback;

    void PrepareSenderObjects()
    {
        if (_send == null)
        {
            // Game view capture method: Borrow the shared sender instance.
            var send = captureMethod == CaptureMethod.GameView
              ? SharedInstance.GameViewSend : Interop.Send.Create(ndiName);
            lock (SharedInstance.SendLock) _send = send;
        }

        // Private object initialization
        if (_pool == null) _pool = new ReadbackPool();
        if (_converter == null) _converter = new FormatConverter(_resources);
        if (_onReadback == null) _onReadback = OnReadback;
    }

    void ReleaseSenderObjects()
    {
        // Total synchronization: This may cause a frame hiccup, but it's
        // needed to dispose the readback buffers safely.
        AsyncGPUReadback.WaitAllRequests();

        // Detach the sender instance first. SendAudio holds the lock from the
        // audio thread, so the slow sync/disposal below runs outside it.
        Interop.Send send;
        lock (SharedInstance.SendLock) (send, _send) = (_send, null);

        // Game view capture method: Leave the sender instance without
        // disposing (we're not the owner) but synchronize it. It's needed to
        // dispose the readback buffers safely too.
        if (SharedInstance.IsGameViewSend(send))
            send.SendVideoAsync(); // Sync by null-send
        else
            send?.Dispose(); // Private objet disposal

        _pool?.Dispose();
        _pool = null;

        _converter?.Dispose();
        _converter = null;

        // We don't dispose _onReadback because it's reusable.
    }

    #endregion

    #region Capture coroutine for the Texture/GameView capture methods

    // Region of the source texture to send (the whole texture when unset)
    // The width is rounded down to what the frame format can pack.
    RectInt SourceRegion
    {
        get
        {
            var region = sourceRect.width > 0 && sourceRect.height > 0
              ? sourceRect
              : new RectInt(0, 0, sourceTexture.width, sourceTexture.height);
            region.width = AlignWidth(region.width);
            return region;
        }
    }

    // Width rounded down to what the frame format can pack
    int AlignWidth(int width) => width & (keepAlpha ? ~3 : ~1);

    System.Collections.IEnumerator CaptureCoroutine()
    {
        for (var eof = new WaitForEndOfFrame(); true;)
        {
        #if !UNITY_ANDROID || UNITY_EDITOR
            // Wait for the end of the frame.
            yield return eof;
        #else
            // Temporary workaround for glitches on Android:
            // Process the input at the beginning of the frame instead of EoF.
            // I don't know why these glitches occur, but this change solves
            // them anyway. I should investigate them further if they reappear.
            yield return null;
        #endif

            PrepareSenderObjects();

            // Texture capture method
            if (captureMethod == CaptureMethod.Texture && sourceTexture != null &&
                SourceRegion is var region &&
                region.width > 0 && region.height > 0)
            {
                // Pixel format conversion
                var buffer = _converter.Encode
                  (sourceTexture, region, keepAlpha, true);

                // Readback entry allocation and request
                _pool.NewEntry(region.width, region.height, keepAlpha, metadata)
                     .RequestReadback(buffer, _onReadback);
            }

            // Game View capture method
            if (captureMethod == CaptureMethod.GameView)
            {
                // Game View screen capture with a temporary RT
                var (w, h) = (Screen.width, Screen.height);
                var tempRT = RenderTexture.GetTemporary(w, h, 0);
                ScreenCapture.CaptureScreenshotIntoRenderTexture(tempRT);

                // Pixel format conversion
                w = AlignWidth(w);
                var buffer = _converter.Encode
                  (tempRT, new RectInt(0, 0, w, h), keepAlpha, false);
                RenderTexture.ReleaseTemporary(tempRT);

                // Readback entry allocation and request
                _pool.NewEntry(w, h, keepAlpha, metadata)
                     .RequestReadback(buffer, _onReadback);
            }
        }
    }

    #endregion

    #region SRP camera capture callback for the Camera capture method

    void OnCameraCapture(RenderTargetIdentifier source, CommandBuffer cb)
    {
        // A SRP may call this callback after object destruction. We can
        // exclude those cases by null-checking _attachedCamera.
        if (_attachedCamera == null) return;

        PrepareSenderObjects();

        // Pixel format conversion
        var (w, h) = (AlignWidth(sourceCamera.pixelWidth),
                      sourceCamera.pixelHeight);
        var buffer = _converter.Encode(cb, source, w, h, keepAlpha, true);

        // Readback entry allocation and request
        _pool.NewEntry(w, h, keepAlpha, metadata)
             .RequestReadback(buffer, _onReadback);
    }

    #endregion

    #region GPU readback completion callback

    unsafe void OnReadback(AsyncGPUReadbackRequest req)
    {
        // Readback entry retrieval
        var entry = _pool.FindEntry(req.GetData<byte>());
        if (entry == null) return;

        // Invalid state detection
        if (req.hasError || _send == null || _send.IsInvalid || _send.IsClosed)
        {
            // Do nothing but release the readback entry.
            _pool.Free(entry);
            return;
        }

        // Frame data
        var frame = new Interop.VideoFrame
          { Width       = entry.Width,
            Height      = entry.Height,
            LineStride  = entry.Stride,
            FourCC      = entry.FourCC,
            FrameFormat = Interop.FrameFormat.Progressive,
            Timecode    = Interop.Timecode.Synthesize,
            Data        = entry.ImagePointer,
            Metadata    = entry.MetadataPointer };

        // Async-send initiation
        // This causes a synchronization for the last frame -- i.e., It locks
        // the thread if the last frame is still under processing.
        _send.SendVideoAsync(frame);

        // We don't need the last frame anymore. Free it.
        _pool.FreeMarkedEntry();

        // Mark this frame to get freed in the next frame.
        _pool.Mark(entry);
    }

    #endregion

    #region Audio sending

    // Planar sample buffer (reused; guarded by SharedInstance.SendLock)
    float[] _audioPlanes = new float[0];

    // Sends interleaved audio samples with this sender's NDI source.
    // Thread-safe: this can be called from the audio thread (for example,
    // from OnAudioFilterRead). The audio rides on the NDI source that the
    // video capture creates, so it does nothing until the first video frame
    // is captured or while the sender is inactive.
    // Senders in Game View mode share one NDI source, so audio sent from
    // several of them is mixed into the same stream.
    public unsafe void SendAudio
      (System.ReadOnlySpan<float> interleaved, int channels, int sampleRate)
    {
        if (channels <= 0 || interleaved.Length < channels) return;

        var samples = interleaved.Length / channels;

        lock (SharedInstance.SendLock)
        {
            if (_send == null || _send.IsInvalid || _send.IsClosed) return;

            // Interleaved -> planar conversion
            if (_audioPlanes.Length < samples * channels)
                _audioPlanes = new float[samples * channels];

            for (var ch = 0; ch < channels; ch++)
                for (var i = 0; i < samples; i++)
                    _audioPlanes[ch * samples + i] =
                      interleaved[i * channels + ch];

            fixed (float* data = _audioPlanes)
            {
                var frame = new Interop.AudioFrame
                  { SampleRate    = sampleRate,
                    ChannelCount  = channels,
                    SampleCount   = samples,
                    Timecode      = Interop.Timecode.Synthesize,
                    Data          = (System.IntPtr)data,
                    ChannelStride = samples * sizeof(float) };

                _send.SendAudio(frame);
            }
        }
    }

    #endregion

    #region Component state controller

    Camera _attachedCamera;

    // Component state reset without NDI object disposal
    internal void ResetState(bool willBeActive)
    {
        // Camera capture coroutine termination
        // We use this to kill only a single coroutine. It may sound like
        // overkill, but I think there is no side effect in doing so.
        StopAllCoroutines();

        #if KLAK_NDI_HAS_SRP

        // A SRP may call this callback after camera destruction. We can
        // exclude those cases by null-checking _attachedCamera.
        if (_attachedCamera != null)
            CameraCaptureBridge.RemoveCaptureAction(_attachedCamera, OnCameraCapture);

        #endif

        _attachedCamera = null;

        // The following part of code is to activate the subcomponents. We can
        // break here if willBeActive is false.
        if (!willBeActive) return;

        if (captureMethod == CaptureMethod.Camera)
        {
            #if KLAK_NDI_HAS_SRP

            // Camera capture callback setup
            if (sourceCamera != null)
                CameraCaptureBridge.AddCaptureAction(sourceCamera, OnCameraCapture);

            #endif

            _attachedCamera = sourceCamera;
        }
        else
        {
            // Capture coroutine initiation
            StartCoroutine(CaptureCoroutine());
        }
    }

    // Component state reset with NDI object disposal
    internal void Restart(bool willBeActivate)
    {
        ResetState(false);
        ReleaseSenderObjects();
        if (willBeActivate) ResetState(true);
    }

    internal void ResetState() => ResetState(isActiveAndEnabled);
    internal void Restart() => Restart(isActiveAndEnabled);

    #endregion

    #region MonoBehaviour implementation

    void OnEnable() => ResetState();
    void OnDisable() => Restart(false);
    void OnDestroy() => Restart(false);

    #endregion
}

} // namespace Klak.Ndi
