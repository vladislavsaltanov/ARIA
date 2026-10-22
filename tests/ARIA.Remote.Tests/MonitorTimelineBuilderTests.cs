namespace Aria.Remote.Tests;

using System.Diagnostics;
using Aria.Audio;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.State;
using Aria.Remote.Monitor;

public sealed class MonitorTimelineBuilderTests
{
    private static readonly long T0 = Stopwatch.Frequency * 100;
    private static readonly long T0Us = HostClock.TicksToUs(T0);
    private readonly Dictionary<int, DeckContent> _bindings = [];
    private readonly List<Track> _tracks = [];
    private readonly HashSet<TrackId> _unavailable = [];

    private MonitorTimelineBuilder Builder() =>
        new(handle => _bindings.TryGetValue(handle.Value, out var deck) ? deck : null, new TrackListSources(() => [.. _tracks]), id => !_unavailable.Contains(id));

    private static OutputClock Clock()
    {
        var clock = new OutputClock(48000);
        clock.Observe(new OutputStamp(0, T0, 0, 0));
        clock.Observe(new OutputStamp(48000, T0 + Stopwatch.Frequency, 0, 0));
        return clock;
    }

    private static long UsAt(long outputFrame) => T0Us + outputFrame * 1_000_000 / 48000;

    private static MixBlockReport Report(long outputFrame, params VoiceReport[] voices)
    {
        var report = new MixBlockReport { OutputFrame = outputFrame, Count = voices.Length };
        voices.CopyTo(report.Voices, 0);
        return report;
    }

    private static VoiceReport Voice(int tag, long sourceFrame, bool active = true, int epoch = 0, bool stopWhenDone = false, bool ended = false, long endSourceFrame = -1, double gainDb = 0) =>
        new()
        {
            Tag = tag,
            SourceFrame = sourceFrame,
            Active = active,
            Epoch = epoch,
            StopWhenDone = stopWhenDone,
            Ended = ended,
            EndSourceFrame = endSourceFrame,
            GainDb = gainDb,
            FadeFrom = 0,
            FadeTo = 1,
            FadeProgress = 1,
            FadeCurve = FadeCurve.Linear,
        };

    private Track AddTrack(string name, BeatGrid? grid = null, double gainDb = 0)
    {
        var track = new Track(TrackId.New(), $"/audio/{name}.wav", name, TimeSpan.FromMinutes(3), new TrackDefaults(GainDb: gainDb, Grid: grid));
        _tracks.Add(track);
        return track;
    }

    private static DeckContent Deck(Track track, TimeSpan cueIn = default, TrackId? backing = null) =>
        new(EntryId.New(), track.Id, track.DefaultName, null, EndAction.Stop, track.Duration, cueIn, null, track.Defaults.Grid, default, backing);

    [Fact]
    public void Build_PlayingVoice_AnchorsFileFrameToPresentation()
    {
        var song = AddTrack("song");
        _bindings[1] = Deck(song, TimeSpan.FromSeconds(2));
        var builder = Builder();
        builder.Apply(Report(96000, Voice(1, 4800, gainDb: -3)));

        var message = builder.Build(Clock(), UsAt(96000) - 80_000, force: false);

        Assert.NotNull(message);
        Assert.Equal("timeline", message.Type);
        Assert.InRange(message.Rate, 47999.9, 48000.1);
        var voice = Assert.Single(message.Voices);
        Assert.Equal(song.Id.Value, voice.Track);
        Assert.Equal("entry", voice.Kind);
        Assert.False(voice.Fallback);
        Assert.Equal(-3, voice.GainDb);
        Assert.Equal(96000 + 4800, voice.AnchorFrame);
        Assert.Equal(UsAt(96000), voice.AnchorUs);
        Assert.True(voice.Playing);
        Assert.Null(voice.EndFrame);
        Assert.Null(voice.Fade);
    }

    [Fact]
    public void Build_BackingWithCueIn_AnchorsAbsoluteFrame()
    {
        var song = AddTrack("song");
        var guide = AddTrack("guide", gainDb: -4);
        _bindings[1] = Deck(song, TimeSpan.FromSeconds(1.5), guide.Id);
        var builder = Builder();
        builder.Apply(Report(0, Voice(1, 1000, gainDb: -10)));

        var voice = Assert.Single(builder.Build(Clock(), UsAt(0), false)!.Voices);

        Assert.Equal(guide.Id.Value, voice.Track);
        Assert.Equal("backing", voice.Kind);
        Assert.Equal(72000 + 1000, voice.AnchorFrame);
        Assert.Equal(-4, voice.GainDb);
    }

    [Fact]
    public void Build_BackingUnavailable_FallsBackToEntry()
    {
        var song = AddTrack("song");
        var guide = AddTrack("guide");
        _unavailable.Add(guide.Id);
        _bindings[1] = Deck(song, backing: guide.Id);
        var builder = Builder();
        builder.Apply(Report(0, Voice(1, 0)));

        var voice = Assert.Single(builder.Build(Clock(), UsAt(0), false)!.Voices);

        Assert.Equal(song.Id.Value, voice.Track);
        Assert.Equal("entry", voice.Kind);
        Assert.True(voice.Fallback);
    }

    [Fact]
    public void Build_EndedVoice_CarriesEndFrame()
    {
        var song = AddTrack("song");
        _bindings[1] = Deck(song);
        var builder = Builder();
        builder.Apply(Report(512, Voice(1, 23552, ended: true, endSourceFrame: 24000)));

        var voice = Assert.Single(builder.Build(Clock(), UsAt(512), false)!.Voices);

        Assert.Equal(24000, voice.EndFrame);
        Assert.True(voice.Playing);
    }

    [Fact]
    public void Build_Fade_MapsFaderState()
    {
        var song = AddTrack("song");
        _bindings[1] = Deck(song);
        var builder = Builder();
        var fading = Voice(1, 0, stopWhenDone: true) with { };
        fading.FadeFrom = 1;
        fading.FadeTo = 0;
        fading.FadeProgress = 0.25;
        fading.FadeFrames = 38400;
        fading.FadeCurve = FadeCurve.Exponential;
        builder.Apply(Report(0, fading));

        var voice = Assert.Single(builder.Build(Clock(), UsAt(0), false)!.Voices);

        Assert.Equal(new FadeTimeline(1, 0, 0.25, 38400, "Exponential", true), voice.Fade);
    }

    [Fact]
    public void Build_Grid_NewestActiveNonStopping()
    {
        var old = AddTrack("old", new BeatGrid(120, 4, TimeSpan.Zero));
        var fresh = AddTrack("fresh", new BeatGrid(90, 3, TimeSpan.FromMilliseconds(500)));
        _bindings[1] = Deck(old);
        _bindings[2] = Deck(fresh);
        var builder = Builder();
        builder.Apply(Report(0, Voice(1, 1000, stopWhenDone: true), Voice(2, 0)));

        var grid = builder.Build(Clock(), UsAt(0), false)!.Grid;

        Assert.Equal(new GridTimeline(2, 90, 3, 24000), grid);
    }

    [Fact]
    public void Build_Grid_PausedVoiceStillProvidesGrid()
    {
        var song = AddTrack("song", new BeatGrid(120, 4, TimeSpan.Zero));
        _bindings[1] = Deck(song);
        var builder = Builder();
        builder.Apply(Report(0, Voice(1, 48000, active: false)));

        var message = builder.Build(Clock(), UsAt(0), false)!;

        Assert.Equal(1, message.Grid!.Voice);
        Assert.False(Assert.Single(message.Voices).Playing);
    }

    [Fact]
    public void Build_Grid_FallsBackToBackingGrid()
    {
        var song = AddTrack("song");
        var guide = AddTrack("guide", new BeatGrid(100, 4, TimeSpan.Zero));
        _bindings[1] = Deck(song, backing: guide.Id);
        var builder = Builder();
        builder.Apply(Report(0, Voice(1, 0)));

        Assert.Equal(100, builder.Build(Clock(), UsAt(0), false)!.Grid!.Bpm);
    }

    [Fact]
    public void Build_NoChange_NullUntilHeartbeat()
    {
        var song = AddTrack("song");
        _bindings[1] = Deck(song);
        var builder = Builder();
        var clock = Clock();
        builder.Apply(Report(0, Voice(1, 0)));
        var now = UsAt(0);

        var first = builder.Build(clock, now, false);
        var quiet = builder.Build(clock, now + 100_000, false);
        var heartbeat = builder.Build(clock, now + 500_000, false);

        Assert.NotNull(first);
        Assert.Null(quiet);
        Assert.NotNull(heartbeat);
        Assert.True(heartbeat.Seq > first.Seq);
    }

    [Fact]
    public void Build_EpochChange_Immediate()
    {
        var song = AddTrack("song");
        _bindings[1] = Deck(song);
        var builder = Builder();
        var clock = Clock();
        builder.Apply(Report(0, Voice(1, 0)));
        var now = UsAt(0);
        builder.Build(clock, now, false);

        builder.Apply(Report(512, Voice(1, 96000, epoch: 1)));

        Assert.NotNull(builder.Build(clock, now + 10_000, false));
    }

    [Fact]
    public void Build_RefreshesBindingOnApply()
    {
        var song = AddTrack("song", new BeatGrid(120, 4, TimeSpan.Zero));
        _bindings[1] = Deck(song);
        var builder = Builder();
        var clock = Clock();
        builder.Apply(Report(0, Voice(1, 0)));
        Assert.Equal(120, builder.Build(clock, UsAt(0), false)!.Grid!.Bpm);

        _bindings[1] = Deck(song) with { Grid = new BeatGrid(90, 3, TimeSpan.Zero) };
        builder.Apply(Report(512, Voice(1, 512)));

        Assert.Equal(new GridTimeline(1, 90, 3, 0), builder.Build(clock, UsAt(512), false)!.Grid);
    }

    [Fact]
    public void Build_UnboundVoice_Skipped()
    {
        var builder = Builder();
        builder.Apply(Report(0, Voice(3, 0)));

        Assert.Empty(builder.Build(Clock(), UsAt(0), false)!.Voices);
    }

    [Fact]
    public void Build_UnboundAfterSeen_UsesCachedBinding()
    {
        var song = AddTrack("song");
        _bindings[1] = Deck(song);
        var builder = Builder();
        builder.Apply(Report(0, Voice(1, 0)));
        _bindings.Remove(1);

        builder.Apply(Report(512, Voice(1, 512, stopWhenDone: true)));

        Assert.Single(builder.Build(Clock(), UsAt(512), false)!.Voices);
    }

    [Fact]
    public void Build_Next_FromTransport()
    {
        var song = AddTrack("song");
        var next = AddTrack("next");
        var guide = AddTrack("guide");
        var builder = Builder();
        builder.SetTransport(new TransportState(TransportStatus.Playing, Deck(song), Deck(next, TimeSpan.FromSeconds(0.5), guide.Id), []));

        var deck = builder.Build(Clock(), UsAt(0), false)!.Deck;

        Assert.Equal("Playing", deck.Status);
        Assert.Equal("song", deck.Title);
        Assert.Equal(new NextTimeline("next", guide.Id.Value, "backing", 24000), deck.Next);
    }

    [Fact]
    public void Build_TestClick_AnchorsMultipleOf48000()
    {
        var builder = Builder();
        var report = Report(50000);
        report.TestClick = true;
        builder.Apply(report);

        var test = builder.Build(Clock(), UsAt(50000), false)!.Test;

        Assert.NotNull(test);
        Assert.Equal(UsAt(96000), test.AnchorUs);
        Assert.InRange(test.IntervalUs, 999_990, 1_000_010);
    }

    [Fact]
    public void Build_ClockNotSettled_Null()
    {
        var builder = Builder();
        builder.Apply(Report(0));

        Assert.Null(builder.Build(new OutputClock(48000), UsAt(0), true));
    }

    [Fact]
    public void AllowedTracks_KeepsSeenFor30s()
    {
        var song = AddTrack("song");
        _bindings[1] = Deck(song);
        var builder = Builder();
        var clock = Clock();
        builder.Apply(Report(0, Voice(1, 0, ended: true, endSourceFrame: 100)));
        var now = UsAt(0);

        builder.Build(clock, now, false);
        Assert.Contains(song.Id, builder.AllowedTracks);
        var later = builder.Build(clock, now + 5_000_000, true);
        Assert.Empty(later!.Voices);
        Assert.Contains(song.Id, builder.AllowedTracks);
        builder.Build(clock, now + 31_000_000, true);
        Assert.DoesNotContain(song.Id, builder.AllowedTracks);
    }
}
