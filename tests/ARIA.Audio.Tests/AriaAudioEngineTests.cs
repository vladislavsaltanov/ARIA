namespace Aria.Audio.Tests;

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.Runtime;
using Aria.Core.State;

public sealed class AriaAudioEngineTests
{
    [Fact]
    public async Task Play_EndToEnd_StreamsAudio_AndPublishesMonitorPosition()
    {
        using var stack = new Stack("sine.flac", "sine.flac");
        stack.Submit(new Play());

        await Poll(() => stack.Transport.Status == TransportStatus.Playing, "transport never started playing");
        Assert.Equal(stack.Tracks[0].Id, stack.Transport.Current!.TrackId);

        await Poll(() => stack.Sink.TotalFrames > 0, "sink never received audio");
        var frames = stack.Sink.TotalFrames;
        await Poll(() => stack.Sink.TotalFrames > frames + 1000, "sink stopped receiving audio");

        await Poll(() => stack.Monitor.Latest is not null, "monitor never published");
        var before = stack.Monitor.Latest!;
        await Poll(() => stack.Monitor.Latest is { } after && after.FilePosition > before.FilePosition, "monitor position stalled");
    }

    [Fact]
    public async Task BrokenTrack_StopsWithoutAdvance()
    {
        using var stack = new Stack("broken.flac", "sine.flac");
        stack.Submit(new Play());

        await Poll(
            () => stack.Transport.Faulted.Contains(stack.Tracks[0].Id),
            "controller did not flag broken track");

        Assert.Null(stack.Transport.Current);
        Assert.Equal(TransportStatus.Stopped, stack.Transport.Status);
        Assert.Contains(stack.EngineEvents, e => e.Kind == StreamEventKind.Faulted);
    }

    [Fact]
    public async Task Panic_SilencesEverything_AndRecoversOnPlay()
    {
        using var stack = new Stack("sine.flac");
        stack.Submit(new Play());
        await Poll(() => stack.Transport.Status == TransportStatus.Playing && stack.Sink.TotalFrames > 0, "transport never started playing");

        stack.Submit(new Panic());
        await Poll(() => stack.Transport.Status == TransportStatus.Panicked, "transport never panicked");

        var framesAtPanic = stack.Sink.TotalFrames;
        await Poll(
            () => BlocksFrom(stack.Sink, framesAtPanic).All(IsSilent),
            "panic did not silence output");

        var framesAfterSilence = stack.Sink.TotalFrames;
        stack.Submit(new Play());
        await Poll(() => stack.Transport.Status == TransportStatus.Playing, "transport did not recover after panic");
        await Poll(
            () => BlocksFrom(stack.Sink, framesAfterSilence).Any(IsAudible),
            "no audible signal after panic recovery");
    }

    [Fact]
    public async Task Stop_StopsTransport_AndClearsMonitor()
    {
        using var stack = new Stack("sine.flac");
        stack.Submit(new Play());
        await Poll(() => stack.Transport.Status == TransportStatus.Playing && stack.Sink.TotalFrames > 0, "transport never started playing");

        stack.Submit(new Stop());
        await Poll(() => stack.Transport.Status == TransportStatus.Stopped, "transport never stopped");
        await Poll(() => stack.Monitor.Latest is null, "monitor was not cleared after stop");
    }

    [Fact]
    public async Task MasterGain_AppliesToOutput()
    {
        using var stack = new Stack("sine.flac");
        stack.Submit(new Play());
        await Poll(() => stack.Sink.TotalFrames > 0, "sink never received audio");

        stack.Submit(new SetMasterGain(-6));
        await Poll(
            () => RecentBlocks(stack.Sink, 8).Length == 8 && RecentBlocks(stack.Sink, 8).All(b => InRange(Peak(b), 0.20, 0.30)),
            "master gain -6 did not reach output");

        stack.Submit(new SetMasterGain(0));
        await Poll(
            () => RecentBlocks(stack.Sink, 8).Length == 8 && RecentBlocks(stack.Sink, 8).All(b => InRange(Peak(b), 0.45, 0.55)),
            "master gain 0 did not reach output");
    }

    [Fact]
    public async Task Meter_PublishesLufs_WhilePlaying()
    {
        var sink = new CapturingSink(8000, 2);
        var meters = new MeterMonitor();
        using var engine = new AriaAudioEngine(new SyntheticSourceFactory(8000, 2), null, 8000, 2, 128, sink, meters);
        var handle = engine.StartStream(
            new TrackSource("/audio/sine.flac", TimeSpan.Zero, null),
            new StreamOptions(StreamBus.Main, []));
        engine.Transport(handle, TransportCommand.Play);

        var deadline = Environment.TickCount64 + 10_000;
        while (meters.Latest is not { } snapshot || snapshot.MomentaryLufs <= -60.0)
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException("meter never published signal");
            }
            await Task.Delay(10);
        }

        Assert.InRange(meters.Latest.MomentaryLufs, -9.0, -5.0);
        engine.DisposeStream(handle);
    }

    [Fact]
    public async Task Meter_PublishesStereoPeaks_WhilePlaying()
    {
        var sink = new CapturingSink(8000, 2);
        var meters = new MeterMonitor();
        using var engine = new AriaAudioEngine(new SyntheticSourceFactory(8000, 2), null, 8000, 2, 128, sink, meters);
        var handle = engine.StartStream(
            new TrackSource("/audio/sine.flac", TimeSpan.Zero, null),
            new StreamOptions(StreamBus.Main, []));
        engine.Transport(handle, TransportCommand.Play);

        var deadline = Environment.TickCount64 + 10_000;
        while (meters.Latest is not { } snapshot || snapshot.PeakLeft <= 0.1)
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException("meter never published channel peaks");
            }
            await Task.Delay(10);
        }

        Assert.InRange(meters.Latest.PeakLeft, 0.3, 0.7);
        Assert.InRange(meters.Latest.PeakRight, 0.3, 0.7);
        engine.DisposeStream(handle);
    }

    [Fact]
    public async Task FiniteTrack_EndsWithAdvance()
    {
        using var stack = new Stack("finite.flac", "sine.flac");
        stack.Submit(new Play());

        await Poll(() => stack.Transport.Current?.TrackId == stack.Tracks[0].Id, "first track never started");
        await Poll(() => stack.Transport.Current?.TrackId == stack.Tracks[1].Id, "finite track did not advance to next");
        Assert.Equal(TransportStatus.Playing, stack.Transport.Status);
    }

    [Fact]
    public async Task Next_WithCrossfade_KeepsPlaying()
    {
        var first = TestTracks.Track("sine.flac", fadeOut: new Fade(TimeSpan.FromSeconds(0.2), FadeCurve.Linear));
        var second = TestTracks.Track("sine.flac");
        using var stack = new Stack(first, second);
        stack.Submit(new Play());
        await Poll(() => stack.Transport.Current?.TrackId == first.Id, "first track never started");

        stack.Submit(new Next());

        await Poll(
            () => stack.Transport.Current?.TrackId == second.Id && stack.Transport.Status == TransportStatus.Playing,
            "next did not start second track while playing");
    }

    private static async Task Poll(Func<bool> condition, string message)
    {
        var deadline = Environment.TickCount64 + 10_000;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException(message);
            }
            await Task.Delay(10);
        }
    }

    private static float[][] BlocksFrom(CapturingSink sink, int fromFrame)
    {
        var blocks = sink.Blocks;
        var result = new List<float[]>();
        var cursor = 0;
        foreach (var block in blocks)
        {
            if (cursor >= fromFrame)
            {
                result.Add(block);
            }
            cursor += block.Length / 2;
        }
        return [.. result];
    }

    private static float[][] RecentBlocks(CapturingSink sink, int count)
    {
        var blocks = sink.Blocks;
        var start = Math.Max(0, blocks.Count - count);
        var result = new float[blocks.Count - start][];
        for (var index = start; index < blocks.Count; index++)
        {
            result[index - start] = blocks[index];
        }
        return result;
    }

    private static bool InRange(float value, double low, double high) => value >= low && value <= high;

    private static float Peak(float[] block)
    {
        var peak = 0f;
        foreach (var sample in block)
        {
            var magnitude = Math.Abs(sample);
            if (magnitude > peak)
            {
                peak = magnitude;
            }
        }
        return peak;
    }

    private static bool IsSilent(float[] block)
    {
        foreach (var sample in block)
        {
            if (sample != 0f)
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsAudible(float[] block) => !IsSilent(block);

    private sealed class Stack : IDisposable
    {
        public static readonly ClientId Client = new("audio-e2e");

        public CapturingSink Sink { get; }
        public PlaybackMonitor Monitor { get; } = new();
        public AriaAudioEngine Engine { get; }
        public CommandBus Bus { get; }
        public List<StreamEvent> EngineEvents { get; } = [];
        public ImmutableArray<Track> Tracks { get; }
        public TransportState Transport => Bus.Snapshot().Transport;

        private readonly StrongBox<CommandBus> _busHolder = new();
        private long _seq;

        public Stack(params Track[] tracks)
            : this((tracks, [.. tracks.Select(t => new ProjectEntry(EntryId.New(), t.Id))]))
        {
        }

        public Stack(params string[] files)
            : this(BuildShow(files))
        {
        }

        private static (Track[] Tracks, ImmutableArray<ProjectEntry> Entries) BuildShow(string[] files)
        {
            var tracks = files.Select(f => TestTracks.Track(f)).ToArray();
            return (tracks, [.. tracks.Select(t => new ProjectEntry(EntryId.New(), t.Id))]);
        }

        private Stack((Track[] Tracks, ImmutableArray<ProjectEntry> Entries) show)
        {
            Tracks = [.. show.Tracks];
            Sink = new CapturingSink(8000, 2);
            Engine = new AriaAudioEngine(new SyntheticSourceFactory(8000, 2), Monitor, sampleRate: 8000, channels: 2, blockSizeFrames: 128, Sink);
            Engine.Events += RecordEngineEvent;
            Action<Action> marshal = work => _busHolder.Value!.Post(work);
            var controller = new ShowController(Engine, Monitor, marshal);
            Bus = new CommandBus(controller, BusMode.Pumped);
            _busHolder.Value = Bus;

            var project = new Project(ProjectId.New(), "Main", show.Entries);
            Bus.Submit(Client, NextSeq(), new LoadShow(Tracks, [project], project.Id));
        }

        public void Submit(Command command) => Bus.Submit(Client, NextSeq(), command);

        public long NextSeq() => ++_seq;

        public void Dispose()
        {
            Bus.Dispose();
            Engine.Dispose();
        }

        private void RecordEngineEvent(StreamEvent e)
        {
            lock (EngineEvents)
            {
                EngineEvents.Add(e);
            }
        }
    }
}
