namespace Aria.Audio.Tests;

using System.Diagnostics;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.State;

public sealed class OutputStallTests
{
    private const int Rate = 8000;
    private const int Block = 128;

    [Fact]
    public void BoundedRingWrite_NoSpace_GivesUpWithinTimeout()
    {
        var samples = new float[Block * 2];
        var clock = Stopwatch.StartNew();

        var accepted = RingWriter.Write(samples, 2, Block, TimeSpan.FromMilliseconds(30), () => 0, (_, frames) => frames);

        Assert.Equal(0, accepted);
        Assert.InRange(clock.ElapsedMilliseconds, 0, 1000);
    }

    [Fact]
    public void BoundedRingWrite_ZeroTimeout_WritesWhatFitsWithoutWaiting()
    {
        var samples = new float[Block * 2];
        var free = 32;

        var accepted = RingWriter.Write(samples, 2, Block, TimeSpan.Zero, () => free, (_, frames) =>
        {
            free -= frames;
            return frames;
        });

        Assert.Equal(32 * 2, accepted);
    }

    [Fact]
    public void BoundedRingWrite_SpaceAppears_WritesEverything()
    {
        var samples = new float[Block * 2];
        var space = 0;
        var calls = 0;

        var accepted = RingWriter.Write(samples, 2, Block, TimeSpan.FromSeconds(5), () => ++calls > 3 ? Block : space, (_, frames) => frames);

        Assert.Equal(samples.Length, accepted);
    }

    [Fact]
    public async Task StalledPreviewSink_KeepsMainOutputFlowing()
    {
        var main = new CapturingSink(Rate, 2);
        var preview = new StallSink(Rate, 2) { Stalled = true };
        using var engine = new AriaAudioEngine(new SyntheticSourceFactory(Rate, 2), null, Rate, 2, Block, main, null, preview);
        var handle = engine.StartStream(new TrackSource("/audio/sine.flac", TimeSpan.Zero, null), new StreamOptions(StreamBus.Main, []));
        engine.Transport(handle, TransportCommand.Play);

        await Poll(() => main.TotalFrames > Rate, "main output starved by a stalled preview device");
    }

    [Fact]
    public async Task StalledMainSink_HoldsPosition_ReportsStall_AndResumesOnReplacement()
    {
        var monitor = new PlaybackMonitor();
        var main = new StallSink(Rate, 2);
        using var engine = new AriaAudioEngine(new SyntheticSourceFactory(Rate, 2), monitor, Rate, 2, Block, main, null, new NullSink(Rate, 2));
        var stalls = new List<bool>();
        engine.MainOutputStalled += stalled =>
        {
            lock (stalls)
            {
                stalls.Add(stalled);
            }
        };
        var handle = engine.StartStream(new TrackSource("/audio/sine.flac", TimeSpan.Zero, null), new StreamOptions(StreamBus.Main, []));
        monitor.Bind(handle, new DeckContent(null, TrackId.New(), "sine", null, EndAction.Pause, TimeSpan.FromMinutes(5), TimeSpan.Zero));
        engine.Transport(handle, TransportCommand.Play);
        await Poll(() => main.AcceptedFrames > Block * 4 && monitor.Latest is not null, "main never played");

        main.Stalled = true;
        await Poll(() => Snapshot(stalls).Contains(true), "stall never reported");
        var frozen = monitor.Latest!.FilePosition;
        await Task.Delay(300);
        Assert.InRange((monitor.Latest!.FilePosition - frozen).TotalMilliseconds, 0, 100);

        var replacement = new CapturingSink(Rate, 2);
        engine.ReplaceSink(replacement);

        await Poll(() => replacement.TotalFrames > Block * 4, "replacement sink never received audio while old one stalled");
        await Poll(() => Snapshot(stalls).LastOrDefault() == false, "recovery never reported");
    }

    [Fact]
    public async Task RenderFault_IsReported_AndRenderingContinues()
    {
        var main = new FaultOnceSink(Rate, 2);
        using var engine = new AriaAudioEngine(new SyntheticSourceFactory(Rate, 2), null, Rate, 2, Block, main, null, new NullSink(Rate, 2));
        var faults = 0;
        engine.RenderFaulted += _ => Interlocked.Increment(ref faults);
        main.Armed = true;

        await Poll(() => Volatile.Read(ref faults) == 1 && main.WritesAfterFault > 10, "render thread died on a sink exception");
    }

    private static List<bool> Snapshot(List<bool> stalls)
    {
        lock (stalls)
        {
            return [.. stalls];
        }
    }

    private static async Task Poll(Func<bool> condition, string message)
    {
        var deadline = Environment.TickCount64 + 5_000;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException(message);
            }
            await Task.Delay(10);
        }
    }

    private sealed class StallSink(int sampleRate, int channels) : IAudioSink
    {
        private long _accepted;

        public volatile bool Stalled;

        public long AcceptedFrames => Interlocked.Read(ref _accepted);

        public int SampleRate { get; } = sampleRate;

        public int Channels { get; } = channels;

        public int Write(ReadOnlySpan<float> samples)
        {
            if (Stalled)
            {
                Thread.Sleep(2);
                return 0;
            }
            Thread.Sleep(1);
            Interlocked.Add(ref _accepted, samples.Length / Channels);
            return samples.Length;
        }

        public void Flush()
        {
        }
    }

    private sealed class FaultOnceSink(int sampleRate, int channels) : IAudioSink
    {
        private int _faulted;
        private int _after;

        public volatile bool Armed;

        public int WritesAfterFault => Volatile.Read(ref _after);

        public int SampleRate { get; } = sampleRate;

        public int Channels { get; } = channels;

        public int Write(ReadOnlySpan<float> samples)
        {
            if (Armed && Interlocked.Exchange(ref _faulted, 1) == 0)
            {
                throw new InvalidOperationException("device glitch");
            }
            if (Volatile.Read(ref _faulted) == 1)
            {
                Interlocked.Increment(ref _after);
            }
            Thread.Sleep(1);
            return samples.Length;
        }

        public void Flush()
        {
        }
    }
}
