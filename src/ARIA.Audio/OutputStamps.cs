namespace Aria.Audio;

using System.Diagnostics;

public interface IOutputClockSource
{
    int DeviceBufferFrames { get; }

    bool TryReadStamp(out long frame, out long nativeNs, out long flushGeneration);

    long NativeNowNs();
}

public readonly record struct OutputStamp(long Frame, long HostTicks, long FlushGeneration, int SinkGeneration);

public interface IOutputStampSource
{
    int DeviceBufferFrames { get; }

    bool TryReadOutputStamp(out OutputStamp stamp);
}

public static class HostClock
{
    public static long NowUs() => TicksToUs(Stopwatch.GetTimestamp());

    public static long TicksToUs(long ticks)
    {
        var frequency = Stopwatch.Frequency;
        return ticks / frequency * 1_000_000 + ticks % frequency * 1_000_000 / frequency;
    }

    public static long UsToTicks(long us)
    {
        var frequency = Stopwatch.Frequency;
        return us / 1_000_000 * frequency + us % 1_000_000 * frequency / 1_000_000;
    }
}
