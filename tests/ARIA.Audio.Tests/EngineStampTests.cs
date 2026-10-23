namespace Aria.Audio.Tests;

using System.Diagnostics;

public sealed class EngineStampTests
{
    [Fact]
    public void Stamp_NullSink_False()
    {
        using var engine = new AriaAudioEngine(new SyntheticSourceFactory(48000), sink: new NullSink(48000, 2));

        Assert.False(engine.TryReadOutputStamp(out _));
    }

    [Fact]
    public void Stamp_ClockSink_MappedToHostTicks()
    {
        var sink = new ClockSink { NativeOffsetNs = 7_000_000_000 };
        using var engine = new AriaAudioEngine(new SyntheticSourceFactory(48000), sink: sink);
        var hostTicks = Stopwatch.GetTimestamp();
        sink.Stamp = (4800, HostTicksToNs(hostTicks) + sink.NativeOffsetNs, 3);

        Assert.True(engine.TryReadOutputStamp(out var stamp));

        Assert.Equal(4800, stamp.Frame);
        Assert.Equal(3, stamp.FlushGeneration);
        Assert.Equal(0, stamp.SinkGeneration);
        Assert.InRange(stamp.HostTicks, hostTicks - Stopwatch.Frequency / 1000, hostTicks + Stopwatch.Frequency / 1000);
        Assert.Equal(1024, engine.DeviceBufferFrames);
    }

    [Fact]
    public async Task SinkSwap_IncrementsGeneration()
    {
        var first = new ClockSink { Stamp = (0, 1, 0) };
        using var engine = new AriaAudioEngine(new SyntheticSourceFactory(48000), sink: first);
        Assert.True(engine.TryReadOutputStamp(out var before));

        var second = new ClockSink { Stamp = (0, 1, 0) };
        engine.ReplaceSink(second);

        var deadline = Environment.TickCount64 + 5000;
        OutputStamp after;
        while (!engine.TryReadOutputStamp(out after) || after.SinkGeneration == before.SinkGeneration)
        {
            Assert.True(Environment.TickCount64 < deadline, "sink generation did not change");
            await Task.Delay(10);
        }
        Assert.Equal(before.SinkGeneration + 1, after.SinkGeneration);
    }

    private static long HostTicksToNs(long ticks) =>
        ticks / Stopwatch.Frequency * 1_000_000_000 + ticks % Stopwatch.Frequency * 1_000_000_000 / Stopwatch.Frequency;

    private sealed class ClockSink : IAudioSink, IOutputClockSource
    {
        public long NativeOffsetNs { get; init; }

        public (long Frame, long Ns, long Generation) Stamp { get; set; }

        public int SampleRate => 48000;

        public int Channels => 2;

        public int DeviceBufferFrames => 1024;

        public int Write(ReadOnlySpan<float> samples)
        {
            Thread.Sleep(1);
            return samples.Length;
        }

        public void Flush()
        {
        }

        public bool TryReadStamp(out long frame, out long nativeNs, out long flushGeneration)
        {
            (frame, nativeNs, flushGeneration) = Stamp;
            return true;
        }

        public long NativeNowNs() => HostTicksToNs(Stopwatch.GetTimestamp()) + NativeOffsetNs;
    }
}
