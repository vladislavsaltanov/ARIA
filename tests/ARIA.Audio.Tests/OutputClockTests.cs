namespace Aria.Audio.Tests;

using System.Diagnostics;

public sealed class OutputClockTests
{
    private const int SampleRate = 48000;
    private const int Period = 512;
    private static readonly long T0 = Stopwatch.Frequency * 1000;

    private static long TicksAt(long frame, long firstFrame, double rateHz) =>
        T0 + (long)Math.Round((frame - firstFrame) * (double)Stopwatch.Frequency / rateHz);

    private static long UsAt(long frame, long firstFrame, double rateHz) =>
        HostClock.TicksToUs(T0) + (long)Math.Round((frame - firstFrame) * 1_000_000.0 / rateHz);

    private static OutputClock Feed(long firstFrame, double rateHz, int callbacks, Func<int, long>? delayTicks = null, long flushGeneration = 0, int sinkGeneration = 0)
    {
        var clock = new OutputClock(SampleRate);
        for (var index = 0; index < callbacks; index++)
        {
            var frame = firstFrame + (long)index * Period;
            clock.Observe(new OutputStamp(frame, TicksAt(frame, firstFrame, rateHz) + (delayTicks?.Invoke(index) ?? 0), flushGeneration, sinkGeneration));
        }
        return clock;
    }

    [Fact]
    public void PresentAt_NominalRate_Linear()
    {
        var clock = Feed(0, SampleRate, 100);
        clock.DeviceBufferFrames = 1024;

        Assert.True(clock.IsSettled);
        Assert.True(clock.TryPresentAtUs(60_000, 0, out var hostUs));

        Assert.InRange(hostUs - (UsAt(60_000, 0, SampleRate) + 21_333), -50, 50);
    }

    [Fact]
    public void PresentAt_DriftedDac_TracksRate()
    {
        var rate = SampleRate * (1 + 80e-6);
        var clock = Feed(0, rate, 290);

        Assert.InRange(clock.RateHz, rate - 0.5, rate + 0.5);
        var ahead = 289L * Period + 4096;
        Assert.True(clock.TryPresentAtUs(ahead, 0, out var hostUs));
        Assert.InRange(hostUs - UsAt(ahead, 0, rate), -50, 50);
    }

    [Fact]
    public void PresentAt_JitteryCallbacks_UsesLowerEnvelope()
    {
        var random = new Random(42);
        var clock = Feed(0, SampleRate, 290, index => index % 2 == 0 ? 0 : (long)(random.NextDouble() * 0.002 * Stopwatch.Frequency));

        var ahead = 289L * Period + 4096;
        Assert.True(clock.TryPresentAtUs(ahead, 0, out var hostUs));
        Assert.InRange(hostUs - UsAt(ahead, 0, SampleRate), -200, 200);
    }

    [Fact]
    public void PresentAt_BeyondInt32Frames_Exact()
    {
        const long first = 3_000_000_000;
        var clock = Feed(first, SampleRate, 200);

        var target = first + 199L * Period + 2048;
        Assert.True(clock.TryPresentAtUs(target, 0, out var hostUs));
        Assert.InRange(hostUs - UsAt(target, first, SampleRate), -50, 50);
    }

    [Fact]
    public void FlushGeneration_ResetsModel_AndUnsettles()
    {
        var clock = Feed(0, SampleRate, 50);
        Assert.True(clock.IsSettled);

        clock.Observe(new OutputStamp(200_000, TicksAt(200_000, 0, SampleRate), 1, 0));

        Assert.False(clock.IsSettled);
        Assert.False(clock.TryPresentAtUs(200_000, 0, out _));
        clock.Observe(new OutputStamp(200_512, TicksAt(200_512, 0, SampleRate), 1, 0));
        Assert.True(clock.IsSettled);
        Assert.True(clock.TryPresentAtUs(201_024, 0, out var hostUs));
        Assert.InRange(hostUs - UsAt(201_024, 0, SampleRate), -50, 50);
    }

    [Fact]
    public void SinkGeneration_Mismatch_ReturnsFalse()
    {
        var clock = Feed(0, SampleRate, 50, sinkGeneration: 2);

        Assert.False(clock.TryPresentAtUs(30_000, 1, out _));
        Assert.True(clock.TryPresentAtUs(30_000, 2, out _));
    }

    [Fact]
    public void HallTrim_ShiftsPresentation()
    {
        var clock = Feed(0, SampleRate, 50);
        Assert.True(clock.TryPresentAtUs(30_000, 0, out var plain));

        clock.HallTrimMs = 12.5;

        Assert.True(clock.TryPresentAtUs(30_000, 0, out var trimmed));
        Assert.Equal(12_500, trimmed - plain);
    }

    [Fact]
    public void Duplicates_Ignored()
    {
        var clock = new OutputClock(SampleRate);
        var stamp = new OutputStamp(0, T0, 0, 0);

        clock.Observe(stamp);
        clock.Observe(stamp);

        Assert.False(clock.IsSettled);
    }
}
