namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.Runtime;
using Aria.Core.State;

public sealed class PlaybackMonitorTests
{
    [Fact]
    public void Publish_ComputesFilePosition_AndRemaining()
    {
        var monitor = new PlaybackMonitor();
        var deck = new DeckContent(null, TrackId.New(), "Буря", null, EndAction.Advance, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(10));
        var handle = new StreamHandle(1);
        monitor.Bind(handle, deck);

        monitor.Publish(handle, TimeSpan.FromSeconds(5));

        var latest = monitor.Latest;
        Assert.NotNull(latest);
        Assert.Equal("Буря", latest.Deck.DisplayName);
        Assert.Equal(TimeSpan.FromSeconds(15), latest.FilePosition);
        Assert.Equal(TimeSpan.FromSeconds(165), latest.Remaining);
    }

    [Fact]
    public void Publish_WithCueOut_ComputesRemainingFromCueOut()
    {
        var monitor = new PlaybackMonitor();
        var deck = new DeckContent(null, TrackId.New(), "d", null, EndAction.Pause, TimeSpan.FromMinutes(3), TimeSpan.Zero, TimeSpan.FromSeconds(60));
        var handle = new StreamHandle(1);
        monitor.Bind(handle, deck);

        monitor.Publish(handle, TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.FromSeconds(50), monitor.Latest!.Remaining);
    }

    [Fact]
    public void Publish_ClampsRemainingAtZero()
    {
        var monitor = new PlaybackMonitor();
        var deck = new DeckContent(null, TrackId.New(), "d", null, EndAction.Stop, TimeSpan.FromSeconds(60), TimeSpan.Zero, TimeSpan.FromSeconds(30));
        var handle = new StreamHandle(1);
        monitor.Bind(handle, deck);

        monitor.Publish(handle, TimeSpan.FromSeconds(45));

        Assert.Equal(TimeSpan.Zero, monitor.Latest!.Remaining);
    }

    [Fact]
    public void Publish_UnknownHandle_IsIgnored()
    {
        var monitor = new PlaybackMonitor();
        var deck = new DeckContent(null, TrackId.New(), "d", null, EndAction.Advance, TimeSpan.FromMinutes(3), TimeSpan.Zero);
        var handle = new StreamHandle(1);
        monitor.Bind(handle, deck);
        monitor.Publish(handle, TimeSpan.FromSeconds(1));
        var before = monitor.Latest;

        monitor.Publish(new StreamHandle(999), TimeSpan.FromSeconds(2));

        Assert.Same(before, monitor.Latest);
    }

    [Fact]
    public void Unbind_ClearsLatest_AndIgnoresFurtherPublishes()
    {
        var monitor = new PlaybackMonitor();
        var deck = new DeckContent(null, TrackId.New(), "d", null, EndAction.Advance, TimeSpan.FromMinutes(3), TimeSpan.Zero);
        var handle = new StreamHandle(1);
        monitor.Bind(handle, deck);
        monitor.Publish(handle, TimeSpan.FromSeconds(1));
        Assert.NotNull(monitor.Latest);

        monitor.Unbind(handle);

        Assert.Null(monitor.Latest);

        monitor.Publish(handle, TimeSpan.FromSeconds(3));

        Assert.Null(monitor.Latest);
    }

    [Fact]
    public void Bind_SameHandle_ReplacesContext()
    {
        var monitor = new PlaybackMonitor();
        var first = new DeckContent(null, TrackId.New(), "first", null, EndAction.Advance, TimeSpan.FromMinutes(3), TimeSpan.Zero);
        var second = new DeckContent(null, TrackId.New(), "second", null, EndAction.Advance, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(10));
        var handle = new StreamHandle(1);
        monitor.Bind(handle, first);
        monitor.Bind(handle, second);

        monitor.Publish(handle, TimeSpan.Zero);

        Assert.Equal("second", monitor.Latest!.Deck.DisplayName);
        Assert.Equal(TimeSpan.FromSeconds(10), monitor.Latest.FilePosition);
    }

    [Fact]
    public void Changed_FiresOnPublish_WithSnapshot()
    {
        var monitor = new PlaybackMonitor();
        var deck = new DeckContent(null, TrackId.New(), "Буря", null, EndAction.Advance, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(10));
        var handle = new StreamHandle(1);
        monitor.Bind(handle, deck);
        PositionSnapshot? received = null;
        monitor.Changed += snapshot => received = snapshot;

        monitor.Publish(handle, TimeSpan.FromSeconds(5));

        Assert.NotNull(received);
        Assert.Same(monitor.Latest, received);
        Assert.Equal(TimeSpan.FromSeconds(15), received!.FilePosition);
        Assert.Equal("Буря", received.Deck.DisplayName);
    }

    [Fact]
    public void Controller_EndAdvance_UnbindsOldHandle_BindsNew()
    {
        var engine = new FakeEngine();
        var monitor = new PlaybackMonitor();
        using var bus = new CommandBus(new ShowController(engine, monitor), BusMode.Inline);
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        var entry2 = TestShow.Entry(t2, new ProjectOverrides(EndAction: EndAction.Pause));
        var p = TestShow.Project("Main", TestShow.Entry(t1), entry2);
        var seq = 0L;
        bus.Submit(Harness.Client, ++seq, new LoadShow([t1, t2], [p], p.Id));
        bus.Submit(Harness.Client, ++seq, new Play());
        var first = engine.Created[0].Handle;

        monitor.Publish(first, TimeSpan.FromSeconds(30));

        Assert.Equal(t1.Id, monitor.Latest!.Deck.TrackId);
        Assert.Equal(TimeSpan.FromSeconds(30), monitor.Latest.FilePosition);

        engine.End(first, StreamEndReason.Completed);

        Assert.Equal(2, engine.Created.Count);
        Assert.Equal(t2.FilePath, engine.Last!.Source.FilePath);
        Assert.Null(monitor.Latest);

        monitor.Publish(first, TimeSpan.FromSeconds(40));

        Assert.Null(monitor.Latest);

        var second = engine.Last!.Handle;
        monitor.Publish(second, TimeSpan.FromSeconds(5));

        Assert.Equal(t2.Id, monitor.Latest!.Deck.TrackId);

        engine.End(second, StreamEndReason.Completed);

        Assert.Equal(TransportStatus.Paused, bus.Snapshot().Transport.Status);
        Assert.Null(monitor.Latest);
    }

    [Fact]
    public void Controller_Stop_ClearsLatest()
    {
        var engine = new FakeEngine();
        var monitor = new PlaybackMonitor();
        using var bus = new CommandBus(new ShowController(engine, monitor), BusMode.Inline);
        var t1 = TestShow.Track("one");
        var p = TestShow.Project("Main", TestShow.Entry(t1));
        var seq = 0L;
        bus.Submit(Harness.Client, ++seq, new LoadShow([t1], [p], p.Id));
        bus.Submit(Harness.Client, ++seq, new Play());
        monitor.Publish(engine.Created[0].Handle, TimeSpan.FromSeconds(10));
        Assert.NotNull(monitor.Latest);

        bus.Submit(Harness.Client, ++seq, new Stop());

        Assert.Null(monitor.Latest);
    }

    [Fact]
    public void Controller_Panic_ClearsLatest()
    {
        var engine = new FakeEngine();
        var monitor = new PlaybackMonitor();
        using var bus = new CommandBus(new ShowController(engine, monitor), BusMode.Inline);
        var t1 = TestShow.Track("one");
        var p = TestShow.Project("Main", TestShow.Entry(t1));
        var seq = 0L;
        bus.Submit(Harness.Client, ++seq, new LoadShow([t1], [p], p.Id));
        bus.Submit(Harness.Client, ++seq, new Play());
        monitor.Publish(engine.Created[0].Handle, TimeSpan.FromSeconds(10));
        Assert.NotNull(monitor.Latest);

        bus.Submit(Harness.Client, ++seq, new Panic());

        Assert.Null(monitor.Latest);
    }
}
