namespace Aria.Audio;

public sealed class SevenBandEq : IDspNode
{
    public const int BandCount = 7;

    private const double MinGainDb = -15.0;
    private const double MaxGainDb = 15.0;
    private const double MinQ = 0.1;
    private const double MaxQ = 10.0;

    private readonly float[] _frequencies;
    private readonly int _channels;
    private readonly int _sampleRate;
    private readonly long[] _gainBits = new long[BandCount];
    private readonly long[] _qBits = new long[BandCount];
    private readonly double[] _x1;
    private readonly double[] _x2;
    private readonly double[] _y1;
    private readonly double[] _y2;

    public SevenBandEq(float[] frequencies, int channels, int sampleRate)
    {
        if (frequencies is null || frequencies.Length != BandCount)
        {
            throw new ArgumentException($"Exactly {BandCount} band frequencies are required.", nameof(frequencies));
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        for (var band = 0; band < BandCount; band++)
        {
            if (!(frequencies[band] > 0.0) || !(frequencies[band] < sampleRate / 2.0))
            {
                throw new ArgumentOutOfRangeException(nameof(frequencies));
            }
        }
        _frequencies = (float[])frequencies.Clone();
        _channels = channels;
        _sampleRate = sampleRate;
        var zeroBits = BitConverter.DoubleToInt64Bits(0.0);
        var qBits = BitConverter.DoubleToInt64Bits(1.0);
        for (var band = 0; band < BandCount; band++)
        {
            _gainBits[band] = zeroBits;
            _qBits[band] = qBits;
        }
        _x1 = new double[BandCount * channels];
        _x2 = new double[BandCount * channels];
        _y1 = new double[BandCount * channels];
        _y2 = new double[BandCount * channels];
    }

    public void SetBand(int index, double gainDb, double q)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (index >= BandCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        if (gainDb is < MinGainDb or > MaxGainDb)
        {
            throw new ArgumentOutOfRangeException(nameof(gainDb));
        }
        if (q is < MinQ or > MaxQ)
        {
            throw new ArgumentOutOfRangeException(nameof(q));
        }
        Volatile.Write(ref _gainBits[index], BitConverter.DoubleToInt64Bits(gainDb));
        Volatile.Write(ref _qBits[index], BitConverter.DoubleToInt64Bits(q));
    }

    public void Process(Span<float> samples, int channels)
    {
        if (channels != _channels)
        {
            throw new ArgumentException("Channel count does not match the equalizer.", nameof(channels));
        }
        Span<double> b0 = stackalloc double[BandCount];
        Span<double> b1 = stackalloc double[BandCount];
        Span<double> b2 = stackalloc double[BandCount];
        Span<double> a1 = stackalloc double[BandCount];
        Span<double> a2 = stackalloc double[BandCount];
        Span<bool> active = stackalloc bool[BandCount];
        for (var band = 0; band < BandCount; band++)
        {
            var gainDb = BitConverter.Int64BitsToDouble(Volatile.Read(ref _gainBits[band]));
            if (Math.Abs(gainDb) < 1e-9)
            {
                active[band] = false;
                continue;
            }
            active[band] = true;
            var q = BitConverter.Int64BitsToDouble(Volatile.Read(ref _qBits[band]));
            Peaking(_frequencies[band], gainDb, q, _sampleRate, out b0[band], out b1[band], out b2[band], out a1[band], out a2[band]);
        }
        var frames = samples.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            for (var channel = 0; channel < channels; channel++)
            {
                var value = (double)samples[frame * channels + channel];
                for (var band = 0; band < BandCount; band++)
                {
                    if (!active[band])
                    {
                        continue;
                    }
                    var slot = band * _channels + channel;
                    var out_ = b0[band] * value + b1[band] * _x1[slot] + b2[band] * _x2[slot]
                        - a1[band] * _y1[slot] - a2[band] * _y2[slot];
                    _x2[slot] = _x1[slot];
                    _x1[slot] = value;
                    _y2[slot] = _y1[slot];
                    _y1[slot] = out_;
                    value = out_;
                }
                samples[frame * channels + channel] = (float)value;
            }
        }
    }

    private static void Peaking(double frequency, double gainDb, double q, int sampleRate,
        out double b0, out double b1, out double b2, out double a1, out double a2)
    {
        var a = Math.Pow(10.0, gainDb / 40.0);
        var w0 = 2.0 * Math.PI * frequency / sampleRate;
        var alpha = Math.Sin(w0) / (2.0 * q);
        var cos = Math.Cos(w0);
        var a0 = 1.0 + alpha / a;
        b0 = (1.0 + alpha * a) / a0;
        b1 = -2.0 * cos / a0;
        b2 = (1.0 - alpha * a) / a0;
        a1 = -2.0 * cos / a0;
        a2 = (1.0 - alpha / a) / a0;
    }
}

public sealed class SimpleLimiter : IDspNode
{
    private const double MinThresholdDb = -24.0;
    private const double MaxThresholdDb = 0.0;
    private const double MinReleaseMs = 10.0;
    private const double MaxReleaseMs = 1000.0;
    private const double AttackMs = 1.0;

    private readonly int _channels;
    private readonly int _sampleRate;
    private long _thresholdBits = BitConverter.DoubleToInt64Bits(-1.0);
    private long _releaseMsBits = BitConverter.DoubleToInt64Bits(100.0);
    private double _envelope;
    private double _gain = 1.0;

    public SimpleLimiter(int channels, int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        _channels = channels;
        _sampleRate = sampleRate;
    }

    public void SetParams(double thresholdDb, double releaseMs)
    {
        if (thresholdDb is < MinThresholdDb or > MaxThresholdDb)
        {
            throw new ArgumentOutOfRangeException(nameof(thresholdDb));
        }
        if (releaseMs is < MinReleaseMs or > MaxReleaseMs)
        {
            throw new ArgumentOutOfRangeException(nameof(releaseMs));
        }
        Volatile.Write(ref _thresholdBits, BitConverter.DoubleToInt64Bits(thresholdDb));
        Volatile.Write(ref _releaseMsBits, BitConverter.DoubleToInt64Bits(releaseMs));
    }

    public void Process(Span<float> samples, int channels)
    {
        if (channels != _channels)
        {
            throw new ArgumentException("Channel count does not match the limiter.", nameof(channels));
        }
        var ceiling = Math.Pow(10.0, BitConverter.Int64BitsToDouble(Volatile.Read(ref _thresholdBits)) / 20.0);
        var releaseMs = BitConverter.Int64BitsToDouble(Volatile.Read(ref _releaseMsBits));
        var attack = Math.Exp(-1.0 / (AttackMs / 1000.0 * _sampleRate));
        var release = Math.Exp(-1.0 / (releaseMs / 1000.0 * _sampleRate));
        var envelope = _envelope;
        var gain = _gain;
        var frames = samples.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var peak = 0.0;
            for (var channel = 0; channel < channels; channel++)
            {
                var magnitude = Math.Abs(samples[frame * channels + channel]);
                if (magnitude > peak)
                {
                    peak = magnitude;
                }
            }
            var follow = peak > envelope ? attack : release;
            envelope = follow * envelope + (1.0 - follow) * peak;
            var target = envelope > ceiling ? ceiling / envelope : 1.0;
            if (target < gain)
            {
                gain = attack * gain + (1.0 - attack) * target;
            }
            else
            {
                gain = release * gain + (1.0 - release) * target;
                if (1.0 - gain < 1e-9)
                {
                    gain = 1.0;
                }
            }
            var applied = (float)gain;
            for (var channel = 0; channel < channels; channel++)
            {
                samples[frame * channels + channel] *= applied;
            }
        }
        _envelope = envelope;
        _gain = gain;
    }
}

public sealed class PanNode : IDspNode
{
    private long _panBits;

    public PanNode(double pan)
    {
        Validate(pan);
        _panBits = BitConverter.DoubleToInt64Bits(pan);
    }

    public void SetPan(double pan)
    {
        Validate(pan);
        Volatile.Write(ref _panBits, BitConverter.DoubleToInt64Bits(pan));
    }

    public void Process(Span<float> samples, int channels)
    {
        if (channels != 2)
        {
            throw new ArgumentException("Pan requires stereo.", nameof(channels));
        }
        var pan = BitConverter.Int64BitsToDouble(Volatile.Read(ref _panBits));
        var angle = (pan + 1.0) * Math.PI / 4.0;
        var left = (float)Math.Cos(angle);
        var right = (float)Math.Sin(angle);
        for (var frame = 0; frame < samples.Length / 2; frame++)
        {
            samples[frame * 2] *= left;
            samples[frame * 2 + 1] *= right;
        }
    }

    private static void Validate(double pan)
    {
        if (pan is < -1.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(pan));
        }
    }
}

public sealed class HighPassNode : IDspNode
{
    private const double MaxFrequencyHz = 400.0;

    private readonly int _channels;
    private readonly int _sampleRate;
    private readonly double[] _x1;
    private readonly double[] _x2;
    private readonly double[] _y1;
    private readonly double[] _y2;
    private long _frequencyBits;

    public HighPassNode(double hz, int channels, int sampleRate)
    {
        Validate(hz);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        _channels = channels;
        _sampleRate = sampleRate;
        _frequencyBits = BitConverter.DoubleToInt64Bits(hz);
        _x1 = new double[channels];
        _x2 = new double[channels];
        _y1 = new double[channels];
        _y2 = new double[channels];
    }

    public void SetFrequency(double hz)
    {
        Validate(hz);
        Volatile.Write(ref _frequencyBits, BitConverter.DoubleToInt64Bits(hz));
    }

    public void Process(Span<float> samples, int channels)
    {
        if (channels != _channels)
        {
            throw new ArgumentException("Channel count does not match the filter.", nameof(channels));
        }
        var hz = BitConverter.Int64BitsToDouble(Volatile.Read(ref _frequencyBits));
        if (hz == 0.0)
        {
            return;
        }
        var w0 = 2.0 * Math.PI * hz / _sampleRate;
        var alpha = Math.Sin(w0) / (2.0 * 0.7071067811865476);
        var cos = Math.Cos(w0);
        var a0 = 1.0 + alpha;
        var b0 = (1.0 + cos) / 2.0 / a0;
        var b1 = -(1.0 + cos) / a0;
        var b2 = b0;
        var a1 = -2.0 * cos / a0;
        var a2 = (1.0 - alpha) / a0;
        var frames = samples.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            for (var channel = 0; channel < channels; channel++)
            {
                var x = samples[frame * channels + channel];
                var y = b0 * x + b1 * _x1[channel] + b2 * _x2[channel] - a1 * _y1[channel] - a2 * _y2[channel];
                _x2[channel] = _x1[channel];
                _x1[channel] = x;
                _y2[channel] = _y1[channel];
                _y1[channel] = y;
                samples[frame * channels + channel] = (float)y;
            }
        }
    }

    private static void Validate(double hz)
    {
        if (hz is < 0.0 or > MaxFrequencyHz)
        {
            throw new ArgumentOutOfRangeException(nameof(hz));
        }
    }
}

public sealed class MonoSumNode : IDspNode
{
    private int _enabled;

    public bool Enabled
    {
        get => Volatile.Read(ref _enabled) == 1;
        set => Volatile.Write(ref _enabled, value ? 1 : 0);
    }

    public void Process(Span<float> samples, int channels)
    {
        if (Volatile.Read(ref _enabled) == 0)
        {
            return;
        }
        if (channels != 2)
        {
            throw new ArgumentException("Mono sum requires stereo.", nameof(channels));
        }
        for (var frame = 0; frame < samples.Length / 2; frame++)
        {
            var mixed = (samples[frame * 2] + samples[frame * 2 + 1]) * 0.5f;
            samples[frame * 2] = mixed;
            samples[frame * 2 + 1] = mixed;
        }
    }
}
