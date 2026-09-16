namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.State;

public sealed class PlaylistEditTests
{
    [Fact]
    public void Create_Rename_Delete_ReflectInShow()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p1 = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p1], p1.Id));
        var version = h.Snapshot.ShowVersion;

        h.Submit(new CreatePlaylist("Spare"));
        var spare = h.Snapshot.Show.Playlists.First(p => p.Name == "Spare");
        Assert.Equal(2, h.Snapshot.Show.Playlists.Length);
        Assert.Empty(spare.Entries);
        Assert.True(h.Snapshot.ShowVersion > version);
        version = h.Snapshot.ShowVersion;

        h.Submit(new RenamePlaylist(spare.Id, "SpareRenamed"));
        Assert.Equal("SpareRenamed", h.Snapshot.Show.Playlists.First(p => p.Id == spare.Id).Name);
        Assert.True(h.Snapshot.ShowVersion > version);
        version = h.Snapshot.ShowVersion;

        h.Submit(new DeletePlaylist(spare.Id));
        Assert.Single(h.Snapshot.Show.Playlists);
        Assert.Equal("Main", h.Snapshot.Show.Playlists[0].Name);
        Assert.True(h.Snapshot.ShowVersion > version);
    }

    [Fact]
    public void PlaylistName_EmptyOrWhitespace_IsRejected()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p1 = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p1], p1.Id));

        var emptyCreate = h.Submit(new CreatePlaylist(""));
        var blankCreate = h.Submit(new CreatePlaylist("   "));
        var emptyRename = h.Submit(new RenamePlaylist(p1.Id, ""));
        var blankRename = h.Submit(new RenamePlaylist(p1.Id, "  "));

        Assert.Equal("bad-name", h.RejectionOf(emptyCreate)?.Reason);
        Assert.Equal("bad-name", h.RejectionOf(blankCreate)?.Reason);
        Assert.Equal("bad-name", h.RejectionOf(emptyRename)?.Reason);
        Assert.Equal("bad-name", h.RejectionOf(blankRename)?.Reason);
        Assert.Single(h.Snapshot.Show.Playlists);
        Assert.Equal("Main", h.Snapshot.Show.Playlists[0].Name);
    }

    [Fact]
    public void DeleteActivePlaylist_ClearsActiveId()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p1 = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p1], p1.Id));

        h.Submit(new DeletePlaylist(p1.Id));

        var snap = h.Snapshot;
        Assert.Null(snap.Show.ActiveId);
        Assert.Empty(snap.Show.Playlists);
        Assert.Equal(TransportStatus.Stopped, snap.Transport.Status);
    }

    [Fact]
    public void DeletePlaylist_DropsQueueSnapshots()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("queued");
        var p1 = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p1], p1.Id));
        h.Submit(new EnqueueEntry(p1.Entries[0].Id));

        h.Submit(new DeletePlaylist(p1.Id));

        Assert.Empty(h.Snapshot.Queue.Items);

        var seq = h.Submit(new Play());

        Assert.Equal("nothing-to-play", h.RejectionOf(seq)?.Reason);
        Assert.Equal(TransportStatus.Stopped, h.Transport.Status);
    }

    [Fact]
    public void AddEntry_AppendsInserts_Validates()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        var p1 = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1, t2], [p1], p1.Id));

        h.Submit(new AddEntry(p1.Id, t2.Id));
        var entries = h.Snapshot.Show.Playlists.Single().Entries;
        Assert.Equal(2, entries.Length);
        Assert.Equal(t2.Id, entries[1].TrackId);

        h.Submit(new AddEntry(p1.Id, t1.Id, 0));
        entries = h.Snapshot.Show.Playlists.Single().Entries;
        Assert.Equal(3, entries.Length);
        Assert.Equal(t1.Id, entries[0].TrackId);

        var tooHigh = h.Submit(new AddEntry(p1.Id, t1.Id, 4));
        var negative = h.Submit(new AddEntry(p1.Id, t1.Id, -1));
        var unknownPlaylist = h.Submit(new AddEntry(PlaylistId.New(), t1.Id));
        var unknownTrack = h.Submit(new AddEntry(p1.Id, TrackId.New()));

        Assert.Equal("bad-index", h.RejectionOf(tooHigh)?.Reason);
        Assert.Equal("bad-index", h.RejectionOf(negative)?.Reason);
        Assert.Equal("unknown-playlist", h.RejectionOf(unknownPlaylist)?.Reason);
        Assert.Equal("unknown-track", h.RejectionOf(unknownTrack)?.Reason);
        Assert.Equal(3, h.Snapshot.Show.Playlists.Single().Entries.Length);
    }

    [Fact]
    public void MoveEntry_ReordersNeighbors_Validates()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        var t3 = TestShow.Track("three");
        var e1 = TestShow.Entry(t1);
        var e2 = TestShow.Entry(t2);
        var e3 = TestShow.Entry(t3);
        var p1 = TestShow.Playlist("Main", e1, e2, e3);
        h.Submit(new LoadShow([t1, t2, t3], [p1], p1.Id));

        h.Submit(new MoveEntry(e3.Id, 0));
        var entries = h.Snapshot.Show.Playlists.Single().Entries;
        Assert.Equal(e3.Id, entries[0].Id);
        Assert.Equal(e1.Id, entries[1].Id);
        Assert.Equal(e2.Id, entries[2].Id);

        var tooHigh = h.Submit(new MoveEntry(e1.Id, 3));
        var negative = h.Submit(new MoveEntry(e1.Id, -1));
        var unknown = h.Submit(new MoveEntry(EntryId.New(), 0));

        Assert.Equal("bad-index", h.RejectionOf(tooHigh)?.Reason);
        Assert.Equal("bad-index", h.RejectionOf(negative)?.Reason);
        Assert.Equal("unknown-entry", h.RejectionOf(unknown)?.Reason);

        h.Submit(new MoveEntry(e3.Id, 2));
        var reordered = h.Snapshot.Show.Playlists.Single().Entries;
        Assert.Equal(new[] { e1.Id, e2.Id, e3.Id }, reordered.Select(x => x.Id).ToArray());
    }

    [Fact]
    public void SetEntryOverrides_AppliesAfterJumpTo_ClearRestoresDefaults()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("original");
        var e1 = TestShow.Entry(t1);
        var p1 = TestShow.Playlist("Main", e1);
        h.Submit(new LoadShow([t1], [p1], p1.Id));

        h.Submit(new SetEntryOverrides(e1.Id, new PlaylistOverrides(
            Name: "Буря, акт 2",
            Color: "amber",
            EndAction: EndAction.Pause)));
        h.Submit(new JumpTo(e1.Id));

        var current = h.Transport.Current!;
        Assert.Equal("Буря, акт 2", current.DisplayName);
        Assert.Equal("amber", current.Color);
        Assert.Equal(EndAction.Pause, current.EndAction);

        h.Submit(new SetEntryOverrides(e1.Id, null));
        h.Submit(new JumpTo(e1.Id));

        current = h.Transport.Current!;
        Assert.Equal("original", current.DisplayName);
        Assert.Null(current.Color);
        Assert.Equal(EndAction.Advance, current.EndAction);
    }

    [Fact]
    public void SetEntryOverrides_UpdatesNextHint()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        var e2 = TestShow.Entry(t2);
        var p1 = TestShow.Playlist("Main", TestShow.Entry(t1), e2);
        h.Submit(new LoadShow([t1, t2], [p1], p1.Id));
        h.Submit(new Play());
        Assert.Equal(t2.DefaultName, h.Transport.Next!.DisplayName);

        h.Submit(new SetEntryOverrides(e2.Id, new PlaylistOverrides(Name: "Финал", Color: "blue")));

        Assert.Equal("Финал", h.Transport.Next!.DisplayName);
        Assert.Equal("blue", h.Transport.Next.Color);
    }

    [Fact]
    public void MoveQueueItem_Reorders_Validates()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        var t3 = TestShow.Track("three");
        var p1 = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1, t2, t3], [p1], p1.Id));
        h.Submit(new EnqueueTrack(t1.Id));
        h.Submit(new EnqueueTrack(t2.Id));
        h.Submit(new EnqueueTrack(t3.Id));

        h.Submit(new MoveQueueItem(0, 1));
        var order = h.Snapshot.Queue.Items.Select(i => i.TrackId).ToArray();
        Assert.Equal(new[] { t2.Id, t1.Id, t3.Id }, order);

        var tooHigh = h.Submit(new MoveQueueItem(0, 3));
        var negative = h.Submit(new MoveQueueItem(-1, 0));
        var outOfRange = h.Submit(new MoveQueueItem(3, 0));

        Assert.Equal("bad-index", h.RejectionOf(tooHigh)?.Reason);
        Assert.Equal("bad-index", h.RejectionOf(negative)?.Reason);
        Assert.Equal("bad-index", h.RejectionOf(outOfRange)?.Reason);

        h.Submit(new MoveQueueItem(0, 2));
        Assert.Equal(new[] { t1.Id, t3.Id, t2.Id }, h.Snapshot.Queue.Items.Select(i => i.TrackId).ToArray());
    }

    [Fact]
    public void SetActivePlaylist_SwitchesActiveAndNext_RejectsWhenPanicked()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        var p1 = TestShow.Playlist("One", TestShow.Entry(t1));
        var p2 = TestShow.Playlist("Two", TestShow.Entry(t2));
        h.Submit(new LoadShow([t1, t2], [p1, p2], p1.Id));
        Assert.Equal(p1.Id, h.Snapshot.Show.ActiveId);
        Assert.Equal(t1.Id, h.Transport.Next!.TrackId);

        h.Submit(new SetActivePlaylist(p2.Id));

        Assert.Equal(p2.Id, h.Snapshot.Show.ActiveId);
        Assert.Equal(t2.Id, h.Transport.Next!.TrackId);

        h.Submit(new Panic());
        var rejected = h.Submit(new SetActivePlaylist(p1.Id));

        Assert.Equal("panicked", h.RejectionOf(rejected)?.Reason);
        Assert.Equal(p2.Id, h.Snapshot.Show.ActiveId);
    }

    [Fact]
    public void EditCommands_WorkWhilePanicked()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p1 = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p1], p1.Id));
        h.Submit(new Play());
        h.Submit(new Panic());
        Assert.Equal(TransportStatus.Panicked, h.Transport.Status);

        var seq = h.Submit(new CreatePlaylist("Emergency"));
        Assert.Null(h.RejectionOf(seq));
        var emergency = h.Snapshot.Show.Playlists.First(p => p.Name == "Emergency");

        h.Submit(new AddEntry(emergency.Id, t1.Id));

        Assert.Single(h.Snapshot.Show.Playlists.First(p => p.Name == "Emergency").Entries);
        Assert.Equal(TransportStatus.Panicked, h.Transport.Status);
    }

    [Fact]
    public void RemoveEntry_PlayingEntry_StopsAndDisposes()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        var t3 = TestShow.Track("three");
        var e1 = TestShow.Entry(t1);
        var p1 = TestShow.Playlist("Main", e1, TestShow.Entry(t2), TestShow.Entry(t3));
        h.Submit(new LoadShow([t1, t2, t3], [p1], p1.Id));
        h.Submit(new Play());
        var handle = h.Engine.Created[0].Handle;
        Assert.Equal(t1.Id, h.Transport.Current!.TrackId);

        h.Submit(new RemoveEntry(e1.Id));

        Assert.Equal("unknown-entry", h.RejectionOf(h.Submit(new RemoveEntry(EntryId.New())))?.Reason);
        Assert.Equal(TransportStatus.Stopped, h.Transport.Status);
        Assert.Null(h.Transport.Current);
        Assert.Contains(handle, h.Engine.Disposed);
        Assert.Single(h.Engine.Created);
        Assert.Equal(2, h.Snapshot.Show.Playlists.Single().Entries.Length);
    }

    [Fact]
    public void ImportPlaylist_CreatesPlaylist_WithEntriesAndOverrides()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        h.Submit(new LoadShow([t1, t2], [], null));
        var version = h.Snapshot.ShowVersion;

        h.Submit(new ImportPlaylist("Вечер", [
            new ImportPlaylistEntry(t1.Id, new PlaylistOverrides("Утро", GainDb: -3)),
            new ImportPlaylistEntry(t2.Id),
        ]));

        var imported = Assert.Single(h.Snapshot.Show.Playlists);
        Assert.Equal("Вечер", imported.Name);
        Assert.Equal(2, imported.Entries.Length);
        Assert.Equal(t1.Id, imported.Entries[0].TrackId);
        Assert.Equal("Утро", imported.Entries[0].Overrides?.Name);
        Assert.Equal(-3, imported.Entries[0].Overrides?.GainDb);
        Assert.Equal(t2.Id, imported.Entries[1].TrackId);
        Assert.Null(imported.Entries[1].Overrides);
        Assert.True(h.Snapshot.ShowVersion > version);
    }

    [Theory]
    [InlineData("", "bad-name")]
    [InlineData("   ", "bad-name")]
    public void ImportPlaylist_BlankName_IsRejected(string name, string reason)
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        h.Submit(new LoadShow([t1], [], null));

        var seq = h.Submit(new ImportPlaylist(name, [new ImportPlaylistEntry(t1.Id)]));

        Assert.Equal(reason, h.RejectionOf(seq)?.Reason);
        Assert.Empty(h.Snapshot.Show.Playlists);
    }

    [Fact]
    public void ImportPlaylist_EmptyEntries_IsRejected()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        h.Submit(new LoadShow([t1], [], null));

        var seq = h.Submit(new ImportPlaylist("Вечер", []));

        Assert.Equal("empty-playlist", h.RejectionOf(seq)?.Reason);
        Assert.Empty(h.Snapshot.Show.Playlists);
    }

    [Fact]
    public void ImportPlaylist_UnknownTrack_IsRejected()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        h.Submit(new LoadShow([t1], [], null));

        var seq = h.Submit(new ImportPlaylist("Вечер", [new ImportPlaylistEntry(TrackId.New())]));

        Assert.Equal("unknown-track", h.RejectionOf(seq)?.Reason);
        Assert.Empty(h.Snapshot.Show.Playlists);
    }
}
