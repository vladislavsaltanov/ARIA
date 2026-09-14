namespace Aria.App.Tests;

using Aria.App.Services;
using Aria.App.ViewModels;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;

public sealed class SettingsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"aria-settings-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefault()
    {
        var settings = new AppSettingsStore(_path).Load();

        Assert.False(settings.UseFileName);
        Assert.Equal("{name}", settings.RowFormat);
    }

    [Fact]
    public void Roundtrip_PreservesSettings()
    {
        var store = new AppSettingsStore(_path);
        store.Save(new AppSettings(true, "{position} {filename}", Smoothing.Default));

        var loaded = store.Load();

        Assert.True(loaded.UseFileName);
        Assert.Equal("{position} {filename}", loaded.RowFormat);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefault()
    {
        File.WriteAllText(_path, "{ nope");

        Assert.Equal(AppSettings.Default, new AppSettingsStore(_path).Load());
    }

    [Fact]
    public void Load_BlankFormat_FallsBackToDefault()
    {
        new AppSettingsStore(_path).Save(new AppSettings(false, "  ", Smoothing.Default));

        Assert.Equal("{name}", new AppSettingsStore(_path).Load().RowFormat);
    }

    [Theory]
    [InlineData("{name}", "Осенний дождь")]
    [InlineData("{position} {name}", "01 Осенний дождь")]
    [InlineData("{filename}", "rain.flac")]
    [InlineData("{name} [{duration}]", "Осенний дождь [03:42]")]
    [InlineData("{unknown}", "{unknown}")]
    [InlineData("", "Осенний дождь")]
    public void Format_ReplacesTokens(string format, string expected)
    {
        Assert.Equal(expected, RowFormatter.Format(format, "01", "Осенний дождь", "rain.flac", "03:42"));
    }

    [Fact]
    public void DisplayName_UseFileName_SwitchesSource()
    {
        Assert.Equal("rain.flac", RowFormatter.DisplayName(new AppSettings(true, "{name}", Smoothing.Default), "Осенний дождь", "rain.flac"));
        Assert.Equal("Осенний дождь", RowFormatter.DisplayName(AppSettings.Default, "Осенний дождь", "rain.flac"));
    }

    [Fact]
    public void TrySetGesture_Conflict_ReturnsActionLabel()
    {
        using var bus = NewBus();
        using var viewModel = NewSettings(bus);
        var pause = viewModel.Hotkeys.Gestures.First(g => g.Action == "pause");

        var error = viewModel.Hotkeys.TrySetGesture(pause, "Space");

        Assert.Contains("Воспроизведение", error);
        Assert.Equal("Esc", pause.Gesture);
    }

    [Fact]
    public void TrySetGesture_Reserved_ReturnsSystemError()
    {
        using var bus = NewBus();
        using var viewModel = NewSettings(bus);
        var pause = viewModel.Hotkeys.Gestures.First(g => g.Action == "pause");

        Assert.Equal("жест занят системой macOS", viewModel.Hotkeys.TrySetGesture(pause, "Meta+Q"));
    }

    [Fact]
    public void TrySetGesture_FreeGesture_AppliesAndPersists()
    {
        using var bus = NewBus();
        var path = Path.Combine(Path.GetTempPath(), $"aria-hotkeys-{Guid.NewGuid():N}.json");
        try
        {
            using var viewModel = new SettingsViewModel(bus, new HotkeyService(HotkeyConfig.Default, _ => { }), path, new AppSettingsStore(Path.Combine(Path.GetTempPath(), $"aria-row-{Guid.NewGuid():N}.json")));

            var pause = viewModel.Hotkeys.Gestures.First(g => g.Action == "pause");
            Assert.Null(viewModel.Hotkeys.TrySetGesture(pause, "F9"));

            Assert.Equal("f9", viewModel.Hotkeys.Gestures.First(g => g.Action == "pause").Gesture);
            var dispatched = new List<string>();
            var reloaded = new HotkeyService(HotkeyConfig.Load(path), dispatched.Add);
            Assert.True(reloaded.TryHandle("F9"));
            Assert.Equal(["pause"], dispatched);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ResetClock_SubmitsCommand()
    {
        using var bus = NewBus();
        using var viewModel = NewSettings(bus);
        var track = new Track(TrackId.New(), "/audio/x.flac", "x", TimeSpan.FromMinutes(1), new TrackDefaults());
        var playlist = new Playlist(PlaylistId.New(), "Main", [new PlaylistEntry(EntryId.New(), track.Id)]);
        bus.Submit(new ClientId("setup"), 9, new RestoreShow(
            [track], [playlist], playlist.Id, [], 0, TimeSpan.FromMilliseconds(100),
            TimeSpan.FromSeconds(90), true, []));
        bus.Submit(new ClientId("setup"), 10, new StartShowClock());
        Assert.True(bus.Snapshot().Show.Clock.Running);

        viewModel.Clock.ResetClockCommand.Execute(null);

        Assert.Equal(TimeSpan.Zero, bus.Snapshot().Show.Clock.Elapsed);
        Assert.False(bus.Snapshot().Show.Clock.Running);
    }

    [Fact]
    public void PanicFadeMs_SubmitsAndSyncs()
    {
        using var bus = NewBus();
        using var viewModel = NewSettings(bus);

        viewModel.Engine.PanicFadeMs = 250;

        Assert.Equal(TimeSpan.FromMilliseconds(250), bus.Snapshot().Mixer.PanicFade);
        Assert.Equal(250, viewModel.Engine.PanicFadeMs);
    }

    [Fact]
    public void SaveRowSettings_PersistsAndNotifies()
    {
        using var bus = NewBus();
        var path = Path.Combine(Path.GetTempPath(), $"aria-row-{Guid.NewGuid():N}.json");
        AppSettings? applied = null;
        try
        {
            using var viewModel = new SettingsViewModel(
                bus, new HotkeyService(HotkeyConfig.Default, _ => { }), Path.Combine(Path.GetTempPath(), $"aria-hk-{Guid.NewGuid():N}.json"),
                new AppSettingsStore(path), s => applied = s);
            viewModel.RowFormat.UseFileName = true;
            viewModel.RowFormat.RowFormat = "{filename}";

            viewModel.RowFormat.SaveRowSettingsCommand.Execute(null);

            Assert.Equal(new AppSettings(true, "{filename}", Smoothing.Default), new AppSettingsStore(path).Load());
        }
        finally
        {
            File.Delete(path);
        }
        Assert.Equal(new AppSettings(true, "{filename}", Smoothing.Default), applied);
    }

    [Fact]
    public void Smoothing_Settings_SubmitAndPersist()
    {
        using var bus = NewBus();
        var path = Path.Combine(Path.GetTempPath(), $"aria-smooth-{Guid.NewGuid():N}.json");
        try
        {
            using var viewModel = new SettingsViewModel(
                bus, new HotkeyService(HotkeyConfig.Default, _ => { }), Path.Combine(Path.GetTempPath(), $"aria-hk-{Guid.NewGuid():N}.json"),
                new AppSettingsStore(path));

            viewModel.Engine.SmoothingEnabled = true;
            viewModel.Engine.ManualCrossfadeMs = 400;
            viewModel.Engine.AutoCrossfadeMs = 900;
            viewModel.Engine.StartFadeMs = 250;
            viewModel.Engine.StopFadeMs = 300;
            viewModel.Engine.SeekFadeMs = 350;

            var expected = new Smoothing(true, TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(900), TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(350));
            Assert.Equal(expected, bus.Snapshot().Mixer.Smoothing);
            Assert.Equal(expected, new AppSettingsStore(path).Load().Smoothing);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Smoothing_Roundtrip_PreservesValues()
    {
        var store = new AppSettingsStore(_path);
        var smoothing = new Smoothing(true, TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(900), TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(350));
        store.Save(new AppSettings(false, "{name}", smoothing));

        Assert.Equal(smoothing, store.Load().Smoothing);
    }

    private static CommandBus NewBus() => new(new ShowController(new StubEngine()), BusMode.Inline);

    private static SettingsViewModel NewSettings(CommandBus bus) => new(
        bus,
        new HotkeyService(HotkeyConfig.Default, _ => { }),
        Path.Combine(Path.GetTempPath(), $"aria-hk-{Guid.NewGuid():N}.json"),
        new AppSettingsStore(Path.Combine(Path.GetTempPath(), $"aria-row-{Guid.NewGuid():N}.json")));
}
