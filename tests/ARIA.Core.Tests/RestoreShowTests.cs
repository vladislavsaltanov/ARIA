namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;
using Aria.Core.State;
using Aria.Persistence;

public sealed class RestoreShowTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"aria-restore-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        foreach (var suffix in new[] { "", ".tmp" })
        {
            var file = _path + suffix;
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public void Restore_RoundTripThroughSnapshotStore_E2E()
    {
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        var t3 = TestShow.Track("three");
        var p1 = TestShow.Playlist("Main", TestShow.Entry(t1), TestShow.Entry(t2));
        var p2 = TestShow.Playlist("Spare", TestShow.Entry(t3));

        ShowDocument saved;
        using (var store = new JsonSnapshotStore(_path))
        {
            using var bus = new CommandBus(new ShowController(new FakeEngine()), BusMode.Inline);
            using var autosaver = new ShowAutosaver(bus, store, TimeSpan.FromMilliseconds(150), () => [t1, t2, t3]);
            var seq = 0;
            bus.Submit(Harness.Client, ++seq, new LoadShow([t1, t2, t3], [p1, p2], p1.Id));
            bus.Submit(Harness.Client, ++seq, new EnqueueTrack(t2.Id));
            bus.Submit(Harness.Client, ++seq, new EnqueueEntry(p1.Entries[0].Id));
            bus.Submit(Harness.Client, ++seq, new SetMasterGain(-6));
            bus.Submit(Harness.Client, ++seq, new SetPanicFade(TimeSpan.FromMilliseconds(150)));
            autosaver.FlushNow();
            saved = store.LoadLatest()!;
        }

        using var h = new Harness();
        var restoreSeq = h.Submit(new RestoreShow(saved.Tracks, saved.Playlists, saved.ActiveId, saved.Queue, saved.MasterGainDb, saved.PanicFade, saved.ClockElapsed, saved.ClockRunning));

        Assert.Null(h.RejectionOf(restoreSeq));
        var snap = h.Snapshot;
        Assert.Equal(2, snap.Show.Playlists.Length);
        Assert.Equal(p1.Id, snap.Show.Playlists[0].Id);
        Assert.Equal("Main", snap.Show.Playlists[0].Name);
        Assert.Equal(p1.Entries[0].Id, snap.Show.Playlists[0].Entries[0].Id);
        Assert.Equal(p2.Id, snap.Show.Playlists[1].Id);
        Assert.Equal("Spare", snap.Show.Playlists[1].Name);
        Assert.Equal(p1.Id, snap.Show.ActiveId);
        Assert.Equal(2, snap.Queue.Items.Length);
        Assert.Null(snap.Queue.Items[0].EntryId);
        Assert.Equal(t2.Id, snap.Queue.Items[0].TrackId);
        Assert.Equal("two", snap.Queue.Items[0].DisplayName);
        Assert.Null(snap.Queue.Items[0].Color);
        Assert.Equal(p1.Entries[0].Id, snap.Queue.Items[1].EntryId);
        Assert.Equal(t1.Id, snap.Queue.Items[1].TrackId);
        Assert.Equal("one", snap.Queue.Items[1].DisplayName);
        Assert.Equal(-6, snap.Mixer.MasterGainDb);
        Assert.Equal(TimeSpan.FromMilliseconds(150), snap.Mixer.PanicFade);
        Assert.Contains(-6, h.Engine.MasterGains);
        Assert.True(snap.ShowVersion > 0);
        Assert.True(snap.TransportVersion > 0);
        Assert.True(snap.QueueVersion > 0);
        Assert.True(snap.MixerVersion > 0);
    }

    [Fact]
    public void Restore_WhilePlaying_Rejected()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new Play());

        var seq = h.Submit(new RestoreShow([t1], [p], p.Id, [], 0, TimeSpan.FromMilliseconds(100), TimeSpan.Zero, false));

        Assert.Equal("show-load-requires-stopped", h.RejectionOf(seq)?.Reason);
        Assert.Equal(TransportStatus.Playing, h.Transport.Status);
    }

    [Fact]
    public void Restore_QueueItemUnknownTrack_Rejected()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        var before = h.Snapshot;
        var ghost = TestShow.Track("ghost");

        var seq = h.Submit(new RestoreShow([t1], [p], p.Id, [new QueueItem(null, ghost.Id, "ghost", null)], 0, TimeSpan.FromMilliseconds(100), TimeSpan.Zero, false));

        Assert.Equal("invalid-show", h.RejectionOf(seq)?.Reason);
        Assert.Equal(before.Show, h.Snapshot.Show);
        Assert.Equal(before.ShowVersion, h.Snapshot.ShowVersion);
    }

    [Fact]
    public void Restore_UnknownEntryId_Rejected()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));

        var seq = h.Submit(new RestoreShow([t1], [p], p.Id, [new QueueItem(EntryId.New(), t1.Id, "one", null)], 0, TimeSpan.FromMilliseconds(100), TimeSpan.Zero, false));

        Assert.Equal("invalid-show", h.RejectionOf(seq)?.Reason);
    }

    [Fact]
    public void Restore_UnknownActivePlaylist_Rejected()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));

        var seq = h.Submit(new RestoreShow([t1], [p], PlaylistId.New(), [], 0, TimeSpan.FromMilliseconds(100), TimeSpan.Zero, false));

        Assert.Equal("unknown-active-playlist", h.RejectionOf(seq)?.Reason);
    }

    [Fact]
    public void Restore_GainOutOfRange_Rejected()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));

        var seq = h.Submit(new RestoreShow([t1], [p], p.Id, [], 50, TimeSpan.FromMilliseconds(100), TimeSpan.Zero, false));

        Assert.Equal("gain-out-of-range", h.RejectionOf(seq)?.Reason);
        Assert.Empty(h.Engine.MasterGains);
    }

    [Fact]
    public void Restore_PanicFadeOutOfRange_Rejected()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));

        var seq = h.Submit(new RestoreShow([t1], [p], p.Id, [], 0, TimeSpan.FromSeconds(3), TimeSpan.Zero, false));

        Assert.Equal("panic-fade-out-of-range", h.RejectionOf(seq)?.Reason);
    }

    [Fact]
    public void Restore_ThenPlay_PlaysQueueFirst()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        var t3 = TestShow.Track("three");
        var p1 = TestShow.Playlist("Main", TestShow.Entry(t1), TestShow.Entry(t2));
        var p2 = TestShow.Playlist("Spare", TestShow.Entry(t3));
        h.Submit(new LoadShow([t1, t2, t3], [p1, p2], p1.Id));
        h.Submit(new EnqueueTrack(t3.Id));
        var queue = h.Snapshot.Queue.Items;

        h.Submit(new RestoreShow([t1, t2, t3], [p1, p2], p1.Id, queue, 0, TimeSpan.FromMilliseconds(100), TimeSpan.Zero, false));
        h.Submit(new Play());

        Assert.Equal(t3.Id, h.Transport.Current!.TrackId);
        Assert.Equal(t1.Id, h.Transport.Next!.TrackId);
    }

    [Fact]
    public void Restore_RunningClock_ArrivesStopped_ElapsedKept()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1));

        var seq = h.Submit(new RestoreShow([t1], [p], p.Id, [], 0, TimeSpan.FromMilliseconds(100), TimeSpan.FromMinutes(12), true));

        Assert.Null(h.RejectionOf(seq));
        Assert.False(h.Snapshot.Show.Clock.Running);
        Assert.Equal(TimeSpan.FromMinutes(12), h.Snapshot.Show.Clock.Elapsed);
    }
}
