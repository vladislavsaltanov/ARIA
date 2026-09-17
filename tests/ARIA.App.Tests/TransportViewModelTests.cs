namespace Aria.App.Tests;

using Aria.App.Services;
using Aria.App.ViewModels;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.Runtime;
using Aria.Core.State;
using Avalonia.Media;

public sealed class TransportViewModelTests
{
    private static readonly Track TestTrack = new(
        TrackId.New(), "/audio/test.flac", "test", TimeSpan.FromMinutes(3), new TrackDefaults());

    [Theory]
    [InlineData(0.2, 0.8)]
    [InlineData(0.0, 1.0)]
    public void SmoothLevel_Attack_IsInstant(double shown, double target)
    {
        Assert.Equal(target, TransportViewModel.SmoothLevel(shown, target, 33.0, 250, true));
    }

    [Fact]
    public void SmoothLevel_Release_GlidesTowardTarget()
    {
        var once = TransportViewModel.SmoothLevel(0.8, 0.2, 33.0, 250, true);

        Assert.True(once < 0.8 && once > 0.2);

        var converged = 0.8;
        for (var step = 0; step < 100; step++)
        {
            converged = TransportViewModel.SmoothLevel(converged, 0.2, 33.0, 250, true);
        }

        Assert.Equal(0.2, converged, 3);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SmoothLevel_Disabled_PassesThrough(bool enabledIgnored)
    {
        Assert.Equal(0.2, TransportViewModel.SmoothLevel(0.8, 0.2, 33.0, 250, false));
        Assert.Equal(0.2, TransportViewModel.SmoothLevel(0.8, 0.2, 33.0, 0, true));
    }

    [Fact]
    public void PlayCommand_SubmitsThroughBus()
    {
        var engine = new StubEngine();
        using var bus = new CommandBus(new ShowController(engine), BusMode.Inline);
        var project = new Project(ProjectId.New(), "Main", [new ProjectEntry(EntryId.New(), TestTrack.Id)]);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([TestTrack], [project], project.Id));
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

        Assert.Equal(70.79, vm.VolumePercent, 2);
        Assert.Equal("Громкость — -6.0 дБ", vm.VolumeDbText);

        vm.VolumePercent = 50;

        Assert.Equal(-12.04, bus.Snapshot().Mixer.MasterGainDb, 2);
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
        Assert.False(vm.LufsHot);

        meters.Publish(-3.0);

        Assert.True(vm.LufsHot);
    }

    [Fact]
    public void Levels_PublishChannelPeaksAndClip()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var meters = new MeterMonitor();
        using var vm = new TransportViewModel(bus, null, null, meters);

        Assert.Equal(0, vm.LevelLeft);
        Assert.Equal(0, vm.LevelRight);
        Assert.False(vm.LevelHot);

        meters.Publish(-20.0, 0.8f, 0.3f);

        Assert.Equal(0.8, vm.LevelLeft, 3);
        Assert.Equal(0.3, vm.LevelRight, 3);
        Assert.False(vm.LevelHot);

        Assert.Equal("#FFECECEC", ((SolidColorBrush)vm.LevelBarBrush).Color.ToString(), StringComparer.OrdinalIgnoreCase);

        meters.Publish(-20.0, 0.5f, 1.0f);

        Assert.True(vm.LevelHot);
        Assert.Equal("#FFE5484D", ((SolidColorBrush)vm.LevelBarBrush).Color.ToString(), StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(-30.0, "#FFECECEC")]
    [InlineData(-15.0, "#FF3FB950")]
    [InlineData(-12.0, "#FF3FB950")]
    [InlineData(-9.0, "#FFD29922")]
    [InlineData(-7.0, "#FFD29922")]
    [InlineData(-5.0, "#FFE5484D")]
    [InlineData(-1.0, "#FFE5484D")]
    public void Lufs_Zones_PaintBar(double lufs, string expected)
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var meters = new MeterMonitor();
        using var vm = new TransportViewModel(bus, null, null, meters);

        meters.Publish(lufs);

        Assert.Equal(expected, ((SolidColorBrush)vm.LufsBarBrush).Color.ToString(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Lufs_CustomZones_FromMixer()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var meters = new MeterMonitor();
        using var vm = new TransportViewModel(bus, null, null, meters);
        bus.Submit(new ClientId("setup"), 2, new SetGlobalAudio(GlobalAudioSettings.Default with { MeterZones = new LufsMeterZones(-10.0, -5.0, -2.0) }));

        meters.Publish(-13.5);

        Assert.False(vm.LufsHot);
        Assert.Equal("#FFECECEC", ((SolidColorBrush)vm.LufsBarBrush).Color.ToString(), StringComparer.OrdinalIgnoreCase);

        meters.Publish(-7.0);

        Assert.Equal("#FF3FB950", ((SolidColorBrush)vm.LufsBarBrush).Color.ToString(), StringComparer.OrdinalIgnoreCase);
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
        var project = new Project(ProjectId.New(), "Main", [new ProjectEntry(EntryId.New(), TestTrack.Id)]);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([TestTrack], [project], project.Id));
        bus.Submit(new ClientId("setup"), 2, new Play());

        Assert.Equal("00:00:00", vm.ShowClockText);

        bus.Submit(new ClientId("setup"), 3, new StartShowClock());
        bus.Submit(new ClientId("setup"), 4, new TickShowClock());

        Assert.Equal("00:00:01", vm.ShowClockText);
    }

    [Fact]
    public void ClockCommands_DriveShowClock()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        using var vm = new TransportViewModel(bus);
        var project = new Project(ProjectId.New(), "Main", [new ProjectEntry(EntryId.New(), TestTrack.Id)]);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([TestTrack], [project], project.Id));

        Assert.False(vm.ClockRunning);

        vm.StartClockCommand.Execute(null);

        Assert.True(vm.ClockRunning);
        Assert.True(bus.Snapshot().Show.Clock.Running);

        vm.PauseClockCommand.Execute(null);

        Assert.False(vm.ClockRunning);
        Assert.False(bus.Snapshot().Show.Clock.Running);

        bus.Submit(new ClientId("setup"), 2, new StartShowClock());
        bus.Submit(new ClientId("setup"), 3, new TickShowClock());
        vm.ResetClockCommand.Execute(null);

        Assert.False(vm.ClockRunning);
        Assert.Equal("00:00:00", vm.ShowClockText);
    }

    [Fact]
    public void TogglePlayPause_DispatchesByState()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        using var vm = new TransportViewModel(bus);
        var project = new Project(ProjectId.New(), "Main", [new ProjectEntry(EntryId.New(), TestTrack.Id)]);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([TestTrack], [project], project.Id));

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
        var project = new Project(ProjectId.New(), "Main", [new ProjectEntry(EntryId.New(), TestTrack.Id), new ProjectEntry(EntryId.New(), second.Id)]);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([TestTrack, second], [project], project.Id));

        vm.PlayCommand.Execute(null);

        Assert.Equal("Далее: two", vm.NextLine);
    }

    [Fact]
    public void VolumePercent_SnapsNearHundred_AndShowsPercentLabel()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        using var vm = new TransportViewModel(bus);

        vm.VolumePercent = 98.7;

        Assert.Equal(100, vm.VolumePercent);
        Assert.Equal("100%", vm.VolumePercentText);
        Assert.InRange(bus.Snapshot().Mixer.MasterGainDb, -0.1, 0.1);
    }

    [Fact]
    public void VolumePercent_HalfGivesAudibleLevel()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        using var vm = new TransportViewModel(bus);

        vm.VolumePercent = 50;

        Assert.InRange(bus.Snapshot().Mixer.MasterGainDb, -13.0, -11.0);
    }

    [Fact]
    public void VolumePercent_EndpointsMapToFloorAndCeiling()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        using var vm = new TransportViewModel(bus);

        vm.VolumePercent = 100;
        Assert.InRange(bus.Snapshot().Mixer.MasterGainDb, -0.1, 0.1);

        vm.VolumePercent = 125;
        Assert.InRange(bus.Snapshot().Mixer.MasterGainDb, 3.7, 4.0);

        vm.VolumePercent = 0;
        Assert.InRange(bus.Snapshot().Mixer.MasterGainDb, -80.1, -79.9);
    }

    [Fact]
    public void VolumePercent_LowerQuarterStaysAudible()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        using var vm = new TransportViewModel(bus);

        vm.VolumePercent = 25;

        Assert.InRange(bus.Snapshot().Mixer.MasterGainDb, -25.0, -23.0);
    }

    [Fact]
    public void DisplayName_TruncatesLongNames_ToHundredChars()
    {
        var longName = new string('н', 140);
        var longTrack = new Track(TrackId.New(), "/audio/long.flac", longName, TimeSpan.FromMinutes(3), new TrackDefaults());
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var project = new Project(ProjectId.New(), "Main", [new ProjectEntry(EntryId.New(), longTrack.Id)]);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([longTrack], [project], project.Id));
        using var vm = new TransportViewModel(bus);

        vm.PlayCommand.Execute(null);

        Assert.Equal(101, vm.DisplayName.Length);
        Assert.Equal(longName[..100] + "…", vm.DisplayName);
    }

    [Fact]
    public void NextLine_TruncatesLongNames_ToHundredChars()
    {
        var longName = new string('т', 140);
        var first = new Track(TrackId.New(), "/audio/one.flac", "one", TimeSpan.FromMinutes(3), new TrackDefaults());
        var second = new Track(TrackId.New(), "/audio/two.flac", longName, TimeSpan.FromMinutes(3), new TrackDefaults());
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var project = new Project(ProjectId.New(), "Main", [new ProjectEntry(EntryId.New(), first.Id), new ProjectEntry(EntryId.New(), second.Id)]);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([first, second], [project], project.Id));
        using var vm = new TransportViewModel(bus);

        vm.PlayCommand.Execute(null);

        Assert.Equal(101, vm.NextLine.Length);
        Assert.EndsWith("…", vm.NextLine);
    }

    [Fact]
    public void RowSettings_UseFileName_SwitchesHeaderDisplay()
    {
        var tagged = new Track(TrackId.New(), "/audio/rain.flac", "Осенний дождь", TimeSpan.FromMinutes(3), new TrackDefaults());
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var project = new Project(ProjectId.New(), "Main", [new ProjectEntry(EntryId.New(), tagged.Id)]);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([tagged], [project], project.Id));
        using var vm = new TransportViewModel(bus, trackSource: () => [tagged]);

        vm.PlayCommand.Execute(null);

        Assert.Equal("Осенний дождь", vm.DisplayName);

        vm.UpdateRowSettings(new AppSettings(true, "{name}", Smoothing.Default));

        Assert.Equal("rain.flac", vm.DisplayName);

        vm.UpdateRowSettings(AppSettings.Default);

        Assert.Equal("Осенний дождь", vm.DisplayName);
    }
}
