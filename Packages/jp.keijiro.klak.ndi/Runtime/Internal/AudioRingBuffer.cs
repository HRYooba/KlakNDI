using System;

namespace Klak.Ndi {

//
// Audio ring buffer for received audio
//
// The main thread pushes planar frames from NDI, and the audio thread pulls
// interleaved samples at the output rate. The sender's clock and the local
// audio clock drift apart, so the buffered amount is kept between the start
// threshold and the upper limit: on underrun it adds nothing until enough
// samples have accumulated again, and on overrun it drops the oldest ones.
//
sealed class AudioRingBuffer
{
    #region Latency settings (in seconds)

    const float StartLatency = 0.05f;
    const float TrimLatency = 0.1f;
    const float MaxLatency = 0.2f;

    #endregion

    #region Private members

    readonly object _lock = new object();

    float[] _samples = new float[0]; // Interleaved, source channel layout
    int _channels;
    int _sampleRate;
    int _capacity;  // In frames
    int _head;      // Read position (frames)
    int _count;     // Buffered frames
    double _phase;  // Fractional read position for resampling
    bool _primed;

    void Reset(int channels, int sampleRate)
    {
        _channels = channels;
        _sampleRate = sampleRate;
        _capacity = (int)(sampleRate * MaxLatency * 2);
        _samples = new float[_capacity * channels];
        _head = _count = 0;
        _phase = 0;
        _primed = false;
    }

    void Drop(int frames)
    {
        _head = (_head + frames) % _capacity;
        _count -= frames;
    }

    float Sample(int frame, int channel)
      => _samples[((_head + frame) % _capacity) * _channels + channel];

    #endregion

    #region Public methods

    public void Clear()
    {
        lock (_lock)
        {
            _count = 0;
            _phase = 0;
            _primed = false;
        }
    }

    // Frames come from the network, so their headers are checked before they
    // size any buffer or drive any pointer arithmetic.
    const int MinSampleRate = 8000;
    const int MaxSampleRate = 192000;
    const int MaxChannels = 32;

    static bool IsValid(in Interop.AudioFrame frame)
      => frame.Data != System.IntPtr.Zero &&
         frame.SampleRate >= MinSampleRate &&
         frame.SampleRate <= MaxSampleRate &&
         frame.ChannelCount > 0 && frame.ChannelCount <= MaxChannels &&
         frame.SampleCount > 0 &&
         (long)frame.ChannelStride >= (long)frame.SampleCount * 4;

    // Planar 32-bit float samples (main thread)
    public unsafe void Push(in Interop.AudioFrame frame)
    {
        if (!IsValid(frame)) return;

        lock (_lock)
        {
            if (frame.ChannelCount != _channels ||
                frame.SampleRate != _sampleRate)
                Reset(frame.ChannelCount, frame.SampleRate);

            var src = (byte*)frame.Data;
            var frames = Math.Min(frame.SampleCount, _capacity);

            // Make room by dropping the oldest frames.
            var overflow = _count + frames - _capacity;
            if (overflow > 0) Drop(overflow);

            for (var i = 0; i < frames; i++)
            {
                var dst = ((_head + _count + i) % _capacity) * _channels;
                for (var ch = 0; ch < _channels; ch++)
                    _samples[dst + ch] =
                      *(float*)(src + (long)ch * frame.ChannelStride + i * 4L);
            }

            _count += frames;

            // Latency trimming
            var max = (int)(_sampleRate * MaxLatency);
            if (_count > max) Drop(_count - (int)(_sampleRate * TrimLatency));
        }
    }

    // Interpolated source sample mapped to an output channel
    // A mono source goes to the front pair, a source with more channels than
    // the output is averaged into mono output, and extra output channels
    // (beyond the source layout) stay silent.
    float MappedSample(int index, float t, int ch, int channels)
    {
        if (_channels == 1)
            return ch < 2 ? Lerp(index, t, 0) : 0;

        if (channels == 1)
        {
            var sum = 0.0f;
            for (var sc = 0; sc < _channels; sc++) sum += Lerp(index, t, sc);
            return sum / _channels;
        }

        return ch < _channels ? Lerp(index, t, ch) : 0;
    }

    float Lerp(int index, float t, int channel)
    {
        var a = Sample(index, channel);
        var b = Sample(index + 1, channel);
        return a + (b - a) * t;
    }

    // Mixes the buffered samples into the interleaved output (audio thread)
    public void Read(float[] output, int channels, int outputRate)
    {
        if (channels <= 0 || outputRate <= 0) return;

        lock (_lock)
        {
            if (_channels == 0) return;

            if (!_primed)
            {
                if (_count < _sampleRate * StartLatency) return;
                _primed = true;
            }

            var step = (double)_sampleRate / outputRate;
            var frames = output.Length / channels;

            for (var i = 0; i < frames; i++)
            {
                var index = (int)_phase;

                // Underrun: wait for the buffer to fill up again.
                if (index + 1 >= _count)
                {
                    _primed = false;
                    break;
                }

                var t = (float)(_phase - index);
                for (var ch = 0; ch < channels; ch++)
                    output[i * channels + ch] +=
                      MappedSample(index, t, ch, channels);

                _phase += step;
            }

            // Consumed frames
            var consumed = Math.Min((int)_phase, _count);
            Drop(consumed);
            _phase -= consumed;
        }
    }

    #endregion
}

} // namespace Klak.Ndi
