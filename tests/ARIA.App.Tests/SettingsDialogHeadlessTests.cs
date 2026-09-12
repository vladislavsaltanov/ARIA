namespace Aria.App.Tests;

using Aria.App.Services;
using Aria.App.ViewModels;
using Aria.App.Views;
using Aria.Core.Commands;
using Aria.Core.Runtime;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;

[Collection("headless")]
public sealed class SettingsDialogHeadlessTests : IDisposable
{
    private readonly HeadlessUnitTestSession _session = HeadlessUnitTestSession.StartNew(typeof(App));

    private readonly string _hotkeysPath = Path.Combine(Path.GetTempPath(), $"aria-hk-{Guid.NewGuid():N}.json");

    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"aria-settings-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        _session.Dispose();
        foreach (var file in new[] { _hotkeysPath, _settingsPath })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public async Task SettingsDialog_Composes_AllSections()
    {
        await _session.Dispatch(() =>
        {
            using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
            using var settings = NewSettings(bus);
            var remote = new ViewModels.RemotePanelViewModel();
            var dialog = new SettingsDialog(settings, remote);
            dialog.Show();

            var pairing = dialog.FindControl<RemotePanel>("PairingPanel");
            Assert.NotNull(pairing);
            Assert.True(pairing.IsVisible);
            Assert.NotNull(dialog.FindControl<Button>("ResetClockButton"));
            Assert.NotNull(dialog.FindControl<Button>("ApplyRowFormatButton"));
            Assert.NotNull(dialog.FindControl<CheckBox>("UseFileNameCheck"));
            Assert.NotNull(dialog.FindControl<TextBox>("RowFormatBox"));

            dialog.Close();
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SettingsDialog_RecordsGesture_ThroughKeyPress()
    {
        await _session.Dispatch(() =>
        {
            using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
            using var settings = NewSettings(bus);
            var dialog = new SettingsDialog(settings);
            dialog.Show();
            var row = settings.Gestures.First(g => g.Action == "replay");

            settings.BeginRecord(row);
            Assert.True(row.IsRecording);

            dialog.KeyPress(Key.F7, RawInputModifiers.Control, PhysicalKey.F7, null);

            Assert.False(row.IsRecording);
            Assert.Equal("Ctrl+f7", settings.Gestures.First(g => g.Action == "replay").Gesture);
            Assert.True(File.Exists(_hotkeysPath));

            dialog.Close();
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SettingsDialog_Escape_CancelsRecording()
    {
        await _session.Dispatch(() =>
        {
            using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
            using var settings = NewSettings(bus);
            var dialog = new SettingsDialog(settings);
            dialog.Show();
            var row = settings.Gestures.First(g => g.Action == "replay");

            settings.BeginRecord(row);
            dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

            Assert.False(row.IsRecording);
            Assert.Equal("Ctrl+R", settings.Gestures.First(g => g.Action == "replay").Gesture);
            dialog.Close();
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MainWindow_HasNoRemoteTab()
    {
        await _session.Dispatch(() =>
        {
            var window = new MainWindow(null);
            window.Show();

            Assert.Null(window.FindControl<TabItem>("RemoteTab"));

            window.Close();
            return 0;
        }, CancellationToken.None);
    }

    private SettingsViewModel NewSettings(CommandBus bus) => new(
        bus,
        new HotkeyService(HotkeyConfig.Default, _ => { }),
        _hotkeysPath,
        new AppSettingsStore(_settingsPath));
}
