using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;
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

    // Interval of the attempts to find the source (in seconds)
    // Finding a source marshals the whole source list, so it's not repeated
    // on every update while the source is missing.
    const float RecvRetryInterval = 1;

    float _nextRecvAttempt;

    void PrepareReceiverObjects()
    {
        if (_recv == null && Time.realtimeSinceStartup >= _nextRecvAttempt)
        {
            _recv = RecvHelper.TryCreateRecv(ndiName);
            if (_recv == null)
                _nextRecvAttempt = Time.realtimeSinceStartup + RecvRetryInterval;
        }
        if (_converter == null) _converter = new FormatConverter(_resources);
        if (_override == null) _override = new MaterialPropertyBlock();
    }

    // True while frames taken from _recv are held. The videoFrameCaptured
    // handler may restart or disable the receiver, so the release is deferred
    // until the held frames are freed.
    bool _capturing;
    bool _releasePending;

    void ReleaseReceiverObjects()
    {
        if (_capturing)
        {
            _releasePending = true;
            return;
        }

        _recv?.Dispose();
        _recv = null;
        _nextRecvAttempt = 0;

        _audioBuffer.Clear();

        _converter?.Dispose();
        _converter = null;

        // We don't dispose _override because it's reusable.
    }

    #endregion

    #region Receiver implementation

    // Upper bound of frames taken from the queue in a single update
    // (a safety bound; the queue is normally drained well before it)
    const int MaxCapturesPerUpdate = 1024;

    RenderTexture TryReceiveFrame()
    {
        PrepareReceiverObjects();
        if (_recv == null) return null;

        // Drain the queue and keep only the newest video frame, so that a
        // receiver updated less often than the source doesn't fall behind it.
        // Audio frames are pushed to the ring buffer when audio is enabled.
        var withAudio = receiveAudio;
        var recv = _recv;
        var video = default(Interop.VideoFrame);
        var hasVideo = false;

        _capturing = true;

        try
        {
            for (var i = 0; i < MaxCapturesPerUpdate; i++)
            {
                Interop.VideoFrame v;
                var a = default(Interop.AudioFrame);
                var type = withAudio
                  ? recv.Capture(out v, out a, IntPtr.Zero, 0)
                  : recv.Capture(out v, IntPtr.Zero, IntPtr.Zero, 0);

                if (type == Interop.FrameType.Video)
                {
                    if (hasVideo) recv.FreeVideoFrame(video);
                    (video, hasVideo) = (v, true);
                    videoFrameCaptured?.Invoke(v);
                    // The handler restarted or disabled the receiver.
                    if (_releasePending) break;
                }
                else if (type == Interop.FrameType.Audio)
                {
                    try { _audioBuffer.Push(a); }
                    finally { recv.FreeAudioFrame(a); }
                }
                else
                {
                    break;
                }
            }

            if (!hasVideo || _releasePending) return null;

            // DecodeAndFree frees the frame even when it throws.
            hasVideo = false;
            return DecodeAndFree(recv, video);
        }
        finally
        {
            // The held video frame is freed before the recv object is
            // released or an exception leaves.
            if (hasVideo) recv.FreeVideoFrame(video);
            _capturing = false;
            if (_releasePending)
            {
                _releasePending = false;
                ReleaseReceiverObjects();
            }
        }
    }

    RenderTexture DecodeAndFree(Interop.Recv recv, in Interop.VideoFrame frame)
    {
        try
        {
            // Pixel format conversion
            var rt = _converter.Decode
              (frame.Width, frame.Height, frame.LineStride,
               Util.HasAlpha(frame.FourCC), frame.Data);

            if (rt != null) decodedFrameCount++; else rejectedFrameCount++;

            // Metadata retrieval
            if (frame.Metadata != IntPtr.Zero)
                metadata = Marshal.PtrToStringAnsi(frame.Metadata);
            else
                metadata = null;

            return rt;
        }
        finally
        {
            // Video frame release
            recv.FreeVideoFrame(frame);
        }
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
        _extraIPsRegistered = true;
        SharedInstance.SetReceiverExtraIPs(this, _extraIPsRuntime);
        UpdateOutputSampleRate();
        AudioSettings.OnAudioConfigurationChanged += UpdateOutputSampleRate;
    }

    void OnDisable()
    {
        _extraIPsRegistered = false;
        SharedInstance.SetReceiverExtraIPs(this, null);
        AudioSettings.OnAudioConfigurationChanged -= UpdateOutputSampleRate;
        ReleaseReceiverObjects();
    }

    // Received audio is mixed in through the AudioSource filter chain, which
    // only runs while the AudioSource is playing.
    void EnsureAudioSourcePlaying()
    {
        if (!Application.isPlaying || !_receiveAudioRuntime) return;
        if (_audioSource == null && !TryGetComponent(out _audioSource)) return;
        if (!_audioSource.isPlaying) _audioSource.Play();
    }

    AudioSource _audioSource;

    // Received audio output
    // This runs only when an AudioSource is playing on this GameObject.
    void OnAudioFilterRead(float[] data, int channels)
    {
        if (_receiveAudioRuntime)
            _audioBuffer.Read(data, channels, _outputSampleRate);
    }

    static readonly ProfilerMarker UpdateMarker =
      new ProfilerMarker("NdiReceiver.Update");

    void Update()
    {
        using var _ = UpdateMarker.Auto();

        EnsureAudioSourcePlaying();

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
