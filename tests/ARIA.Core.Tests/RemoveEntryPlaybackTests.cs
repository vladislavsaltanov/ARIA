namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.State;

public sealed class RemoveEntryPlaybackTests
{
    [Fact]
    public void RemoveEntry_Current_StopsAndDisposesHandle()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var e1 = TestShow.Entry(t1);
        var p = TestShow.Project("Main", e1);
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new Play());
        var handle = h.Engine.Last!.Handle;

        h.Submit(new RemoveEntry(e1.Id));

        Assert.Equal(TransportStatus.Stopped, h.Transport.Status);
        Assert.Null(h.Transport.Current);
        Assert.Contains(handle, h.Engine.Disposed);
    }

    [Fact]
    public void RemoveEntry_Queued_DropsQueueItemKeepsPlaying()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        var e1 = TestShow.Entry(t1);
        var e2 = TestShow.Entry(t2);
        var p = TestShow.Project("Main", e1, e2);
        h.Submit(new LoadShow([t1, t2], [p], p.Id));
        h.Submit(new Play());
        h.Submit(new EnqueueEntry(e2.Id));
        var created = h.Engine.Created.Count;

        h.Submit(new RemoveEntry(e2.Id));

        Assert.Empty(h.Snapshot.Queue.Items);
        Assert.Equal(TransportStatus.Playing, h.Transport.Status);
        Assert.Equal(created, h.Engine.Created.Count);
    }

    [Fact]
    public void DeleteProject_WithCurrent_StopsAndDisposesHandle()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var e1 = TestShow.Entry(t1);
        var p = TestShow.Project("Main", e1);
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new Play());
        var handle = h.Engine.Last!.Handle;

        h.Submit(new DeleteProject(p.Id));

        Assert.Equal(TransportStatus.Stopped, h.Transport.Status);
        Assert.Null(h.Transport.Current);
        Assert.Contains(handle, h.Engine.Disposed);
    }

    [Fact]
    public void DeleteProject_DropsItsQueuedItemsKeepsPlaying()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        var e1 = TestShow.Entry(t1);
        var e2 = TestShow.Entry(t2);
        var p1 = TestShow.Project("One", e1);
        var p2 = TestShow.Project("Two", e2);
        h.Submit(new LoadShow([t1, t2], [p1, p2], p1.Id));
        h.Submit(new Play());
        h.Submit(new EnqueueEntry(e2.Id));

        h.Submit(new DeleteProject(p2.Id));

        Assert.Empty(h.Snapshot.Queue.Items);
        Assert.Equal(TransportStatus.Playing, h.Transport.Status);
        Assert.Equal(t1.Id, h.Transport.Current!.TrackId);
    }
}
