namespace Aria.Audio;

using System.Diagnostics;

public sealed class OutputClock
{
    private const double WindowSeconds = 3.0;
    private const double EnvelopePercentile = 0.1;

    private readonly int _sampleRate;
    private readonly List<(long Frame, long Ticks)> _stamps = [];
    private long _flushGeneration = -1;
    private int _sinkGeneration = -1;
    private double _meanFrame;
    private double _meanTicks;
    private double _ticksPerFrame;
    private double _envelopeShift;
    private long _hallTrimBits;
    private double[] _residuals = new double[512];

    public OutputClock(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        _sampleRate = sampleRate;
        _ticksPerFrame = (double)Stopwatch.Frequency / sampleRate;
    }

    public int DeviceBufferFrames { get; set; }

    public double HallTrimMs
    {
        get => BitConverter.Int64BitsToDouble(Volatile.Read(ref _hallTrimBits));
        set => Volatile.Write(ref _hallTrimBits, BitConverter.DoubleToInt64Bits(value));
    }

    public bool IsSettled => _stamps.Count >= 2;

    public double RateHz => Stopwatch.Frequency / _ticksPerFrame;

    public void Observe(OutputStamp stamp)
    {
        if (stamp.SinkGeneration != _sinkGeneration || stamp.FlushGeneration != _flushGeneration)
        {
            _stamps.Clear();
            _sinkGeneration = stamp.SinkGeneration;
            _flushGeneration = stamp.FlushGeneration;
        }
        if (_stamps.Count > 0)
        {
            var last = _stamps[^1];
            if (stamp.Frame == last.Frame)
            {
                return;
            }
            if (stamp.Frame < last.Frame)
            {
                _stamps.Clear();
            }
        }
        _stamps.Add((stamp.Frame, stamp.HostTicks));
        var oldest = stamp.HostTicks - (long)(WindowSeconds * Stopwatch.Frequency);
        var expired = 0;
        while (expired < _stamps.Count - 2 && _stamps[expired].Ticks < oldest)
        {
            expired++;
        }
        if (expired > 0)
        {
            _stamps.RemoveRange(0, expired);
        }
        Fit();
    }

    public bool TryPresentAtUs(long outputFrame, int sinkGeneration, out long hostUs)
    {
        if (!IsSettled || sinkGeneration != _sinkGeneration)
        {
            hostUs = 0;
            return false;
        }
        var ticks = _meanTicks + _envelopeShift + _ticksPerFrame * (outputFrame - _meanFrame);
        var bufferUs = DeviceBufferFrames * 1_000_000.0 / _sampleRate;
        hostUs = HostClock.TicksToUs((long)Math.Round(ticks)) + (long)Math.Round(bufferUs + HallTrimMs * 1000.0);
        return true;
    }

    private void Fit()
    {
        var count = _stamps.Count;
        if (count < 2)
        {
            return;
        }
        var baseFrame = _stamps[0].Frame;
        var baseTicks = _stamps[0].Ticks;
        double sumFrame = 0;
        double sumTicks = 0;
        foreach (var (frame, ticks) in _stamps)
        {
            sumFrame += frame - baseFrame;
            sumTicks += ticks - baseTicks;
        }
        var meanFrame = sumFrame / count;
        var meanTicks = sumTicks / count;
        double covariance = 0;
        double variance = 0;
        foreach (var (frame, ticks) in _stamps)
        {
            var dx = frame - baseFrame - meanFrame;
            covariance += dx * (ticks - baseTicks - meanTicks);
            variance += dx * dx;
        }
        if (variance <= 0)
        {
            return;
        }
        var slope = covariance / variance;
        if (_residuals.Length < count)
        {
            _residuals = new double[count * 2];
        }
        var residuals = _residuals.AsSpan(0, count);
        for (var index = 0; index < count; index++)
        {
            var (frame, ticks) = _stamps[index];
            residuals[index] = ticks - baseTicks - meanTicks - slope * (frame - baseFrame - meanFrame);
        }
        residuals.Sort();
        _ticksPerFrame = slope;
        _meanFrame = baseFrame + meanFrame;
        _meanTicks = baseTicks + meanTicks;
        _envelopeShift = residuals[(int)Math.Floor(EnvelopePercentile * (count - 1))];
    }
}
