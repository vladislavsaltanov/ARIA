namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.State;

public sealed class MergeTracksTests
{
    [Fact]
    public void Stopped_ExtendsTrackSet_KeepsPlaylistsAndQueue()
    {
        using var harness = new Harness();
        var first = TestShow.Track("first");
        var playlist = TestShow.Playlist("Main", TestShow.Entry(first));
        var second = TestShow.Track("second");
        harness.Submit(new LoadShow([first], [playlist], playlist.Id));
        harness.Submit(new EnqueueTrack(first.Id));

        harness.Submit(new MergeTracks([second]));

        var seq = harness.Submit(new EnqueueTrack(second.Id));
        Assert.Null(harness.RejectionOf(seq));
        Assert.Single(harness.Snapshot.Show.Playlists);
        Assert.Equal(2, harness.Snapshot.Queue.Items.Length);
        Assert.Equal(TransportStatus.Stopped, harness.Transport.Status);
    }

    [Fact]
    public void WhilePlaying_DoesNotTouchTransportOrQueue()
    {
        using var harness = new Harness();
        var first = TestShow.Track("first");
        var playlist = TestShow.Playlist("Main", TestShow.Entry(first));
        harness.Submit(new LoadShow([first], [playlist], playlist.Id));
        harness.Submit(new Play());
        var queueBefore = harness.Snapshot.Queue.Items.Length;

        var second = TestShow.Track("second");
        harness.Submit(new MergeTracks([second]));

        Assert.Equal(TransportStatus.Playing, harness.Transport.Status);
        Assert.Equal(first.Id, harness.Transport.Current!.TrackId);
        Assert.Equal(queueBefore, harness.Snapshot.Queue.Items.Length);
    }

    [Fact]
    public void MergedTrack_CanBeEnqueuedAndPlayed()
    {
        using var harness = new Harness();
        var first = TestShow.Track("first");
        var playlist = TestShow.Playlist("Main", TestShow.Entry(first));
        harness.Submit(new LoadShow([first], [playlist], playlist.Id));
        harness.Submit(new Play());

        var second = TestShow.Track("second");
        harness.Submit(new MergeTracks([second]));
        harness.Submit(new EnqueueTrack(second.Id));
        harness.Submit(new Next());

        Assert.Equal(second.Id, harness.Transport.Current!.TrackId);
    }

    [Fact]
    public void DuplicateIds_Rejected()
    {
        using var harness = new Harness();
        var track = TestShow.Track("a");

        var seq = harness.Submit(new MergeTracks([track, track]));

        Assert.Equal("duplicate-track", harness.RejectionOf(seq)!.Reason);
    }

    [Fact]
    public void Empty_Rejected()
    {
        using var harness = new Harness();

        var seq = harness.Submit(new MergeTracks([]));

        Assert.Equal("show-data-required", harness.RejectionOf(seq)!.Reason);
    }

    [Fact]
    public void KnownIds_NoChange_NoNewDelta()
    {
        using var harness = new Harness();
        var track = TestShow.Track("a");
        harness.Submit(new MergeTracks([track]));
        var showVersion = harness.Snapshot.ShowVersion;

        harness.Submit(new MergeTracks([track]));

        Assert.Equal(showVersion, harness.Snapshot.ShowVersion);
    }
}
