namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.State;

public sealed class MarkerCommandsTests
{
    private static Marker Mark(string name = "метка", double seconds = 30) =>
        new(name, TimeSpan.FromSeconds(seconds), MarkerAction.Stop);

    [Fact]
    public void SetTrackMarkers_UpdatesTrackAndEmitsShow()
    {
        using var h = new Harness();
        var track = TestShow.Track("storm");
        var project = TestShow.Project("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [project], project.Id));
        var version = h.Snapshot.ShowVersion;

        h.Submit(new SetTrackMarkers(track.Id, [Mark("storm-end", 90)]));

        Assert.True(h.Snapshot.ShowVersion > version);
        h.Submit(new Play());
        Assert.Equal("storm-end", Assert.Single(h.Engine.Last!.Options.Markers).Name);
    }

    [Fact]
    public void SetEntryMarkers_PreservesOtherOverrideFields()
    {
        using var h = new Harness();
        var track = TestShow.Track("storm");
        var entry = TestShow.Entry(track, new ProjectOverrides(Name: "Буря, акт 2"));
        var project = TestShow.Project("Main", entry);
        h.Submit(new LoadShow([track], [project], project.Id));

        h.Submit(new SetEntryMarkers(entry.Id, [Mark("storm-end", 90)]));

        var stored = h.Snapshot.Show.Projects.Single().Entries.Single().Overrides!;
        Assert.Equal("Буря, акт 2", stored.Name);
        Assert.Equal("storm-end", Assert.Single(stored.Markers!.Value).Name);
    }

    [Fact]
    public void SetEntryMarkers_NullRemovesEmptyOverride()
    {
        using var h = new Harness();
        var track = TestShow.Track("storm");
        var bare = TestShow.Entry(track);
        var named = TestShow.Entry(track, new ProjectOverrides(Name: "Тихая"));
        var project = TestShow.Project("Main", bare, named);
        h.Submit(new LoadShow([track], [project], project.Id));

        h.Submit(new SetEntryMarkers(bare.Id, [Mark()]));
        h.Submit(new SetEntryMarkers(named.Id, [Mark()]));
        Assert.NotNull(h.Snapshot.Show.Projects.Single().Entries[0].Overrides);

        h.Submit(new SetEntryMarkers(bare.Id, null));
        h.Submit(new SetEntryMarkers(named.Id, null));

        var entries = h.Snapshot.Show.Projects.Single().Entries;
        Assert.Null(entries[0].Overrides);
        Assert.Equal("Тихая", entries[1].Overrides!.Name);
        Assert.Null(entries[1].Overrides!.Markers);
    }

    [Fact]
    public void SetTrackMarkers_RejectsInvalidMarkers()
    {
        using var h = new Harness();
        var track = TestShow.Track("storm");
        var project = TestShow.Project("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [project], project.Id));

        var seq = h.Submit(new SetTrackMarkers(track.Id, [new Marker("", TimeSpan.FromSeconds(10), MarkerAction.Stop)]));

        Assert.Equal("marker-name-empty", h.RejectionOf(seq)?.Reason);
        h.Submit(new Play());
        Assert.Empty(h.Engine.Last!.Options.Markers);
    }

    [Fact]
    public void SetMarkers_ReachesPlayingTrack()
    {
        using var h = new Harness();
        var track = TestShow.Track("storm");
        var project = TestShow.Project("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [project], project.Id));
        h.Submit(new Play());
        var handle = h.Engine.Last!.Handle;

        h.Submit(new SetTrackMarkers(track.Id, [Mark("storm-end", 90)]));

        var set = h.Engine.MarkerSets.Last();
        Assert.Equal(handle, set.Handle);
        Assert.Equal("storm-end", h.Transport.Current!.Markers.Single().Name);
    }

    [Fact]
    public void Undo_RestoresPreviousMarkers()
    {
        using var h = new Harness();
        var track = TestShow.Track("storm", markers: [Mark("first", 30)]);
        var project = TestShow.Project("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [project], project.Id));

        h.Submit(new SetTrackMarkers(track.Id, [Mark("first", 30), Mark("second", 60)]));
        h.Submit(new Undo());

        h.Submit(new Play());
        Assert.Equal("first", Assert.Single(h.Engine.Last!.Options.Markers).Name);

        var seq = h.Submit(new Undo());
        Assert.Equal("nothing-to-undo", h.RejectionOf(seq)?.Reason);
    }

    [Fact]
    public void Undo_OnEmptyStack_IsRejected()
    {
        using var h = new Harness();

        var seq = h.Submit(new Undo());

        Assert.Equal("nothing-to-undo", h.RejectionOf(seq)?.Reason);
    }

    [Fact]
    public void Undo_IsNotRecordedTwice()
    {
        using var h = new Harness();
        var track = TestShow.Track("storm", markers: [Mark("first", 30)]);
        var project = TestShow.Project("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [project], project.Id));

        h.Submit(new SetTrackMarkers(track.Id, [Mark("first", 30), Mark("second", 60)]));
        h.Submit(new SetTrackMarkers(track.Id, [Mark("first", 30)]));
        h.Submit(new Undo());
        h.Submit(new Undo());

        h.Submit(new Play());
        Assert.Equal("first", Assert.Single(h.Engine.Last!.Options.Markers).Name);

        var seq = h.Submit(new Undo());
        Assert.Equal("nothing-to-undo", h.RejectionOf(seq)?.Reason);
    }

    [Fact]
    public void Undo_RestoresNullOverride()
    {
        using var h = new Harness();
        var track = TestShow.Track("storm");
        var entry = TestShow.Entry(track);
        var project = TestShow.Project("Main", entry);
        h.Submit(new LoadShow([track], [project], project.Id));

        h.Submit(new SetEntryMarkers(entry.Id, [Mark()]));
        Assert.NotNull(h.Snapshot.Show.Projects.Single().Entries.Single().Overrides);

        h.Submit(new Undo());

        Assert.Null(h.Snapshot.Show.Projects.Single().Entries.Single().Overrides);
    }

    [Fact]
    public void MarkerFloor_SkipsFiredMarkerOnReplay()
    {
        using var h = new Harness();
        var track = TestShow.Track("storm", end: EndAction.Stop, markers: [Mark("first", 0.5), Mark("second", 2)]);
        var project = TestShow.Project("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [project], project.Id));
        h.Submit(new SetSmoothing(Smoothing.Default with { Enabled = true, SeekFade = TimeSpan.FromMilliseconds(350) }));
        h.Submit(new Play());
        Assert.Equal(2, h.Engine.Last!.Options.Markers.Length);

        h.Engine.End(h.Engine.Last!.Handle, StreamEndReason.StoppedByMarker, TimeSpan.FromSeconds(0.5));
        Assert.Equal(TransportStatus.Stopped, h.Transport.Status);

        h.Submit(new Play());
        Assert.Equal("second", Assert.Single(h.Engine.Last!.Options.Markers).Name);

        h.Submit(new SeekTo(TimeSpan.Zero));
        Assert.Equal(2, h.Engine.Last!.Options.Markers.Length);
    }

    [Fact]
    public void DeckContent_CarriesMarkers()
    {
        using var h = new Harness();
        var track = TestShow.Track("storm", markers: [Mark("track", 30)]);
        var entry = TestShow.Entry(track, new ProjectOverrides(Markers: [Mark("entry", 60)]));
        var project = TestShow.Project("Main", entry);
        h.Submit(new LoadShow([track], [project], project.Id));
        h.Submit(new Play());

        Assert.Equal("entry", Assert.Single(h.Transport.Current!.Markers).Name);
    }
}
