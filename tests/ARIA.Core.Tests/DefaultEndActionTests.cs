namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.State;

public sealed class DefaultEndActionTests
{
    [Fact]
    public void GlobalPause_AppliesWhenNoOverride()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1), TestShow.Entry(t2));
        h.Submit(new LoadShow([t1, t2], [p], p.Id));
        h.Submit(new SetDefaultEndAction(EndAction.Pause));
        h.Submit(new Play());

        h.Engine.End(h.Engine.Created[0].Handle, StreamEndReason.Completed);

        Assert.Equal(TransportStatus.Paused, h.Transport.Status);
        Assert.Equal(t1.Id, h.Transport.Current!.TrackId);
    }

    [Fact]
    public void GlobalReplay_RestartsSameTrack()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new SetDefaultEndAction(EndAction.Replay));
        h.Submit(new Play());

        h.Engine.End(h.Engine.Created[0].Handle, StreamEndReason.Completed);

        Assert.Equal(2, h.Engine.Created.Count);
        Assert.Equal(t1.Id, h.Transport.Current!.TrackId);
        Assert.Equal(TransportStatus.Playing, h.Transport.Status);
    }

    [Fact]
    public void GlobalAdvance_MatchesDefaultBehavior()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1), TestShow.Entry(t2));
        h.Submit(new LoadShow([t1, t2], [p], p.Id));
        h.Submit(new SetDefaultEndAction(EndAction.Advance));
        h.Submit(new Play());

        h.Engine.End(h.Engine.Created[0].Handle, StreamEndReason.Completed);

        Assert.Equal(t2.Id, h.Transport.Current!.TrackId);
    }

    [Fact]
    public void EntryOverride_BeatsGlobal()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var entry = new PlaylistEntry(EntryId.New(), t1.Id, new PlaylistOverrides(EndAction: EndAction.Replay));
        var p = TestShow.Playlist("Main", entry);
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new SetDefaultEndAction(EndAction.Pause));
        h.Submit(new Play());

        h.Engine.End(h.Engine.Created[0].Handle, StreamEndReason.Completed);

        Assert.Equal(2, h.Engine.Created.Count);
        Assert.Equal(t1.Id, h.Transport.Current!.TrackId);
    }

    [Fact]
    public void NonDefaultTrackDefault_BeatsGlobal()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one", EndAction.Pause);
        var p = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new SetDefaultEndAction(EndAction.Replay));
        h.Submit(new Play());

        h.Engine.End(h.Engine.Created[0].Handle, StreamEndReason.Completed);

        Assert.Equal(TransportStatus.Paused, h.Transport.Status);
        Assert.Equal(t1.Id, h.Transport.Current!.TrackId);
    }

    [Fact]
    public void SetDefaultEndAction_UndefinedValue_IsRejected()
    {
        using var h = new Harness();

        var seq = h.Submit(new SetDefaultEndAction((EndAction)99));

        Assert.Equal("default-end-action-unknown", h.RejectionOf(seq)!.Reason);
    }
}
