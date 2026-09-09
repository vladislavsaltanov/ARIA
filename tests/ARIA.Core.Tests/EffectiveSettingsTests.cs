namespace Aria.Core.Tests;

using Aria.Core.Model;

public sealed class EffectiveSettingsTests
{
    [Fact]
    public void NoOverride_InheritsTrackDefaults()
    {
        var marker = new Marker("end", TimeSpan.FromSeconds(30), MarkerAction.Stop);
        var track = TestShow.Track(
            "line",
            EndAction.Pause,
            [marker],
            gainDb: -2,
            fadeIn: new Fade(TimeSpan.FromMilliseconds(300), FadeCurve.Linear));

        var entry = TestShow.Entry(track);
        var settings = EffectiveSettings.Resolve(entry, track);

        Assert.Equal("line", settings.DisplayName);
        Assert.Null(settings.Color);
        Assert.Equal(-2, settings.GainDb);
        Assert.Equal(EndAction.Pause, settings.EndAction);
        Assert.Equal(TimeSpan.FromMilliseconds(300), settings.In.Duration);
        Assert.Equal(Fade.None, settings.Out);
        Assert.Equal(TimeSpan.Zero, settings.CueIn);
        Assert.Null(settings.CueOut);
        Assert.Contains(marker, settings.Markers);
    }

    [Fact]
    public void Override_WinsOverDefaults()
    {
        var track = TestShow.Track("original", EndAction.Advance, gainDb: 0);
        var overrides = new PlaylistOverrides(
            Name: "имя",
            Color: "red",
            Note: "заметка",
            GainDb: -9,
            EndAction: EndAction.Replay,
            In: new Fade(TimeSpan.FromSeconds(1), FadeCurve.Logarithmic),
            Out: new Fade(TimeSpan.FromSeconds(4), FadeCurve.Exponential),
            CueIn: TimeSpan.FromSeconds(5),
            CueOut: TimeSpan.FromMinutes(2));

        var entry = TestShow.Entry(track, overrides);
        var settings = EffectiveSettings.Resolve(entry, track);

        Assert.Equal("имя", settings.DisplayName);
        Assert.Equal("red", settings.Color);
        Assert.Equal(-9, settings.GainDb);
        Assert.Equal(EndAction.Replay, settings.EndAction);
        Assert.Equal(TimeSpan.FromSeconds(1), settings.In.Duration);
        Assert.Equal(FadeCurve.Logarithmic, settings.In.Curve);
        Assert.Equal(TimeSpan.FromSeconds(4), settings.Out.Duration);
        Assert.Equal(TimeSpan.FromSeconds(5), settings.CueIn);
        Assert.Equal(TimeSpan.FromMinutes(2), settings.CueOut);
    }

    [Fact]
    public void PartialOverride_MixesEntryAndTrack()
    {
        var track = TestShow.Track("track", EndAction.Stop, gainDb: -4, fadeIn: new Fade(TimeSpan.FromMilliseconds(200), FadeCurve.SCurve));
        var overrides = new PlaylistOverrides(GainDb: -10);

        var settings = EffectiveSettings.Resolve(TestShow.Entry(track, overrides), track);

        Assert.Equal(-10, settings.GainDb);
        Assert.Equal(EndAction.Stop, settings.EndAction);
        Assert.Equal(TimeSpan.FromMilliseconds(200), settings.In.Duration);
        Assert.Equal("track", settings.DisplayName);
    }

    [Fact]
    public void ForTrack_UsesDefaultsWithoutEntry()
    {
        var track = TestShow.Track("loose", EndAction.Replay, gainDb: -1);

        var settings = EffectiveSettings.ForTrack(track);

        Assert.Equal("loose", settings.DisplayName);
        Assert.Equal(EndAction.Replay, settings.EndAction);
        Assert.Equal(-1, settings.GainDb);
        Assert.Null(settings.Color);
        Assert.Equal(TimeSpan.Zero, settings.CueIn);
    }
}
