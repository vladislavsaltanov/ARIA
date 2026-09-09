namespace Aria.App.Tests;

using Aria.App.ViewModels;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.Runtime;
using Aria.Core.State;

public sealed class TransportViewModelTests
{
    private static readonly Track TestTrack = new(
        TrackId.New(), "/audio/test.flac", "test", TimeSpan.FromMinutes(3), new TrackDefaults());

    [Fact]
    public void PlayCommand_SubmitsThroughBus()
    {
        var engine = new StubEngine();
        using var bus = new CommandBus(new ShowController(engine), BusMode.Inline);
        var playlist = new Playlist(PlaylistId.New(), "Main", [new PlaylistEntry(EntryId.New(), TestTrack.Id)]);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([TestTrack], [playlist], playlist.Id));
        using var vm = new TransportViewModel(bus);

        vm.PlayCommand.Execute(null);

        Assert.Equal(TransportStatus.Playing, bus.Snapshot().Transport.Status);
        Assert.Equal("PLAY", vm.StatusText);
        Assert.Equal("test", vm.DisplayName);
        
    }

    [Fact]
    public void TransportDelta_UpdatesStatusAndNames()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        using var vm = new TransportViewModel(bus);

        vm.PanicCommand.Execute(null);

        Assert.True(vm.Panicked);
        Assert.Equal("PANIC", vm.StatusText);
    }

    [Fact]
    public void MonitorPublish_UpdatesRemaining()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var monitor = new PlaybackMonitor();
        using var vm = new TransportViewModel(bus, monitor);
        var deck = new DeckContent(null, TestTrack.Id, "test", null, EndAction.Pause, TimeSpan.FromSeconds(90), TimeSpan.Zero);

        monitor.Bind(new StreamHandle(1), deck);
        monitor.Publish(new StreamHandle(1), TimeSpan.FromSeconds(30));

        Assert.Equal("01:00", vm.Remaining);
    }

    [Fact]
    public void MonitorUnbind_ResetsToPlaceholder()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var monitor = new PlaybackMonitor();
        using var vm = new TransportViewModel(bus, monitor);
        var deck = new DeckContent(null, TestTrack.Id, "test", null, EndAction.Pause, TimeSpan.FromSeconds(90), TimeSpan.Zero);

        monitor.Bind(new StreamHandle(1), deck);
        monitor.Publish(new StreamHandle(1), TimeSpan.FromSeconds(30));
        monitor.Unbind(new StreamHandle(1));

        Assert.Equal("--:--", vm.Remaining);
    }
}
