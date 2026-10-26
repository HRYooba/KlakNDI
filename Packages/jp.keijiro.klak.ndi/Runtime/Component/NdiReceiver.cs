using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using IntPtr = System.IntPtr;
using Marshal = System.Runtime.InteropServices.Marshal;

namespace Klak.Ndi {

[ExecuteInEditMode]
public sealed partial class NdiReceiver : MonoBehaviour
{
    #region Receiver objects

    Interop.Recv _recv;
    FormatConverter _converter;
    MaterialPropertyBlock _override;
    readonly AudioRingBuffer _audioBuffer = new AudioRingBuffer();

    void PrepareReceiverObjects()
    {
        if (_recv == null) _recv = RecvHelper.TryCreateRecv(ndiName);
        if (_converter == null) _converter = new FormatConverter(_resources);
        if (_override == null) _override = new MaterialPropertyBlock();
    }

    void ReleaseReceiverObjects()
    {
        _recv?.Dispose();
        _recv = null;

        _audioBuffer.Clear();

        _converter?.Dispose();
        _converter = null;

        // We don't dispose _override because it's reusable.
    }

    #endregion

    #region Receiver implementation

    // Upper bound of frames taken from the queue in a single update
    const int MaxCapturesPerUpdate = 32;

    RenderTexture TryReceiveFrame()
    {
        PrepareReceiverObjects();
        if (_recv == null) return null;

        if (!receiveAudio)
        {
            // Video frame capturing
            var frameOrNull = RecvHelper.TryCaptureVideoFrame(_recv);
            if (frameOrNull == null) return null;
            return DecodeAndFree((Interop.VideoFrame)frameOrNull);
        }

        // Audio arrives more often than video, so drain the queue: push every
        // audio frame and keep only the newest video frame.
        var video = default(Interop.VideoFrame);
        var hasVideo = false;

        for (var i = 0; i < MaxCapturesPerUpdate; i++)
        {
            var type = _recv.Capture
              (out var v, out var a, IntPtr.Zero, 0);

            if (type == Interop.FrameType.Video)
            {
                if (hasVideo) _recv.FreeVideoFrame(video);
                (video, hasVideo) = (v, true);
            }
            else if (type == Interop.FrameType.Audio)
            {
                _audioBuffer.Push(a);
                _recv.FreeAudioFrame(a);
            }
            else
            {
                break;
            }
        }

        return hasVideo ? DecodeAndFree(video) : null;
    }

    RenderTexture DecodeAndFree(in Interop.VideoFrame frame)
    {
        // Pixel format conversion
        var rt = _converter.Decode
          (frame.Width, frame.Height, Util.HasAlpha(frame.FourCC), frame.Data);

        // Metadata retrieval
        if (frame.Metadata != IntPtr.Zero)
            metadata = Marshal.PtrToStringAnsi(frame.Metadata);
        else
            metadata = null;

        // Video frame release
        _recv.FreeVideoFrame(frame);

        return rt;
    }

    #endregion

    #region Component state controller

    internal void Restart() => ReleaseReceiverObjects();

    #endregion

    #region MonoBehaviour implementation

    // OnAudioFilterRead runs on the audio thread and can't read
    // AudioSettings, so the output rate is copied here.
    volatile int _outputSampleRate;

    void UpdateOutputSampleRate(bool deviceWasChanged = false)
      => _outputSampleRate = AudioSettings.outputSampleRate;

    void OnEnable()
    {
        UpdateOutputSampleRate();
        AudioSettings.OnAudioConfigurationChanged += UpdateOutputSampleRate;
    }

    void OnDisable()
    {
        AudioSettings.OnAudioConfigurationChanged -= UpdateOutputSampleRate;
        ReleaseReceiverObjects();
    }

    void Start()
    {
        // Received audio is mixed in through the AudioSource filter chain,
        // which only runs while the AudioSource is playing.
        if (Application.isPlaying && _receiveAudioRuntime &&
            TryGetComponent<AudioSource>(out var source) && !source.isPlaying)
            source.Play();
    }

    // Received audio output
    // This runs only when an AudioSource is playing on this GameObject.
    void OnAudioFilterRead(float[] data, int channels)
    {
        if (_receiveAudioRuntime)
            _audioBuffer.Read(data, channels, _outputSampleRate);
    }

    void Update()
    {
        var rt = TryReceiveFrame();
        if (rt == null) return;

        // Material property override
        if (targetRenderer != null)
        {
            targetRenderer.GetPropertyBlock(_override);
            _override.SetTexture(targetMaterialProperty, rt);
            targetRenderer.SetPropertyBlock(_override);
        }

        // External texture update
        if (targetTexture != null) Graphics.Blit(rt, targetTexture);
    }

    #endregion
}

} // namespace Klak.Ndi
