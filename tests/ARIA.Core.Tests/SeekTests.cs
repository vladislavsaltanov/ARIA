namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.State;

public sealed class SeekTests
{
    [Fact]
    public void SeekTo_WhilePlaying_SeeksEngineWithCueRelativeElapsed()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new Play());
        var handle = h.Engine.Last!.Handle;

        h.Submit(new SeekTo(TimeSpan.FromSeconds(30)));

        Assert.Empty(h.Events.OfType<Rejected>());
        var seek = Assert.Single(h.Engine.Last.Seeks);
        Assert.Equal(TimeSpan.FromSeconds(30), seek);
        Assert.Equal(handle, h.Engine.Last.Handle);
        Assert.Equal(TransportStatus.Playing, h.Transport.Status);
    }

    [Fact]
    public void SeekTo_ClampsIntoCueWindow()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var overrides = new PlaylistOverrides(
            CueIn: TimeSpan.FromSeconds(10),
            CueOut: TimeSpan.FromSeconds(60));
        var p = TestShow.Playlist("Main", TestShow.Entry(t1, overrides));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new Play());

        h.Submit(new SeekTo(TimeSpan.FromSeconds(5)));
        h.Submit(new SeekTo(TimeSpan.FromMinutes(2)));

        var last = h.Engine.Last!;
        Assert.Equal(TimeSpan.Zero, last.Seeks[0]);
        Assert.Equal(TimeSpan.FromSeconds(50), last.Seeks[1]);
    }

    [Fact]
    public void SeekTo_WhilePaused_Seeks()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new Play());
        h.Submit(new Pause());

        h.Submit(new SeekTo(TimeSpan.FromSeconds(30)));

        Assert.Empty(h.Events.OfType<Rejected>());
        Assert.Equal(TimeSpan.FromSeconds(30), Assert.Single(h.Engine.Last!.Seeks));
    }

    [Fact]
    public void SeekTo_WhenStopped_Rejected()
    {
        using var h = new Harness();
        var seq = h.Submit(new SeekTo(TimeSpan.FromSeconds(30)));

        Assert.Equal("nothing-to-seek", h.RejectionOf(seq)?.Reason);
        Assert.Null(h.Engine.Last);
    }

    [Fact]
    public void SeekTo_WhileLocked_Rejected()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new Play());
        h.Submit(new SetLocked(true));
        h.Engine.Last!.Seeks.Clear();

        var seq = h.Submit(new SeekTo(TimeSpan.FromSeconds(30)));

        Assert.Equal("locked", h.RejectionOf(seq)?.Reason);
        Assert.Empty(h.Engine.Last.Seeks);
    }
}