namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.State;

public sealed class ShowClockTests
{
    [Fact]
    public void FreshBoot_ClockIsZeroAndStopped()
    {
        using var h = new Harness();

        Assert.Equal(TimeSpan.Zero, h.Snapshot.Show.Clock.Elapsed);
        Assert.False(h.Snapshot.Show.Clock.Running);
    }

    [Fact]
    public void FirstPlay_StartsClock()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));

        h.Submit(new Play());

        Assert.True(h.Snapshot.Show.Clock.Running);
        Assert.Equal(TimeSpan.Zero, h.Snapshot.Show.Clock.Elapsed);
    }

    [Fact]
    public void Ticks_AccumulateElapsed_WhileRunning()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new Play());

        for (var i = 0; i < 5; i++)
        {
            h.Submit(new TickShowClock());
        }

        Assert.Equal(TimeSpan.FromSeconds(5), h.Snapshot.Show.Clock.Elapsed);
    }

    [Fact]
    public void Pause_DoesNotStopClock()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new Play());
        h.Submit(new TickShowClock());
        h.Submit(new Pause());
        h.Submit(new TickShowClock());

        Assert.True(h.Snapshot.Show.Clock.Running);
        Assert.Equal(TimeSpan.FromSeconds(2), h.Snapshot.Show.Clock.Elapsed);
    }

    [Fact]
    public void Reset_ClearsElapsedAndStops()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new Play());
        h.Submit(new TickShowClock());

        h.Submit(new ResetShowClock());
        h.Submit(new TickShowClock());

        Assert.Equal(TimeSpan.Zero, h.Snapshot.Show.Clock.Elapsed);
        Assert.False(h.Snapshot.Show.Clock.Running);
    }

    [Fact]
    public void LoadShow_ResetsClock()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new Play());
        h.Submit(new TickShowClock());
        h.Submit(new Stop());

        h.Submit(new LoadShow([t1], [p], p.Id));

        Assert.Equal(TimeSpan.Zero, h.Snapshot.Show.Clock.Elapsed);
        Assert.False(h.Snapshot.Show.Clock.Running);
    }

    [Fact]
    public void RestoreShow_RestoresClock_AndTicksContinue()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1));

        h.Submit(new RestoreShow([t1], [p], p.Id, [], 0, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(90), true));
        h.Submit(new TickShowClock());

        Assert.Equal(TimeSpan.FromSeconds(91), h.Snapshot.Show.Clock.Elapsed);
        Assert.True(h.Snapshot.Show.Clock.Running);
    }

    [Fact]
    public void RestoreShow_NegativeElapsed_IsRejected()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1));

        var seq = h.Submit(new RestoreShow([t1], [p], p.Id, [], 0, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(-1), false));

        Assert.Equal("bad-clock", h.RejectionOf(seq)?.Reason);
    }

    [Fact]
    public void Ticks_WhileLocked_KeepCounting()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new Play());
        h.Submit(new SetLocked(true));

        var seq = h.Submit(new TickShowClock());

        Assert.Null(h.RejectionOf(seq));
        Assert.Equal(TimeSpan.FromSeconds(1), h.Snapshot.Show.Clock.Elapsed);
    }

    [Fact]
    public void StartShowClock_StartsStoppedClock()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));

        h.Submit(new StartShowClock());
        h.Submit(new TickShowClock());

        Assert.True(h.Snapshot.Show.Clock.Running);
        Assert.Equal(TimeSpan.FromSeconds(1), h.Snapshot.Show.Clock.Elapsed);
    }

    [Fact]
    public void PauseShowClock_HaltsTicks_AndKeepsElapsed()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new StartShowClock());
        h.Submit(new TickShowClock());

        h.Submit(new PauseShowClock());
        h.Submit(new TickShowClock());

        Assert.False(h.Snapshot.Show.Clock.Running);
        Assert.Equal(TimeSpan.FromSeconds(1), h.Snapshot.Show.Clock.Elapsed);
    }

    [Fact]
    public void StartPause_WhenAlreadyThere_EmitNoDelta()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new StartShowClock());
        var version = h.Snapshot.ShowVersion;

        h.Submit(new StartShowClock());
        Assert.Equal(version, h.Snapshot.ShowVersion);

        h.Submit(new PauseShowClock());
        version = h.Snapshot.ShowVersion;
        h.Submit(new PauseShowClock());
        Assert.Equal(version, h.Snapshot.ShowVersion);
    }

    [Fact]
    public void StartShowClock_WhileLocked_IsRejected()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new SetLocked(true));

        var seq = h.Submit(new StartShowClock());

        Assert.Equal("locked", h.RejectionOf(seq)?.Reason);
        Assert.False(h.Snapshot.Show.Clock.Running);
    }
}
