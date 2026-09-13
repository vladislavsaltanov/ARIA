namespace Aria.App.Tests;

using Aria.App.ViewModels;
using Aria.Audio;
using Aria.Core.Commands;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Platform.Storage;

[Collection("headless")]
public sealed class OsDropReproTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aria-osdrop-repro-{Guid.NewGuid():N}");
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
    public async Task DropEvent_WithFilePayload_ImportsEntry()
    {
        var wav = TestWav.Write(_directory, "os-drop.wav");
        await using var host = new AppHost(_directory, null, () => new NullSink(8000, 1), () => new MiniaudioSourceFactory(8000, 1));
        await host.StartAsync();
        host.Submit(new CreatePlaylist("OsDropTarget"));
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline && host.Bus.Snapshot().Show.Playlists.Length == 0)
        {
            await Task.Delay(25);
        }
        var primer = TestWav.Write(_directory, "primer.wav");
        await host.ImportTracksAsync([primer]);
        Window? window = null;
        PlaylistsViewModel? vm = null;
        await _session.Dispatch(() =>
        {
            var sync = SynchronizationContext.Current;
            var playlists = new PlaylistsViewModel(host.Bus, () => host.Library!.Load().Tracks, sync: sync)
            {
                AudioImport = host.ImportTracksAsync,
            };
            var queue = new QueueViewModel(host.Bus, sync);
            window = new Views.MainWindow(null, playlistsViewModel: playlists, queueViewModel: queue);
            window.Show();
            window.UpdateLayout();
            vm = playlists;
            return 0;
        }, CancellationToken.None);
        deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline && vm!.SelectedPlaylist is null)
        {
            await Task.Delay(25);
        }
        Assert.NotNull(vm!.SelectedPlaylist);

        await _session.Dispatch(() =>
        {
            window!.UpdateLayout();
            var entryList = window.FindControl<Views.PlaylistCenter>("PlaylistCenter")!.EntryListBox;
            var topLevel = TopLevel.GetTopLevel(entryList);
            Assert.NotNull(topLevel?.StorageProvider);
            var file = topLevel!.StorageProvider.TryGetFileFromPathAsync(new Uri(wav)).GetAwaiter().GetResult();
            Assert.NotNull(file);
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.CreateFile(file));
            entryList.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, transfer, entryList, new Point(10, 10), KeyModifiers.None));
            return 0;
        }, CancellationToken.None);

        deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        var count = 0;
        while (DateTime.UtcNow < deadline && count == 0)
        {
            count = host.Bus.Snapshot().Show.Playlists[0].Entries.Length;
            await Task.Delay(50);
        }
        var status = string.Empty;
        var trackCount = 0;
        await _session.Dispatch(() =>
        {
            status = vm!.PlaylistIoStatus;
            trackCount = host.Library!.Load().Tracks.Length;
            return 0;
        }, CancellationToken.None);
        Assert.True(count == 1, $"entries={count} tracks={trackCount} status='{status}'");
        await _session.Dispatch(() =>
        {
            window!.Close();
            return 0;
        }, CancellationToken.None);
    }
}
