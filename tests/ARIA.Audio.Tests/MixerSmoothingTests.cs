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
        bus.SetSmoothing(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(50), TimeSpan.Zero, true);
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
        bus.SetSmoothing(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100), TimeSpan.Zero, true);
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

        bus.SetSmoothing(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100), TimeSpan.Zero, false);
        bus.Transport(handle, TransportCommand.Pause);
        bus.Render(output);
        Assert.Equal(0f, bus.Peak);

        bus.SetSmoothing(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(480), TimeSpan.Zero, true);
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
    public void PrerollOverlap_MidTransition_ContainsBothVoices()
    {
        const int rate = 48000;
        const int block = 512;
        var fade = TimeSpan.FromMilliseconds(100);
        var leadBlocks = 4;

        var combined = RenderOverlap(rate, block, fade, leadBlocks, out var combinedMid);
        var oldSolo = RenderSolo(rate, block, fade, leadBlocks, 440, true);
        var newSolo = RenderSolo(rate, block, fade, leadBlocks, 880, false);

        Assert.True(combined, "old voice died before overlap could render");
        Assert.True(combinedMid > oldSolo * 1.25f);
        Assert.True(combinedMid > newSolo * 1.25f);
    }

    [Fact]
    public void FadeOut_MidFadeIn_StartsFromCurrentLevel_NoJump()
    {
        using var bus = new MixerBus(1, 48000, 512);
        var output = new float[512];
        var handle = bus.AddVoice(Config(new SineSource(1, 48000, 440, 0.5)));
        bus.SetMix(handle, new MixParameters(0.0, new FadeSpec(TimeSpan.FromMilliseconds(100), FadeCurve.Linear, 0.0, false)));
        for (var block = 0; block < 2; block++)
        {
            bus.Render(output);
        }
        var partial = bus.Peak;

        bus.SetMix(handle, new MixParameters(0.0, new FadeSpec(TimeSpan.FromMilliseconds(100), FadeCurve.Linear, -80.0, true)));
        bus.Render(output);

        Assert.InRange(bus.Peak, 0f, partial * 1.1f);
    }

    private static bool RenderOverlap(int rate, int block, TimeSpan fade, int leadBlocks, out float midRms)
    {
        using var bus = new MixerBus(1, rate, block);
        var events = new List<StreamEvent>();
        bus.Events += events.Add;
        var output = new float[block];
        var old = bus.AddVoice(Config(new SineSource(1, rate, 440, 0.5)));
        bus.Render(output);
        bus.SetMix(old, new MixParameters(0.0, new FadeSpec(fade, FadeCurve.Linear, -80.0, true)));
        var fresh = bus.AddVoice(Config(new SineSource(1, rate, 880, 0.5)));
        bus.SetMix(fresh, new MixParameters(0.0, new FadeSpec(fade, FadeCurve.Linear, 0.0, false)));
        for (var blockIndex = 0; blockIndex < leadBlocks; blockIndex++)
        {
            bus.Render(output);
        }
        bus.Render(output);
        midRms = Rms(output);
        return events.Count == 0
            && bus.TryGetPosition(old, out _)
            && bus.TryGetPosition(fresh, out _);
    }

    private static float RenderSolo(int rate, int block, TimeSpan fade, int leadBlocks, double frequency, bool fadeOut)
    {
        using var bus = new MixerBus(1, rate, block);
        var output = new float[block];
        var handle = bus.AddVoice(Config(new SineSource(1, rate, frequency, 0.5)));
        if (fadeOut)
        {
            bus.Render(output);
        }
        bus.SetMix(handle, fadeOut
            ? new MixParameters(0.0, new FadeSpec(fade, FadeCurve.Linear, -80.0, true))
            : new MixParameters(0.0, new FadeSpec(fade, FadeCurve.Linear, 0.0, false)));
        for (var blockIndex = 0; blockIndex < leadBlocks; blockIndex++)
        {
            bus.Render(output);
        }
        bus.Render(output);
        return Rms(output);
    }

    private static float Rms(float[] output)
    {
        double sum = 0;
        for (var index = 0; index < output.Length; index++)
        {
            sum += output[index] * output[index];
        }
        return (float)Math.Sqrt(sum / output.Length);
    }

    [Fact]
    public void Seek_WithSmoothing_DipsToSilenceThenFadesBack()
    {
        using var bus = new MixerBus(1, 48000, 512);
        bus.SetSmoothing(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromMilliseconds(100), true);
        var output = new float[512];
        var handle = bus.AddVoice(Config(new SineSource(1, 48000, 440, 0.5)));
        bus.Render(output);
        Assert.InRange(bus.Peak, 0.45, 0.55);

        bus.Seek(handle, 24000);
        bus.Render(output);
        var dipping = bus.Peak;
        Assert.InRange(dipping, 0f, 0.5f);

        for (var block = 0; block < 12; block++)
        {
            bus.Render(output);
        }
        Assert.InRange(bus.Peak, 0.45, 0.55);
        Assert.True(bus.TryGetPosition(handle, out var position));
        Assert.True(position >= TimeSpan.FromSeconds(0.5));
    }

    [Fact]
    public void Seek_WithSmoothing_Seamless_NoSampleJump()
    {
        const int rate = 48000;
        const int block = 512;
        using var bus = new MixerBus(1, rate, block);
        bus.SetSmoothing(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromMilliseconds(20), true);
        var handle = bus.AddVoice(Config(new SineSource(1, rate, 440, 0.5)));
        var output = new float[block];
        bus.Render(output);

        bus.Seek(handle, rate * 10);
        var tail = output[^1];
        var worst = 0f;
        for (var i = 0; i < 40; i++)
        {
            bus.Render(output);
            worst = Math.Max(worst, Math.Abs(output[0] - tail));
            for (var s = 1; s < output.Length; s++)
            {
                worst = Math.Max(worst, Math.Abs(output[s] - output[s - 1]));
            }
            tail = output[^1];
        }
        Assert.InRange(worst, 0f, 0.05f);
    }

    [Fact]
    public void Seek_Disabled_JumpsInstantly()
    {
        using var bus = new MixerBus(1, 48000, 512);
        bus.SetSmoothing(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromMilliseconds(100), false);
        var output = new float[512];
        var handle = bus.AddVoice(Config(new SineSource(1, 48000, 440, 0.5)));
        bus.Render(output);

        bus.Seek(handle, 24000);
        bus.Render(output);

        Assert.True(bus.TryGetPosition(handle, out var position));
        Assert.Equal(TimeSpan.FromSeconds(0.5 + 512.0 / 48000), position);
    }

    [Fact]
    public void PauseFade_NoAllocations_InSteadyState()
    {
        using var bus = new MixerBus(1, 48000, 256);
        bus.SetSmoothing(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100), TimeSpan.Zero, true);
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
