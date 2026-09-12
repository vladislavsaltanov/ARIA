namespace Aria.App.Tests;

using Aria.App.ViewModels;
using Aria.Audio;
using Aria.Core.Commands;
using Aria.Core.Model;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;

[Collection("headless")]
public sealed class DragHeadlessTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aria-drag-headless-{Guid.NewGuid():N}");
    private readonly HeadlessUnitTestSession _session = HeadlessUnitTestSession.StartNew(typeof(App));

    public void Dispose()
    {
        _session.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task DragTrack_FromLibrary_ToPlaylistCenter_AddsEntry()
    {
        await using var host = new AppHost(_directory, null, () => new NullSink(8000, 1), () => new MiniaudioSourceFactory(8000, 1));
        await host.StartAsync();
        var wav = TestWav.Write(_directory, "drag-track.wav");
        var imported = await host.ImportTracksAsync([wav]);
        Assert.Equal(1, imported.Added);
        host.Submit(new CreatePlaylist("DragTarget"));

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline && host.Bus.Snapshot().Show.Playlists.Length == 0)
        {
            await Task.Delay(25);
        }

        await _session.Dispatch(() =>
        {
            using var library = new LibraryViewModel(host.Bus, host.Library!, host.ImportTracksAsync);
            using var playlists = new PlaylistsViewModel(host.Bus, () => host.Library!.Load().Tracks);
            using var queue = new QueueViewModel(host.Bus);
            var window = new Views.MainWindow(null, library, playlists, queue);
            window.Show();
            window.UpdateLayout();

            var trackList = window.FindControl<Views.LibrarySection>("LibrarySection")?.TrackListBox;
            var entryList = window.FindControl<Views.PlaylistCenter>("PlaylistCenter")?.EntryListBox;
            Assert.NotNull(trackList);
            Assert.NotNull(trackList.ItemsPanelRoot);
            Assert.NotNull(entryList);
            Assert.Single(trackList.Items);

            var row = Assert.IsType<ListBoxItem>(trackList.ItemsPanelRoot.Children[0]);
            var pressed = false;
            var handled = false;
            trackList.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
            {
                pressed = true;
                handled = e.Handled;
            }, RoutingStrategies.Bubble, handledEventsToo: true);
            var from = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window);
            var target = entryList.TranslatePoint(new Point(entryList.Bounds.Width / 2, Math.Max(10, entryList.Bounds.Height / 2)), window);
            Assert.NotNull(from);
            Assert.NotNull(target);

            window.MouseDown(from.Value, MouseButton.Left);
            Assert.True(pressed, $"PointerPressed did not reach library ListBox (handled={handled})");
            var current = from.Value;
            for (var step = 1; step <= 10; step++)
            {
                current = new Point(
                    from.Value.X + (target.Value.X - from.Value.X) * step / 10,
                    from.Value.Y + (target.Value.Y - from.Value.Y) * step / 10);
                window.MouseMove(current);
            }
            window.MouseUp(current, MouseButton.Left);

            window.Close();
            return 0;
        }, CancellationToken.None);

        var deadline2 = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline2 && host.Bus.Snapshot().Show.Playlists[0].Entries.Length == 0)
        {
            await Task.Delay(25);
        }
        Assert.Single(host.Bus.Snapshot().Show.Playlists[0].Entries);
    }
}
