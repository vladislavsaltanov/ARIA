namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.State;

public sealed class FaultedTests
{
    [Fact]
    public void EngineFault_MarksTrackFaulted()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Project("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new Play());
        var handle = h.Engine.Last!.Handle;

        h.Engine.Fault(handle);

        Assert.Contains(t1.Id, h.Snapshot.Transport.Faulted);
    }

    [Fact]
    public void ReplayFaultedTrack_ClearsFaultMark()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        var e1 = TestShow.Entry(t1);
        var e2 = TestShow.Entry(t2);
        var p = TestShow.Project("Main", e1, e2);
        h.Submit(new LoadShow([t1, t2], [p], p.Id));
        h.Submit(new Play());
        h.Engine.Fault(h.Engine.Last!.Handle);
        Assert.Contains(t1.Id, h.Snapshot.Transport.Faulted);

        h.Submit(new JumpTo(e1.Id));

        Assert.Empty(h.Snapshot.Transport.Faulted);
    }

    [Fact]
    public void MarkMissing_FlagsKnownTracksWithoutPlayback()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        var p = TestShow.Project("Main", TestShow.Entry(t1), TestShow.Entry(t2));
        h.Submit(new LoadShow([t1, t2], [p], p.Id));

        h.Submit(new MarkMissing([t1.Id, TrackId.New()]));

        Assert.Equal(t1.Id, Assert.Single(h.Snapshot.Transport.Faulted));
    }

    [Fact]
    public void FreshBoot_HasNoFaults()
    {
        using var h = new Harness();

        Assert.Empty(h.Snapshot.Transport.Faulted);
    }

    [Fact]
    public void RelinkClearsFaultMark()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Project("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new Play());
        h.Engine.Fault(h.Engine.Last!.Handle);
        Assert.Contains(t1.Id, h.Snapshot.Transport.Faulted);

        h.Submit(new MergeTracks([t1 with { FilePath = "/audio/one-relinked.flac" }]));

        Assert.Empty(h.Snapshot.Transport.Faulted);
    }

    [Fact]
    public void RelinkEmitsTransportWithoutFault()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Project("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new Play());
        h.Engine.Fault(h.Engine.Last!.Handle);
        var before = h.Events.OfType<TransportDelta>().Count();

        h.Submit(new MergeTracks([t1 with { FilePath = "/audio/one-relinked.flac" }]));

        Assert.Equal(before + 1, h.Events.OfType<TransportDelta>().Count());
        Assert.Empty(h.Events.OfType<TransportDelta>().Last().State.Faulted);
    }
}
