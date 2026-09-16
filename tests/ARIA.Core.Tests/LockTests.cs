namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.State;

public sealed class LockTests
{
    [Fact]
    public void FreshBoot_IsUnlocked()
    {
        using var h = new Harness();

        Assert.False(h.Snapshot.Show.Locked);
    }

    [Fact]
    public void SetLocked_FlipsFlag_AndEmitsShowDelta()
    {
        using var h = new Harness();
        var version = h.Snapshot.ShowVersion;

        h.Submit(new SetLocked(true));

        Assert.True(h.Snapshot.Show.Locked);
        Assert.True(h.Snapshot.ShowVersion > version);
    }

    [Fact]
    public void SetLocked_SameValue_IsIdempotent()
    {
        using var h = new Harness();
        h.Submit(new SetLocked(true));
        var version = h.Snapshot.ShowVersion;

        h.Submit(new SetLocked(true));

        Assert.Equal(version, h.Snapshot.ShowVersion);
    }

    [Fact]
    public void Locked_BlocksTransportCommands()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Project("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new SetLocked(true));

        var seq = h.Submit(new Play());

        Assert.Equal("locked", h.RejectionOf(seq)?.Reason);
        Assert.Equal(TransportStatus.Stopped, h.Transport.Status);
    }

    [Fact]
    public void Locked_BlocksLibraryQueueAndMixerCommands()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Project("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new SetLocked(true));

        Assert.Equal("locked", h.RejectionOf(h.Submit(new CreateProject("x")))?.Reason);
        Assert.Equal("locked", h.RejectionOf(h.Submit(new SetMasterGain(-3)))?.Reason);
        Assert.Equal("locked", h.RejectionOf(h.Submit(new SetMuted(true)))?.Reason);
        Assert.Equal("locked", h.RejectionOf(h.Submit(new ClearQueue()))?.Reason);
        Assert.Equal("locked", h.RejectionOf(h.Submit(new ResetShowClock()))?.Reason);
    }

    [Fact]
    public void Panic_BypassesLock()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Project("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new Play());
        h.Submit(new SetLocked(true));

        var seq = h.Submit(new Panic());

        Assert.Null(h.RejectionOf(seq));
        Assert.Equal(TransportStatus.Panicked, h.Transport.Status);
    }

    [Fact]
    public void Unlock_RestoresControl()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Project("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new SetLocked(true));
        h.Submit(new SetLocked(false));

        var seq = h.Submit(new Play());

        Assert.Null(h.RejectionOf(seq));
        Assert.Equal(TransportStatus.Playing, h.Transport.Status);
    }
}
