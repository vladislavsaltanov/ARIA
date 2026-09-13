namespace Aria.Audio.Tests;

using System.Collections.Immutable;
using Aria.Core.Model;
using Aria.Core.Playback;

public sealed class MixerSmoothingTests
{
    private static VoiceConfig Config(ISampleSource source) =>
        new(source, 0.0, null, null, ImmutableArray<MarkerSpec>.Empty, TimeSpan.Zero, null);

    [Fact]
    public void Pause_WithSmoothing_FadesOutThenSilences()
    {
        using var bus = new MixerBus(1, 48000, 512);
        bus.SetSmoothing(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(50), true);
        var output = new float[512];
        var handle = bus.AddVoice(Config(new SineSource(1, 48000, 1000, 0.5)));

        bus.Render(output);
        var loud = bus.Peak;
        Assert.InRange(loud, 0.45, 0.55);

        bus.Transport(handle, TransportCommand.Pause);
        bus.Render(output);
        Assert.InRange(bus.Peak, 0f, loud);

        for (var block = 0; block < 12; block++)
        {
            bus.Render(output);
        }
        Assert.Equal(0f, bus.Peak);

        bus.Render(output);
        Assert.Equal(0f, bus.Peak);
        Assert.True(bus.TryGetPosition(handle, out _));
    }

    [Fact]
    public void Resume_WithSmoothing_FadesIn()
    {
        using var bus = new MixerBus(1, 48000, 512);
        bus.SetSmoothing(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100), true);
        var output = new float[512];
        var handle = bus.AddVoice(Config(new SineSource(1, 48000, 1000, 0.5)));
        bus.Render(output);

        bus.Transport(handle, TransportCommand.Pause);
        for (var block = 0; block < 12; block++)
        {
            bus.Render(output);
        }
        Assert.Equal(0f, bus.Peak);

        bus.Transport(handle, TransportCommand.Play);
        bus.Render(output);
        var first = bus.Peak;
        Assert.InRange(first, 0f, 0.2f);

        for (var block = 0; block < 12; block++)
        {
            bus.Render(output);
        }
        Assert.InRange(bus.Peak, 0.45, 0.55);
    }

    [Fact]
    public void Pause_Disabled_StopsInstantly()
    {
        using var bus = new MixerBus(1, 48000, 512);
        var output = new float[512];
        var handle = bus.AddVoice(Config(new SineSource(1, 48000, 1000, 0.5)));
        bus.Render(output);
        Assert.InRange(bus.Peak, 0.45, 0.55);

        bus.Transport(handle, TransportCommand.Pause);
        bus.Render(output);

        Assert.Equal(0f, bus.Peak);
    }

    [Fact]
    public void SetSmoothing_CanToggleAtRuntime()
    {
        using var bus = new MixerBus(1, 48000, 512);
        var output = new float[512];
        var handle = bus.AddVoice(Config(new SineSource(1, 48000, 1000, 0.5)));
        bus.Render(output);

        bus.SetSmoothing(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100), false);
        bus.Transport(handle, TransportCommand.Pause);
        bus.Render(output);
        Assert.Equal(0f, bus.Peak);

        bus.SetSmoothing(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(480), true);
        bus.Transport(handle, TransportCommand.Play);
        bus.Render(output);
        Assert.InRange(bus.Peak, 0f, 0.2f);
    }

    [Fact]
    public void SetMix_FadeIn_RampsFromSilence()
    {
        using var bus = new MixerBus(1, 48000, 512);
        var output = new float[512];
        var handle = bus.AddVoice(Config(new SineSource(1, 48000, 1000, 0.5)));
        bus.Render(output);
        Assert.InRange(bus.Peak, 0.45, 0.55);

        bus.SetMix(handle, new MixParameters(0.0, new FadeSpec(TimeSpan.FromMilliseconds(100), FadeCurve.Linear, 0.0, false)));
        bus.Render(output);
        Assert.InRange(bus.Peak, 0f, 0.2f);

        for (var block = 0; block < 12; block++)
        {
            bus.Render(output);
        }
        Assert.InRange(bus.Peak, 0.45, 0.55);
    }

    [Fact]
    public void Crossfade_ViaSetMix_BothVoicesOverlapMidTransition()
    {
        using var bus = new MixerBus(1, 48000, 50);
        var events = new List<StreamEvent>();
        bus.Events += events.Add;
        var output = new float[50];
        var fade = TimeSpan.FromSeconds(100.0 / 48000);
        var old = bus.AddVoice(Config(new SineSource(1, 48000, 440, 0.5)));
        var fresh = bus.AddVoice(Config(new SineSource(1, 48000, 880, 0.3)));
        bus.Render(output);

        bus.SetMix(old, new MixParameters(0.0, new FadeSpec(fade, FadeCurve.Linear, -80.0, true)));
        bus.SetMix(fresh, new MixParameters(0.0, new FadeSpec(fade, FadeCurve.Linear, 0.0, false)));
        bus.Render(output);

        Assert.Empty(events);
        Assert.InRange(bus.Peak, 0.05, 0.8);
        Assert.True(bus.TryGetPosition(old, out _));
        Assert.True(bus.TryGetPosition(fresh, out _));

        for (var block = 0; block < 3; block++)
        {
            bus.Render(output);
        }

        var ended = Assert.Single(events);
        Assert.Equal(old, ended.Handle);
        Assert.Equal(StreamEventKind.Ended, ended.Kind);
        Assert.Equal(StreamEndReason.FadeCompleted, ended.Reason);

        bus.Render(output);
        Assert.InRange(bus.Peak, 0.25, 0.35);
    }

    [Fact]
    public void PauseFade_NoAllocations_InSteadyState()
    {
        using var bus = new MixerBus(1, 48000, 256);
        bus.SetSmoothing(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100), true);
        bus.AddVoice(Config(new SineSource(1, 48000, 440, 0.5)));
        var output = new float[256];

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
}
