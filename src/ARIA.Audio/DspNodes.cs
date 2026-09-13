namespace Aria.Audio;

using Aria.Core.Model;

public interface IDspNode
{
    void Process(Span<float> samples, int channels);
}

public sealed class GainNode : IDspNode
{
    private long _linearBits;

    public GainNode(double initialDb) => SetGainDb(initialDb);

    public void SetGainDb(double db)
    {
        var linear = Math.Pow(10.0, db / 20.0);
        Volatile.Write(ref _linearBits, BitConverter.DoubleToInt64Bits(linear));
    }

    public void Process(Span<float> samples, int channels)
    {
        var gain = (float)BitConverter.Int64BitsToDouble(Volatile.Read(ref _linearBits));
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] *= gain;
        }
    }
}

public sealed class FaderNode : IDspNode
{
    private readonly FadeCurve _curve;
    private readonly double _from;
    private readonly double _to;
    private readonly double _step;
    private double _t;

    public FaderNode(int rampFrames, FadeCurve curve, double fromLinear, double toLinear, bool stopWhenDone)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rampFrames);
        RampFrames = rampFrames;
        StopWhenDone = stopWhenDone;
        _curve = curve;
        _from = fromLinear;
        _to = toLinear;
        Level = fromLinear;
        if (rampFrames == 0)
        {
            _step = 1.0;
            HasCompleted = true;
            _t = 1.0;
        }
        else
        {
            _step = 1.0 / rampFrames;
        }
    }

    public int RampFrames { get; }

    public bool StopWhenDone { get; }

    public bool HasCompleted { get; private set; }

    public double Level { get; private set; }

    public void Process(Span<float> samples, int channels)
    {
        var frames = samples.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var gain = (float)(_from + (_to - _from) * FadeCurves.Evaluate(_curve, _t));
            Level = gain;
            for (var channel = 0; channel < channels; channel++)
            {
                samples[frame * channels + channel] *= gain;
            }
            if (!HasCompleted)
            {
                _t += _step;
                if (_t >= 1.0)
                {
                    _t = 1.0;
                    HasCompleted = true;
                    Level = _to;
                }
            }
        }
    }
}

public sealed class MeterNode : IDspNode
{
    public float Peak { get; private set; }

    public float Rms { get; private set; }

    public float RunningPeak { get; private set; }

    public void Process(Span<float> samples, int channels)
    {
        if (samples.IsEmpty)
        {
            return;
        }
        double sum = 0;
        float peak = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var value = samples[i];
            var magnitude = Math.Abs(value);
            if (magnitude > peak)
            {
                peak = magnitude;
            }
            sum += value * value;
        }
        Peak = peak;
        Rms = (float)Math.Sqrt(sum / samples.Length);
        if (peak > RunningPeak)
        {
            RunningPeak = peak;
        }
    }
}

public sealed class BiquadFilter
{
    private readonly double _b0;
    private readonly double _b1;
    private readonly double _b2;
    private readonly double _a1;
    private readonly double _a2;
    private double _x1;
    private double _x2;
    private double _y1;
    private double _y2;

    public BiquadFilter(double b0, double b1, double b2, double a1, double a2)
    {
        _b0 = b0;
        _b1 = b1;
        _b2 = b2;
        _a1 = a1;
        _a2 = a2;
    }

    public static BiquadFilter KWeightingPreFilter(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        const double f0 = 1681.974450955533;
        const double gainDb = 3.986843164122726;
        const double q = 0.7071752369554196;
        var k = Math.Tan(Math.PI * f0 / sampleRate);
        var vh = Math.Pow(10.0, gainDb / 20.0);
        var vb = Math.Pow(vh, 0.4996667741545416);
        var a0 = 1.0 + k / q + k * k;
        return new BiquadFilter(
            (vh + vb * k / q + k * k) / a0,
            2.0 * (k * k - vh) / a0,
            (vh - vb * k / q + k * k) / a0,
            2.0 * (k * k - 1.0) / a0,
            (1.0 - k / q + k * k) / a0);
    }

    public static BiquadFilter KWeightingHighPass(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        const double f0 = 38.13547087602444;
        const double q = 0.5006351026612103;
        var k = Math.Tan(Math.PI * f0 / sampleRate);
        var a0 = 1.0 + k / q + k * k;
        return new BiquadFilter(
            1.0 / a0,
            -2.0 / a0,
            1.0 / a0,
            2.0 * (k * k - 1.0) / a0,
            (1.0 - k / q + k * k) / a0);
    }

    public double ProcessSample(double x)
    {
        var y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
        _x2 = _x1;
        _x1 = x;
        _y2 = _y1;
        _y1 = y;
        return y;
    }

    public void Reset()
    {
        _x1 = 0.0;
        _x2 = 0.0;
        _y1 = 0.0;
        _y2 = 0.0;
    }
}

public sealed class LufsMeter : IDspNode
{
    private const double ReferenceOffsetDb = -0.691;
    private const double FloorLufs = -70.0;

    private readonly int _channels;
    private readonly BiquadFilter[] _pre;
    private readonly BiquadFilter[] _post;
    private readonly float[] _ring;
    private double _sum;
    private int _position;
    private int _count;

    public LufsMeter(int channels, int sampleRate, TimeSpan window)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window));
        }
        _channels = channels;
        _pre = new BiquadFilter[channels];
        _post = new BiquadFilter[channels];
        for (var channel = 0; channel < channels; channel++)
        {
            _pre[channel] = BiquadFilter.KWeightingPreFilter(sampleRate);
            _post[channel] = BiquadFilter.KWeightingHighPass(sampleRate);
        }
        _ring = new float[Math.Max(1, (int)(window.TotalSeconds * sampleRate))];
    }

    public double MomentaryLufs
    {
        get
        {
            if (_count == 0)
            {
                return FloorLufs;
            }
            var mean = _sum / _count;
            if (!(mean > 0.0))
            {
                return FloorLufs;
            }
            var lufs = ReferenceOffsetDb + 10.0 * Math.Log10(mean);
            return lufs < FloorLufs ? FloorLufs : lufs;
        }
    }

    public void Process(Span<float> samples, int channels)
    {
        if (channels != _channels)
        {
            throw new ArgumentException("Channel count does not match the meter.", nameof(channels));
        }
        var frames = samples.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var energy = 0.0;
            for (var channel = 0; channel < channels; channel++)
            {
                var filtered = _post[channel].ProcessSample(_pre[channel].ProcessSample(samples[frame * channels + channel]));
                energy += filtered * filtered;
            }
            _sum += energy - _ring[_position];
            _ring[_position] = (float)energy;
            _position++;
            if (_position == _ring.Length)
            {
                _position = 0;
            }
            if (_count < _ring.Length)
            {
                _count++;
            }
        }
    }

    public void Reset()
    {
        Array.Clear(_ring);
        _sum = 0.0;
        _position = 0;
        _count = 0;
        foreach (var filter in _pre)
        {
            filter.Reset();
        }
        foreach (var filter in _post)
        {
            filter.Reset();
        }
    }
}
