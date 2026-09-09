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

    public void Process(Span<float> samples, int channels)
    {
        var frames = samples.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var gain = (float)(_from + (_to - _from) * FadeCurves.Evaluate(_curve, _t));
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
