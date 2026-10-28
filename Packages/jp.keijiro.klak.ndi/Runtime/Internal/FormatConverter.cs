using UnityEngine;
using UnityEngine.Rendering;
using IDisposable = System.IDisposable;
using IntPtr = System.IntPtr;
using Marshal = System.Runtime.InteropServices.Marshal;

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
      => Encode(source, source.width, source.height, enableAlpha, vflip);

    // Immediate mode version encoding the width x height part of the source
    // from its origin. The size must not exceed the source.
    public ComputeBuffer Encode
      (Texture source, int width, int height, bool enableAlpha, bool vflip)
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
        compute.SetFloat("VFlip", vflip ? 1 : 0);
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

    // Received frames come from the network, so the dimensions are checked
    // before they size any buffer. The decoder reads two pixels from a 32-bit
    // word of the UYVY plane, so the width must be even and the line stride
    // must be a multiple of 4 that holds the row. Padded rows are read with
    // the stride. The alpha plane of UYVA follows the UYVY plane, and its line
    // stride is half of the UYVY one.
    static bool IsDecodable
      (int width, int height, int stride, bool alpha, IntPtr data)
      => data != IntPtr.Zero && width > 0 && height > 0 &&
         (width & 1) == 0 && (stride & 3) == 0 && stride >= width * 2 &&
         (long)stride * height * (alpha ? 3 : 2) / 2 <= int.MaxValue;

    static long DecoderDataSize(int height, int stride, bool alpha)
      => (long)stride * height * (alpha ? 3 : 2) / 2;

    // The warning about a rejected frame is shown once per frame layout, as
    // the frames of a source usually share one.
    (int, int, int, bool) _warnedLayout;

    void WarnRejectedFrame(int width, int height, int stride, bool alpha)
    {
        var layout = (width, height, stride, alpha);
        if (_warnedLayout == layout) return;
        _warnedLayout = layout;
        Debug.LogWarning("[KlakNDI] Unsupported frame: " +
          $"{width}x{height} {(alpha ? "UYVA" : "UYVY")} " +
          $"(line stride {stride} bytes). The width must be even, and the " +
          "line stride must be a multiple of 4 not less than width x 2.");
    }

    // Returns null when the frame is rejected.
    public RenderTexture Decode
      (int width, int height, int stride, bool enableAlpha, IntPtr data)
    {
        if (!IsDecodable(width, height, stride, enableAlpha, data))
        {
            WarnRejectedFrame(width, height, stride, enableAlpha);
            return null;
        }

        // The UYVA data may end in the middle of a 32-bit word.
        var bytes = DecoderDataSize(height, stride, enableAlpha);
        var words = (int)(bytes / 4);
        var tail = (int)(bytes % 4);
        var dataCount = words + (tail > 0 ? 1 : 0);

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
        #if KLAK_NDI_ISSUE200_WORKAROUND
            _decoderOutput = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf);
        #else
            _decoderOutput = new RenderTexture(width, height, 0);
        #endif
            _decoderOutput.enableRandomWrite = true;
            _decoderOutput.Create();
        }

        // Input buffer update
        // The tail bytes are copied separately so that nothing is read
        // beyond the frame data.
        _decoderInput.SetData(data, words, 4);
        if (tail > 0)
        {
            var last = 0u;
            for (var i = 0; i < tail; i++)
                last |= (uint)Marshal.ReadByte(data, words * 4 + i) << (i * 8);
            _decoderInput.SetData(new[] { last }, 0, words, 1);
        }

        // Kenel select
        var pass = (enableAlpha ? 2 : 0);

        // As far as we know, only Metal supports sRGB write to UAV, so we
        // use the linear-color kernels only on Metal
        if (!Util.InGammaMode && Util.UsingMetal) pass++;

        // Decoder compute dispatching
        // Each thread decodes two pixels.
        var compute = _resources.decoderCompute;
        compute.SetInts("FrameSize", width, height);
        compute.SetInt("Stride", stride / 4);
        compute.SetBuffer(pass, "Source", _decoderInput);
        compute.SetTexture(pass, "Destination", _decoderOutput);
        compute.Dispatch(pass, (width / 2 + 7) / 8, GroupsY(height), 1);

        return _decoderOutput;
    }

    #endregion
}

} // namespace Klak.Ndi
