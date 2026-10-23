namespace Aria.Audio;

using System.Diagnostics;

public sealed class NativeClockMapping
{
    private const int Samples = 5;
    private const long NsPerSecond = 1_000_000_000;

    private readonly Func<long> _nativeNowNs;
    private readonly Func<long> _hostTicks;
    private readonly long _frequency;
    private readonly object _gate = new();
    private long _anchorHostTicks;
    private long _anchorNativeNs;
    private long _calibratedAtTicks;
    private bool _calibrated;

    public NativeClockMapping(Func<long> nativeNowNs, Func<long>? hostTicks = null, long frequency = 0)
    {
        _nativeNowNs = nativeNowNs;
        _hostTicks = hostTicks ?? Stopwatch.GetTimestamp;
        _frequency = frequency > 0 ? frequency : Stopwatch.Frequency;
    }

    public long ToHostTicks(long nativeNs)
    {
        lock (_gate)
        {
            if (!_calibrated || _hostTicks() - _calibratedAtTicks > 5 * _frequency)
            {
                Calibrate();
            }
            var delta = nativeNs - _anchorNativeNs;
            return _anchorHostTicks + delta / NsPerSecond * _frequency + delta % NsPerSecond * _frequency / NsPerSecond;
        }
    }

    private void Calibrate()
    {
        var bestWidth = long.MaxValue;
        for (var sample = 0; sample < Samples; sample++)
        {
            var before = _hostTicks();
            var native = _nativeNowNs();
            var after = _hostTicks();
            if (after - before < bestWidth)
            {
                bestWidth = after - before;
                _anchorHostTicks = before + (after - before) / 2;
                _anchorNativeNs = native;
            }
        }
        _calibratedAtTicks = _hostTicks();
        _calibrated = true;
    }
}
