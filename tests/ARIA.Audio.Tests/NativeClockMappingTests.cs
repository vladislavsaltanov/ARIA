namespace Aria.Audio.Tests;

public sealed class NativeClockMappingTests
{
    private const long Frequency = 10_000_000;

    [Fact]
    public void Mapping_ConstantOffset_Exact()
    {
        long t = 1_000_000;
        var mapping = new NativeClockMapping(() => t++ * 100 + 12_345, () => t++, Frequency);

        Assert.Equal(2_000_000, mapping.ToHostTicks(2_000_000L * 100 + 12_345));
        Assert.Equal(1_000_500, mapping.ToHostTicks(1_000_500L * 100 + 12_345));
    }

    [Fact]
    public void Mapping_PicksNarrowestSandwich()
    {
        long t = 0;
        var preemptions = new Queue<long>([400, 0, 300, 200, 500]);
        long Native()
        {
            var value = t * 100;
            t += 1 + (preemptions.Count > 0 ? preemptions.Dequeue() : 0);
            return value;
        }
        var mapping = new NativeClockMapping(Native, () => t++, Frequency);

        Assert.Equal(10_000, mapping.ToHostTicks(10_000L * 100));
    }

    [Fact]
    public void HostClock_TicksAndMicroseconds_RoundTrip()
    {
        var ticks = System.Diagnostics.Stopwatch.Frequency * 3_600L * 30 + 12_345;

        Assert.Equal(ticks / System.Diagnostics.Stopwatch.Frequency, HostClock.TicksToUs(ticks) / 1_000_000);
        Assert.InRange(HostClock.UsToTicks(HostClock.TicksToUs(ticks)), ticks - System.Diagnostics.Stopwatch.Frequency / 1_000_000 - 1, ticks);
    }
}
