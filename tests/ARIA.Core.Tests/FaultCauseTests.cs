namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.State;

public sealed class FaultCauseTests
{
    [Fact]
    public void EngineFaultWithMissingDetail_RecordsMissingCause()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Project("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new Play());

        h.Engine.Fault(h.Engine.Last!.Handle, "Missing");

        var mark = Assert.Single(h.Snapshot.Transport.FaultCauses);
        Assert.Equal(t1.Id, mark.Track);
        Assert.Equal(SourceOpenFault.Missing, mark.Cause);
    }

    [Fact]
    public void EngineFaultWithUnknownDetail_RecordsUnknownCause()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Project("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new Play());

        h.Engine.Fault(h.Engine.Last!.Handle);

        var mark = Assert.Single(h.Snapshot.Transport.FaultCauses);
        Assert.Equal(SourceOpenFault.Unknown, mark.Cause);
    }

    [Fact]
    public void RelinkClearsFaultCause()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Project("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new Play());
        h.Engine.Fault(h.Engine.Last!.Handle, "Missing");

        h.Submit(new MergeTracks([t1 with { FilePath = "/audio/one-relinked.flac" }]));

        Assert.Empty(h.Snapshot.Transport.FaultCauses);
    }
}
