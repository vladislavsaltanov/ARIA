namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.State;

public sealed class PlayTrackTests : IDisposable
{
    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void PlayTrack_WhenStopped_StartsImmediately()
    {
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1), TestShow.Entry(t2));
        _harness.Submit(new LoadShow([t1, t2], [p], p.Id));

        _harness.Submit(new PlayTrack(t2.Id));

        Assert.Equal(TransportStatus.Playing, _harness.Transport.Status);
        Assert.Equal(t2.Id, _harness.Transport.Current!.TrackId);
        Assert.Empty(_harness.Snapshot.Queue.Items);
    }

    [Fact]
    public void PlayTrack_WhenPlaying_InterruptsCurrent()
    {
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1), TestShow.Entry(t2));
        _harness.Submit(new LoadShow([t1, t2], [p], p.Id));
        _harness.Submit(new Play());

        _harness.Submit(new PlayTrack(t2.Id));

        Assert.Equal(2, _harness.Engine.Created.Count);
        Assert.Equal(t2.Id, _harness.Transport.Current!.TrackId);
        Assert.Equal(TransportStatus.Playing, _harness.Transport.Status);
    }

    [Fact]
    public void PlayTrack_JumpsAheadOfQueued()
    {
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        var t3 = TestShow.Track("three");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1), TestShow.Entry(t2), TestShow.Entry(t3));
        _harness.Submit(new LoadShow([t1, t2, t3], [p], p.Id));
        _harness.Submit(new EnqueueTrack(t3.Id));

        _harness.Submit(new PlayTrack(t2.Id));

        Assert.Equal(t2.Id, _harness.Transport.Current!.TrackId);

        _harness.Submit(new Next());

        Assert.Equal(t3.Id, _harness.Transport.Current!.TrackId);
    }

    [Fact]
    public void PlayTrack_UnknownTrack_Rejects()
    {
        var t1 = TestShow.Track("one");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1));
        _harness.Submit(new LoadShow([t1], [p], p.Id));

        var seq = _harness.Submit(new PlayTrack(TrackId.New()));

        Assert.Equal("unknown-track", _harness.RejectionOf(seq)?.Reason);
        Assert.NotEqual(TransportStatus.Playing, _harness.Transport.Status);
    }

    [Fact]
    public void PlayTrack_Panicked_Rejects()
    {
        var t1 = TestShow.Track("one");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1));
        _harness.Submit(new LoadShow([t1], [p], p.Id));
        _harness.Submit(new Panic());

        var seq = _harness.Submit(new PlayTrack(t1.Id));

        Assert.Equal("panicked", _harness.RejectionOf(seq)?.Reason);
    }
}
