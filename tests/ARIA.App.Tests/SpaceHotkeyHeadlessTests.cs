namespace Aria.App.Tests;

using Aria.App.ViewModels;
using Aria.App.Views;
using Aria.App.Services;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;

[Collection("headless")]
public sealed class SpaceHotkeyHeadlessTests : IDisposable
{
    private static readonly Track TestTrack = new(
        TrackId.New(), "/audio/space.flac", "space", TimeSpan.FromMinutes(3), new TrackDefaults());

    private readonly HeadlessUnitTestSession _session = HeadlessUnitTestSession.StartNew(typeof(App));

    public void Dispose() => _session.Dispose();

    [Fact]
    public async Task Space_OnFocusedButton_TogglesPlayPause_WithoutClick()
    {
        await _session.Dispatch(() =>
        {
            using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
            using var viewModel = new TransportViewModel(bus);
            var playlist = new Playlist(PlaylistId.New(), "Main", [new PlaylistEntry(EntryId.New(), TestTrack.Id)]);
            bus.Submit(new ClientId("setup"), 1, new LoadShow([TestTrack], [playlist], playlist.Id));
            var window = new MainWindow(null) { DataContext = viewModel };
            window.Show();

            var button = window.FindControl<TransportBar>("TransportBar")?.FindControl<Button>("PlayPauseButton");
            Assert.NotNull(button);
            var clicks = 0;
            button.Click += (_, _) => clicks++;
            button.Focus();

            var args = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Space,
                KeyModifiers = KeyModifiers.None,
                Source = button,
            };
            button.RaiseEvent(args);

            Assert.True(args.Handled);
            button.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyUpEvent,
                Key = Key.Space,
                KeyModifiers = KeyModifiers.None,
                Source = button,
            });
            Assert.Equal(0, clicks);
            Assert.True(viewModel.IsPlaying);

            window.Close();
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Space_InTextBox_TypesNormally()
    {
        await _session.Dispatch(() =>
        {
            using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
            using var viewModel = new TransportViewModel(bus);
            var playlist = new Playlist(PlaylistId.New(), "Main", [new PlaylistEntry(EntryId.New(), TestTrack.Id)]);
            bus.Submit(new ClientId("setup"), 1, new LoadShow([TestTrack], [playlist], playlist.Id));
            var hotkeyFired = false;
            var hotkeys = new HotkeyService(HotkeyConfig.Default, _ => hotkeyFired = true);
            var window = new MainWindow(hotkeys) { DataContext = viewModel };
            window.Show();

            var grid = window.FindControl<Grid>("ContentGrid");
            Assert.NotNull(grid);
            var box = new TextBox { Text = "a" };
            grid.Children.Add(box);
            box.Focus();

            var args = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Space,
                KeyModifiers = KeyModifiers.None,
                Source = box,
            };
            box.RaiseEvent(args);

            Assert.False(args.Handled);
            Assert.False(hotkeyFired);
            Assert.False(viewModel.IsPlaying);

            window.Close();
            return 0;
        }, CancellationToken.None);
    }
}
