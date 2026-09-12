namespace Aria.App.Tests;

using System.Collections.Immutable;
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
        await using var host = await SetupHostAsync();
        var window = await SetupWindowAsync(host);

        await PerformDragAsync(window);

        Assert.Single(await EntriesEventuallyAsync(host, 1));
        await CloseAsync(window);
    }

    [Fact]
    public async Task DragTrack_DropTwice_AddsTwoEntriesNotMore()
    {
        await using var host = await SetupHostAsync();
        var window = await SetupWindowAsync(host);

        await PerformDragAsync(window);
        Assert.Single(await EntriesEventuallyAsync(host, 1));

        await Task.Delay(700);
        await PerformDragAsync(window);
        Assert.Equal(2, (await EntriesEventuallyAsync(host, 2)).Length);

        await CloseAsync(window);
    }

    private async Task<AppHost> SetupHostAsync()
    {
        var host = new AppHost(_directory, null, () => new NullSink(8000, 1), () => new MiniaudioSourceFactory(8000, 1));
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
        return host;
    }

    private async Task<Window> SetupWindowAsync(AppHost host)
    {
        Window? window = null;
        await _session.Dispatch(() =>
        {
            var sync = SynchronizationContext.Current;
            var library = new LibraryViewModel(host.Bus, host.Library!, host.ImportTracksAsync, sync: sync);
            var playlists = new PlaylistsViewModel(host.Bus, () => host.Library!.Load().Tracks, sync: sync);
            var queue = new QueueViewModel(host.Bus, sync);
            window = new Views.MainWindow(null, library, playlists, queue);
            window.Show();
            window.UpdateLayout();

            var trackList = window.FindControl<Views.LibrarySection>("LibrarySection")?.TrackListBox;
            var entryList = window.FindControl<Views.PlaylistCenter>("PlaylistCenter")?.EntryListBox;
            Assert.NotNull(trackList);
            Assert.NotNull(trackList.ItemsPanelRoot);
            Assert.NotNull(entryList);
            Assert.Single(trackList.Items);
            return 0;
        }, CancellationToken.None);
        return window!;
    }

    private async Task PerformDragAsync(Window window)
    {
        await _session.Dispatch(() =>
        {
            window.UpdateLayout();
            var trackList = window.FindControl<Views.LibrarySection>("LibrarySection")!.TrackListBox;
            var entryList = window.FindControl<Views.PlaylistCenter>("PlaylistCenter")!.EntryListBox;

            var row = Assert.IsType<ListBoxItem>(trackList.ItemsPanelRoot!.Children[0]);
            var from = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window);
            var target = entryList.TranslatePoint(new Point(entryList.Bounds.Width / 2, Math.Max(10, entryList.Bounds.Height / 2)), window);
            Assert.NotNull(from);
            Assert.NotNull(target);

            window.MouseDown(from.Value, MouseButton.Left);
            var current = from.Value;
            for (var step = 1; step <= 10; step++)
            {
                current = new Point(
                    from.Value.X + (target.Value.X - from.Value.X) * step / 10,
                    from.Value.Y + (target.Value.Y - from.Value.Y) * step / 10);
                window.MouseMove(current);
            }
            window.MouseUp(current, MouseButton.Left);
            return 0;
        }, CancellationToken.None);
    }

    private static async Task<ImmutableArray<PlaylistEntry>> EntriesEventuallyAsync(AppHost host, int count)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        var entries = host.Bus.Snapshot().Show.Playlists.Length > 0
            ? host.Bus.Snapshot().Show.Playlists[0].Entries
            : default;
        while (DateTime.UtcNow < deadline && entries.Length < count)
        {
            await Task.Delay(25);
            entries = host.Bus.Snapshot().Show.Playlists.Length > 0
                ? host.Bus.Snapshot().Show.Playlists[0].Entries
                : default;
        }
        return entries;
    }

    private async Task CloseAsync(Window window)
    {
        await _session.Dispatch(() =>
        {
            window.Close();
            return 0;
        }, CancellationToken.None);
    }
}
