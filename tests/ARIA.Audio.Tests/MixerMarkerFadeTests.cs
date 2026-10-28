namespace Aria.Audio.Tests;

using System.Collections.Immutable;
using Aria.Core.Model;
using Aria.Core.Playback;

public sealed class MixerMarkerFadeTests
{
    private const int Rate = 48000;
    private const int Block = 512;
    private const int MarkerFrame = 24000;

    private static VoiceConfig Config(MarkerAction action) =>
        new(
            new SineSource(1, Rate, 1000, 0.5),
            0.0,
            null,
            null,
            [new MarkerSpec("half", TimeSpan.FromSeconds(0.5), action)],
            TimeSpan.Zero,
            null);

    private static MixerBus Bus(bool enabled, double markerFadeMs)
    {
        var bus = new MixerBus(1, Rate, Block);
        bus.SetSmoothing(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(50), enabled, TimeSpan.FromMilliseconds(markerFadeMs));
        return bus;
    }

    private static List<float> RenderUntilEnded(MixerBus bus, List<StreamEvent> events)
    {
        var peaks = new List<float>();
        var output = new float[Block];
        while (events.Count == 0 && peaks.Count < 200)
        {
            bus.Render(output);
            peaks.Add(bus.Peak);
        }
        return peaks;
    }

    [Fact]
    public void MarkerFade_VoiceFadesOutBeforePauseMarker()
    {
        using var bus = Bus(true, 200);
        var events = new List<StreamEvent>();
        bus.Events += events.Add;
        bus.AddVoice(Config(MarkerAction.Pause));

        var peaks = RenderUntilEnded(bus, events);

        var ended = Assert.Single(events);
        Assert.Equal(StreamEndReason.StoppedByMarker, ended.Reason);
        Assert.Equal(TimeSpan.FromSeconds(0.5), ended.MarkerPosition);
        Assert.Equal(MarkerFrame / Block + 1, peaks.Count);
        var fadeStartBlock = (MarkerFrame - 9600) / Block;
        Assert.InRange(peaks[fadeStartBlock - 1], 0.45f, 0.55f);
        for (var block = fadeStartBlock + 1; block < peaks.Count - 1; block++)
        {
            Assert.True(peaks[block] <= peaks[block - 1] + 1e-6f, $"block {block} rose: {peaks[block - 1]} -> {peaks[block]}");
        }
        Assert.InRange(peaks[(MarkerFrame - 4800) / Block], 0.2f, 0.3f);
        Assert.InRange(peaks[^2], 0f, 0.08f);
        Assert.InRange(peaks[^1], 0f, 0.03f);
    }

    [Fact]
    public void MarkerFade_DisabledWithoutSmoothing_CutsAtFullLevel()
    {
        using var bus = Bus(false, 200);
        var events = new List<StreamEvent>();
        bus.Events += events.Add;
        bus.AddVoice(Config(MarkerAction.Pause));

        var peaks = RenderUntilEnded(bus, events);

        Assert.Equal(StreamEndReason.StoppedByMarker, Assert.Single(events).Reason);
        Assert.InRange(peaks[^2], 0.45f, 0.55f);
    }

    [Fact]
    public void MarkerFade_ZeroDuration_CutsAtFullLevel()
    {
        using var bus = Bus(true, 0);
        var events = new List<StreamEvent>();
        bus.Events += events.Add;
        bus.AddVoice(Config(MarkerAction.Pause));

        var peaks = RenderUntilEnded(bus, events);

        Assert.InRange(peaks[^2], 0.45f, 0.55f);
    }

    [Fact]
    public void MarkerFade_JumpMarker_KeepsFullLevel()
    {
        using var bus = Bus(true, 200);
        var events = new List<StreamEvent>();
        bus.Events += events.Add;
        bus.AddVoice(Config(MarkerAction.RepeatFromPrevious));

        var peaks = RenderUntilEnded(bus, events);

        Assert.Equal(StreamEndReason.StoppedByMarker, Assert.Single(events).Reason);
        Assert.InRange(peaks[^2], 0.45f, 0.55f);
    }

    [Fact]
    public void MarkerFade_SeekBack_RestoresLevelAndRearms()
    {
        using var bus = Bus(true, 200);
        var events = new List<StreamEvent>();
        bus.Events += events.Add;
        var handle = bus.AddVoice(Config(MarkerAction.Pause));
        var output = new float[Block];
        for (var block = 0; block < (MarkerFrame - 2400) / Block; block++)
        {
            bus.Render(output);
        }
        Assert.InRange(bus.Peak, 0f, 0.2f);

        bus.Seek(handle, 0);
        for (var block = 0; block < 6; block++)
        {
            bus.Render(output);
        }
        Assert.InRange(bus.Peak, 0.45f, 0.55f);

        var peaks = RenderUntilEnded(bus, events);
        Assert.Equal(StreamEndReason.StoppedByMarker, Assert.Single(events).Reason);
        Assert.InRange(peaks[^2], 0f, 0.08f);
    }

    [Fact]
    public void MarkerFade_StopAllDuringFadeThenSeek_StaysStopped()
    {
        using var bus = Bus(true, 200);
        var events = new List<StreamEvent>();
        bus.Events += events.Add;
        var handle = bus.AddVoice(Config(MarkerAction.Pause));
        var output = new float[Block];
        for (var block = 0; block < (MarkerFrame - 2400) / Block; block++)
        {
            bus.Render(output);
        }

        bus.StopAll(TimeSpan.FromMilliseconds(500));
        bus.Seek(handle, 0);
        for (var block = 0; block < 60 && events.Count == 0; block++)
        {
            bus.Render(output);
            Assert.InRange(bus.Peak, 0f, 0.2f);
        }

        Assert.Equal(StreamEndReason.FadeCompleted, Assert.Single(events).Reason);
    }

    [Fact]
    public void MarkerFade_MarkerRemovedDuringFade_RestoresLevel()
    {
        using var bus = Bus(true, 200);
        var events = new List<StreamEvent>();
        bus.Events += events.Add;
        var handle = bus.AddVoice(Config(MarkerAction.Pause));
        var output = new float[Block];
        for (var block = 0; block < (MarkerFrame - 2400) / Block; block++)
        {
            bus.Render(output);
        }
        Assert.InRange(bus.Peak, 0f, 0.2f);

        bus.SetMarkers(handle, []);
        for (var block = 0; block < 20; block++)
        {
            bus.Render(output);
        }

        Assert.Empty(events);
        Assert.InRange(bus.Peak, 0.45f, 0.55f);
    }
}
