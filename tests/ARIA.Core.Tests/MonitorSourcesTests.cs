namespace Aria.Core.Tests;

using Aria.Core.Model;
using Aria.Core.Playback;

public sealed class MonitorSourcesTests
{
    [Fact]
    public void Find_ReturnsPathGainGrid()
    {
        var grid = new BeatGrid(96, 3, TimeSpan.FromMilliseconds(40));
        var audio = TrackAudioSettings.Default with { GainDb = -5 };
        var track = new Track(TrackId.New(), "/audio/Подложка песни.wav", "guide", TimeSpan.FromMinutes(3), new TrackDefaults(GainDb: 2, Audio: audio, Grid: grid));
        var sources = new TrackListSources(() => [TestShow.Track("other"), track]);

        var found = sources.Find(track.Id);

        Assert.Equal(new MonitorSource(track.Id, "/audio/Подложка песни.wav", -3, grid), found);
    }

    [Fact]
    public void Find_Unknown_Null()
    {
        var sources = new TrackListSources(() => [TestShow.Track("other")]);

        Assert.Null(sources.Find(TrackId.New()));
    }
}
