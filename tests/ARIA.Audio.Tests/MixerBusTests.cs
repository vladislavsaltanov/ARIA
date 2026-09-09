namespace Aria.Audio.Tests;

using System.Collections.Immutable;
using Aria.Core.Model;
using Aria.Core.Playback;

public sealed class MixerBusTests
{
    [Fact]
    public void SingleVoice_PeakMatchesSourceAmplitude_AndOutputIsNonZero()
    {
        using var bus = new MixerBus(1, 48000, 512);
        var output = new float[512];
        bus.AddVoice(Config(new SineSource(1, 48000, 1000, 0.5)));

        bus.Render(output);

        Assert.InRange(bus.Peak, 0.45, 0.55);
        Assert.Contains(output, sample => sample != 0f);
    }

    [Fact]
    public void TwoVoices_PeakSums_AndRemoveVoiceDropsContribution()
    {
        using var bus = new MixerBus(1, 48000, 512);
        var output = new float[512];
        var first = bus.AddVoice(Config(new SineSource(1, 48000, 750, 0.5)));
        bus.AddVoice(Config(new SineSource(1, 48000, 3750, 0.3)));

        bus.Render(output);
        Assert.InRange(bus.Peak, 0.75, 0.85);

        bus.RemoveVoice(first);
        bus.Render(output);

        Assert.InRange(bus.Peak, 0.25, 0.35);
    }

    [Fact]
    public void Crossfade_OldVoiceFadesOutWithStopWhenDone_NewVoiceFadesInAndSurvives()
    {
        using var bus = new MixerBus(1, 48000, 50);
        var events = new List<StreamEvent>();
        bus.Events += events.Add;
        var output = new float[50];
        var fade = new Fade(TimeSpan.FromSeconds(100.0 / 48000), FadeCurve.Linear);
        var first = bus.AddVoice(Config(new SineSource(1, 48000, 440, 0.5)));
        var second = bus.AddVoice(Config(new SineSource(1, 48000, 880, 0.3), fadeIn: fade));
        bus.SetMix(first, new MixParameters(0.0, new FadeSpec(TimeSpan.FromSeconds(100.0 / 48000), FadeCurve.Linear, -80.0, true)));

        for (var block = 0; block < 3; block++)
        {
            bus.Render(output);
        }

        var ended = Assert.Single(events);
        Assert.Equal(first, ended.Handle);
        Assert.Equal(StreamEventKind.Ended, ended.Kind);
        Assert.Equal(StreamEndReason.FadeCompleted, ended.Reason);

        bus.Render(output);

        Assert.InRange(bus.Peak, 0.25, 0.5);
        Assert.Single(events);
    }

    [Fact]
    public void Pause_SuspendsVoice_PositionStands_AndMarkerDoesNotFire()
    {
        using var bus = new MixerBus(1, 48000, 512);
        var events = new List<StreamEvent>();
        bus.Events += events.Add;
        var output = new float[512];
        var markers = ImmutableArray.Create(new MarkerSpec("stop", TimeSpan.FromSeconds(0.02), MarkerAction.Stop));
        var handle = bus.AddVoice(Config(new SineSource(1, 48000, 1000, 0.5), markers: markers));

        bus.Render(output);
        Assert.InRange(bus.Peak, 0.45, 0.55);
        Assert.Empty(events);

        bus.Transport(handle, TransportCommand.Pause);
        for (var block = 0; block < 3; block++)
        {
            bus.Render(output);
            Assert.Equal(0f, bus.Peak);
        }
        Assert.Empty(events);

        bus.Transport(handle, TransportCommand.Play);
        bus.Render(output);

        var ended = Assert.Single(events);
        Assert.Equal(handle, ended.Handle);
        Assert.Equal(StreamEndReason.StoppedByMarker, ended.Reason);

        bus.Render(output);
        Assert.Equal(0f, bus.Peak);
    }

    [Fact]
    public void MarkerStop_FiresExactlyWhenPositionPasses_AndSilencesVoice()
    {
        using var bus = new MixerBus(1, 48000, 512);
        var events = new List<StreamEvent>();
        bus.Events += events.Add;
        var output = new float[512];
        var markers = ImmutableArray.Create(new MarkerSpec("half", TimeSpan.FromSeconds(0.5), MarkerAction.Stop));
        bus.AddVoice(Config(new SineSource(1, 48000, 1000, 0.5), markers: markers));

        var frames = 0;
        while (events.Count == 0 && frames < 48000)
        {
            bus.Render(output);
            frames += 512;
        }

        Assert.Equal(StreamEndReason.StoppedByMarker, Assert.Single(events).Reason);
        Assert.True(frames >= 24000, $"ended too early at {frames} frames");
        Assert.True(frames - 512 < 24000, $"ended too late at {frames} frames");
        Assert.NotEqual(0f, output[447]);
        Assert.Equal(0f, output[448]);
        Assert.Equal(0f, output[511]);

        bus.Render(output);
        Assert.Equal(0f, bus.Peak);
    }

    [Fact]
    public void CueOut_FiresCueOutReached_AndRemovesVoice()
    {
        using var bus = new MixerBus(1, 48000, 512);
        var events = new List<StreamEvent>();
        bus.Events += events.Add;
        var output = new float[512];
        bus.AddVoice(Config(new SineSource(1, 48000, 1000, 0.5), cueOut: TimeSpan.FromSeconds(0.25)));

        var frames = 0;
        while (events.Count == 0 && frames < 48000)
        {
            bus.Render(output);
            frames += 512;
        }

        Assert.Equal(StreamEndReason.CueOutReached, Assert.Single(events).Reason);
        Assert.True(frames >= 12000, $"ended too early at {frames} frames");
        Assert.True(frames - 512 < 12000, $"ended too late at {frames} frames");

        bus.Render(output);
        Assert.Equal(0f, bus.Peak);
    }

    [Fact]
    public void FiniteSource_EndsWithCompleted_AtLastRenderedFrame()
    {
        using var bus = new MixerBus(1, 48000, 512);
        var events = new List<StreamEvent>();
        bus.Events += events.Add;
        var output = new float[512];
        bus.AddVoice(Config(new FiniteSource(1, 48000, 1000, 0.5, 1200)));

        bus.Render(output);
        bus.Render(output);
        Assert.Empty(events);

        bus.Render(output);

        var ended = Assert.Single(events);
        Assert.Equal(StreamEndReason.Completed, ended.Reason);
        Assert.NotEqual(0f, output[175]);
        Assert.Equal(0f, output[176]);
        Assert.Equal(0f, output[511]);

        bus.Render(output);
        Assert.Equal(0f, bus.Peak);
    }

    [Fact]
    public void Render_NoAllocations_InSteadyState()
    {
        using var bus = new MixerBus(2, 48000, 256);
        bus.AddVoice(Config(new SineSource(2, 48000, 440, 0.5)));
        var output = new float[512];

        for (var warmup = 0; warmup < 200; warmup++)
        {
            bus.Render(output);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var block = 0; block < 1000; block++)
        {
            bus.Render(output);
        }
        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }

    [Fact]
    public async Task ControlThread_AndRenderThread_Cooperate()
    {
        using var bus = new MixerBus(2, 48000, 256);
        var endedCount = 0;
        bus.Events += _ => Interlocked.Increment(ref endedCount);
        var output = new float[512];

        var renderTask = Task.Run(() =>
        {
            for (var block = 0; block < 5000; block++)
            {
                bus.Render(output);
            }
        });
        var controlTask = Task.Run(() =>
        {
            for (var index = 0; index < 100; index++)
            {
                var handle = bus.AddVoice(Config(new SineSource(2, 48000, 200 + index, 0.4)));
                bus.Transport(handle, index % 3 == 0 ? TransportCommand.Stop : index % 3 == 1 ? TransportCommand.Pause : TransportCommand.Play);
                bus.RemoveVoice(handle);
            }
        });

        var all = Task.WhenAll(renderTask, controlTask);
        var finished = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.True(finished == all, "MixerBus multithreaded test timed out");
        await all;
    }

    private static VoiceConfig Config(
        ISampleSource source,
        double gainDb = 0.0,
        Fade? fadeIn = null,
        ImmutableArray<MarkerSpec>? markers = null,
        TimeSpan cueIn = default,
        TimeSpan? cueOut = null)
        => new(source, gainDb, fadeIn, null, markers ?? ImmutableArray<MarkerSpec>.Empty, cueIn, cueOut);

    private sealed class FiniteSource : ISampleSource
    {
        private readonly SineSource _inner;
        private int _remaining;

        public FiniteSource(int channels, int sampleRate, double frequency, double amplitude, int totalFrames)
        {
            _inner = new SineSource(channels, sampleRate, frequency, amplitude);
            Channels = channels;
            SampleRate = sampleRate;
            _remaining = totalFrames;
        }

        public int Channels { get; }

        public int SampleRate { get; }

        public int ReadFrames(Span<float> destination)
        {
            var frames = Math.Min(_remaining, destination.Length / Channels);
            if (frames <= 0)
            {
                return 0;
            }
            var read = _inner.ReadFrames(destination.Slice(0, frames * Channels));
            _remaining -= read;
            return read;
        }
    }
}
