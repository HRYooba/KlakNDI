using System;

namespace Klak.Ndi {

//
// Audio ring buffer for received audio
//
// The main thread pushes planar frames from NDI, and the audio thread pulls
// interleaved samples at the output rate. The sender's clock and the local
// audio clock drift apart, so the buffered amount is kept between the start
// threshold and the upper limit: on underrun it outputs silence until enough
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

    // Planar 32-bit float samples (main thread)
    public unsafe void Push(in Interop.AudioFrame frame)
    {
        if (frame.ChannelCount <= 0 || frame.SampleCount <= 0) return;

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
                      *(float*)(src + ch * frame.ChannelStride + i * 4);
            }

            _count += frames;

            // Latency trimming
            var max = (int)(_sampleRate * MaxLatency);
            if (_count > max) Drop(_count - (int)(_sampleRate * TrimLatency));
        }
    }

    // Interleaved output samples (audio thread)
    public void Read(float[] output, int channels, int outputRate)
    {
        Array.Clear(output, 0, output.Length);
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
                {
                    // Channels beyond the source layout repeat it
                    // (mono to both sides, for example).
                    var sc = ch % _channels;
                    var a = Sample(index, sc);
                    var b = Sample(index + 1, sc);
                    output[i * channels + ch] = a + (b - a) * t;
                }

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
