namespace Aria.App.Tests;

using Aria.App.ViewModels;
using Aria.Audio;
using Aria.Core.Commands;

public sealed class PlaylistAudioImportTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aria-playlist-drop-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task ImportAudioFilesAsync_ExternalFile_AddsTrackAndPlaylistEntry()
    {
        await using var host = new AppHost(_directory, null, () => new NullSink(8000, 1), () => new MiniaudioSourceFactory(8000, 1));
        await host.StartAsync();
        host.Submit(new CreatePlaylist("DropTarget"));
        await PollAsync(() => host.Bus.Snapshot().Show.Playlists.Length == 1);
        using var playlists = new PlaylistsViewModel(
            host.Bus,
            () => host.Library!.Load().Tracks,
            audioImport: host.ImportTracksAsync);
        await PollAsync(() => playlists.SelectedPlaylist is not null);

        var external = TestWav.Write(Path.Combine(_directory, "incoming"), "external-drop.wav");
        var ids = await playlists.ImportAudioFilesAsync([external]);

        var trackId = Assert.Single(ids);
        await PollAsync(() => host.Bus.Snapshot().Show.Playlists[0].Entries.Length == 1);
        var entries = host.Bus.Snapshot().Show.Playlists[0].Entries;
        Assert.Equal(trackId, entries[0].TrackId);
        Assert.Contains(host.Library!.Load().Tracks, t => t.Id == trackId);
    }

    private static async Task PollAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }
            await Task.Delay(25);
        }
        throw new TimeoutException("condition not met");
    }
}
