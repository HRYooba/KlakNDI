using UnityEngine;
using UnityEngine.Rendering;
using IDisposable = System.IDisposable;
using IntPtr = System.IntPtr;

namespace Klak.Ndi {

//
// A format converter class wrapping GPU tasks and resources
//
// We use GPU (compute) to convert textures between the renderer-friendly raw
// formats and the NDI-friendly (chroma-subsampled) formats. This class wraps
// the cumbersome things and provides a simple API.
//
sealed class FormatConverter : IDisposable
{
    #region Common members

    NdiResources _resources;

    public FormatConverter(NdiResources resources) => _resources = resources;

    public void Dispose() => ReleaseBuffers();

    void ReleaseBuffers()
    {
        _encoderOutput?.Dispose();
        _encoderOutput = null;

        _decoderInput?.Dispose();
        _decoderInput = null;

        Util.Destroy(_decoderOutput);
        _decoderOutput = null;
    }

    // UYVY packs two pixels into a word, and the UYVA alpha plane packs four.
    void CheckDimensions(int width, bool alpha)
    {
        if (alpha && (width & 3) != 0)
            WarnWrongSize($"Width ({width}) must be a multiple of 4 with alpha.");
        else if ((width & 1) != 0)
            WarnWrongSize($"Width ({width}) must be a multiple of 2.");
    }

    // Thread group counts covering a frame. The kernels reject the threads
    // that fall outside the frame.
    static int GroupsX(int width, bool alpha)
      => alpha ? (width / 4 + 3) / 4 : (width / 2 + 7) / 8;

    static int GroupsY(int height)
      => (height + 7) / 8;

    void WarnWrongSize(string text)
      => Debug.LogWarning("[KlakNDI] Unsupported frame size: " + text);

    #endregion

    #region Encoder implementation

    ComputeBuffer _encoderOutput;

    // Immediate mode version
    public ComputeBuffer Encode(Texture source, bool enableAlpha, bool vflip)
      => Encode(source, new RectInt(0, 0, source.width, source.height),
                enableAlpha, vflip);

    // Immediate mode version with a source region
    // The region may extend outside the source; those pixels become black.
    public ComputeBuffer Encode
      (Texture source, RectInt region, bool enableAlpha, bool vflip)
    {
        var width = region.width;
        var height = region.height;
        var dataCount = Util.FrameDataSize(width, height, enableAlpha) / 4;

        // Reallocate the output buffer when the output size was changed.
        if (_encoderOutput != null && _encoderOutput.count != dataCount)
            ReleaseBuffers();

        // Output buffer allocation
        if (_encoderOutput == null)
        {
            CheckDimensions(width, enableAlpha);
            _encoderOutput = new ComputeBuffer(dataCount, 4);
        }

        // Compute thread dispatching
        var compute = _resources.encoderCompute;
        var pass = (enableAlpha ? 2 : 0) + (Util.InGammaMode ? 0 : 1);
        compute.SetFloat("VFlip", vflip ? 1 : 0);
        compute.SetInts("Offset", region.x, region.y);
        compute.SetInts("FrameSize", width, height);
        compute.SetTexture(pass, "Source", source);
        compute.SetBuffer(pass, "Destination", _encoderOutput);
        compute.Dispatch
          (pass, GroupsX(width, enableAlpha), GroupsY(height), 1);

        return _encoderOutput;
    }

    // Command buffer version
    public ComputeBuffer Encode
      (CommandBuffer cb, RenderTargetIdentifier source,
       int width, int height, bool enableAlpha, bool vflip)
    {
        var dataCount = Util.FrameDataSize(width, height, enableAlpha) / 4;

        // Reallocate the output buffer when the output size was changed.
        if (_encoderOutput != null && _encoderOutput.count != dataCount)
            ReleaseBuffers();

        // Output buffer allocation
        if (_encoderOutput == null)
        {
            CheckDimensions(width, enableAlpha);
            _encoderOutput = new ComputeBuffer(dataCount, 4);
        }

        // Compute thread dispatching
        var compute = _resources.encoderCompute;
        var pass = (enableAlpha ? 2 : 0) + (Util.InGammaMode ? 0 : 1);
        cb.SetComputeFloatParam(compute, "VFlip", vflip ? 1 : 0);
        cb.SetComputeIntParams(compute, "Offset", 0, 0);
        cb.SetComputeIntParams(compute, "FrameSize", width, height);
        cb.SetComputeTextureParam(compute, pass, "Source", source);
        cb.SetComputeBufferParam(compute, pass, "Destination", _encoderOutput);
        cb.DispatchCompute
          (compute, pass, GroupsX(width, enableAlpha), GroupsY(height), 1);

        return _encoderOutput;
    }

    #endregion

    #region Decoder implementation

    ComputeBuffer _decoderInput;
    RenderTexture _decoderOutput;

    public RenderTexture LastDecoderOutput => _decoderOutput;

    // Received frames come from the network. Frames the decoder can't lay out
    // (non-positive, odd, or unaligned-with-alpha widths) are rejected
    // instead of being decoded with out-of-frame indices.
    // The decoder reads tightly packed rows, so padded rows are rejected too.
    static bool IsDecodable
      (int width, int height, int stride, bool alpha, IntPtr data)
      => data != IntPtr.Zero && width > 0 && height > 0 &&
         (width & (alpha ? 3 : 1)) == 0 && stride == width * 2 &&
         (long)width * height <= int.MaxValue / 4;

    // Returns null when the frame is rejected.
    public RenderTexture Decode
      (int width, int height, int stride, bool enableAlpha, IntPtr data)
    {
        if (!IsDecodable(width, height, stride, enableAlpha, data))
        {
            CheckDimensions(width, enableAlpha);
            return null;
        }

        var dataCount = Util.FrameDataSize(width, height, enableAlpha) / 4;

        // Reallocate the input buffer when the input size was changed.
        if (_decoderInput != null && _decoderInput.count != dataCount)
            ReleaseBuffers();

        // Reallocate the output buffer when the output size was changed.
        if (_decoderOutput != null &&
            (_decoderOutput.width != width ||
             _decoderOutput.height != height))
            ReleaseBuffers();

        // Input buffer allocation
        if (_decoderInput == null)
            _decoderInput = new ComputeBuffer(dataCount, 4);

        // Output buffer allocation
        if (_decoderOutput == null)
        {
            CheckDimensions(width, enableAlpha);
        #if KLAK_NDI_ISSUE200_WORKAROUND
            _decoderOutput = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf);
        #else
            _decoderOutput = new RenderTexture(width, height, 0);
        #endif
            _decoderOutput.enableRandomWrite = true;
            _decoderOutput.Create();
        }

        // Input buffer update
        _decoderInput.SetData(data, dataCount, 4);

        // Kenel select
        var pass = (enableAlpha ? 2 : 0);

        // As far as we know, only Metal supports sRGB write to UAV, so we
        // use the linear-color kernels only on Metal
        if (!Util.InGammaMode && Util.UsingMetal) pass++;

        // Decoder compute dispatching
        var compute = _resources.decoderCompute;
        compute.SetInts("FrameSize", width, height);
        compute.SetBuffer(pass, "Source", _decoderInput);
        compute.SetTexture(pass, "Destination", _decoderOutput);
        compute.Dispatch
          (pass, GroupsX(width, enableAlpha), GroupsY(height), 1);

        return _decoderOutput;
    }

    #endregion
}

} // namespace Klak.Ndi
