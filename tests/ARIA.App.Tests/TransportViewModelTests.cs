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
    public void Lock_ToggleLock_DispatchesShowCommand_ButtonsStayEnabled()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        using var vm = new TransportViewModel(bus);

        Assert.False(vm.Locked);
        Assert.True(vm.PlayCommand.CanExecute(null));

        vm.ToggleLock();

        Assert.True(vm.Locked);
        Assert.True(vm.PlayCommand.CanExecute(null));
        Assert.True(vm.PanicCommand.CanExecute(null));
    }

    [Fact]
    public void Lock_KeepsStatusText()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        using var vm = new TransportViewModel(bus);
        vm.PlayCommand.Execute(null);

        var status = vm.StatusText;
        vm.ToggleLock();

        Assert.True(vm.Locked);
        Assert.Equal(status, vm.StatusText);
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

    [Fact]
    public void VolumePercent_RoundTrips_WithBus()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        using var vm = new TransportViewModel(bus);

        bus.Submit(new ClientId("setup"), 1, new SetMasterGain(-6));

        Assert.Equal(80.43, vm.VolumePercent, 2);
        Assert.Equal("Громкость — -6.0 дБ", vm.VolumeDbText);

        vm.VolumePercent = 50;

        Assert.Equal(-34.0, bus.Snapshot().Mixer.MasterGainDb, 6);
    }

    [Fact]
    public void ToggleMute_FlipsMixerMuted()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        using var vm = new TransportViewModel(bus);

        vm.ToggleMuteCommand.Execute(null);

        Assert.True(vm.Muted);
        Assert.True(bus.Snapshot().Mixer.Muted);

        vm.ToggleMuteCommand.Execute(null);

        Assert.False(vm.Muted);
    }

    [Fact]
    public void Lufs_PublishesTextLevelAndHot()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var meters = new MeterMonitor();
        using var vm = new TransportViewModel(bus, null, null, meters);

        Assert.Equal("—", vm.LufsText);

        meters.Publish(-13.5);

        Assert.Equal("-13.5", vm.LufsText);
        Assert.Equal(0.775, vm.LufsLevel, 3);
        Assert.True(vm.LufsHot);
    }

    [Fact]
    public void Lufs_Panicked_ShowsMuted()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var meters = new MeterMonitor();
        using var vm = new TransportViewModel(bus, null, null, meters);
        meters.Publish(-14.2);

        vm.PanicCommand.Execute(null);

        Assert.Equal("MUTED", vm.LufsText);
        Assert.Equal(0, vm.LufsLevel);
    }

    [Fact]
    public void ShowClock_Ticks_UpdateText()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        using var vm = new TransportViewModel(bus);
        var playlist = new Playlist(PlaylistId.New(), "Main", [new PlaylistEntry(EntryId.New(), TestTrack.Id)]);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([TestTrack], [playlist], playlist.Id));
        bus.Submit(new ClientId("setup"), 2, new Play());

        Assert.Equal("00:00:00", vm.ShowClockText);

        bus.Submit(new ClientId("setup"), 3, new TickShowClock());

        Assert.Equal("00:00:01", vm.ShowClockText);
    }

    [Fact]
    public void TogglePlayPause_DispatchesByState()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        using var vm = new TransportViewModel(bus);
        var playlist = new Playlist(PlaylistId.New(), "Main", [new PlaylistEntry(EntryId.New(), TestTrack.Id)]);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([TestTrack], [playlist], playlist.Id));

        vm.TogglePlayPauseCommand.Execute(null);

        Assert.True(vm.IsPlaying);
        Assert.Equal(TransportStatus.Playing, bus.Snapshot().Transport.Status);

        vm.TogglePlayPauseCommand.Execute(null);

        Assert.False(vm.IsPlaying);
        Assert.Equal(TransportStatus.Paused, bus.Snapshot().Transport.Status);
    }

    [Fact]
    public void NextLine_ShowsNextName()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        using var vm = new TransportViewModel(bus);
        var second = new Track(TrackId.New(), "/audio/two.flac", "two", TimeSpan.FromMinutes(3), new TrackDefaults());
        var playlist = new Playlist(PlaylistId.New(), "Main", [new PlaylistEntry(EntryId.New(), TestTrack.Id), new PlaylistEntry(EntryId.New(), second.Id)]);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([TestTrack, second], [playlist], playlist.Id));

        vm.PlayCommand.Execute(null);

        Assert.Equal("Далее: two", vm.NextLine);
    }
}
