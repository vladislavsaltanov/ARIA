namespace Aria.App.Tests;

using Aria.App.ViewModels;
using Aria.Audio;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;

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

    [Fact]
    public async Task ImportAudioFilesAsync_UnmatchedFile_RaisesIncompleteEvent()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var track = new Track(TrackId.New(), "/audio/test.flac", "test", TimeSpan.FromMinutes(3), new TrackDefaults());
        var playlist = new Playlist(PlaylistId.New(), "Main", []);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([track], [playlist], playlist.Id));
        using var vm = new PlaylistsViewModel(bus, () => [track],
            audioImport: (_, _) => Task.FromResult(new Aria.App.ImportReport(0, 0, [])));
        string? message = null;
        vm.AudioImportIncomplete += m => message = m;

        var ids = await vm.ImportAudioFilesAsync(["/audio/missing.wav"]);

        Assert.Empty(ids);
        Assert.Equal("файлы не распознаны", vm.PlaylistIoStatus);
        Assert.Contains("missing.wav", message);
    }

    [Fact]
    public async Task ImportAudioFilesAsync_PartialMatch_ReportsCounts_AndRaises()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var track = new Track(TrackId.New(), "/audio/test.flac", "test", TimeSpan.FromMinutes(3), new TrackDefaults());
        var playlist = new Playlist(PlaylistId.New(), "Main", []);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([track], [playlist], playlist.Id));
        using var vm = new PlaylistsViewModel(bus, () => [track],
            audioImport: (_, _) => Task.FromResult(new Aria.App.ImportReport(0, 0, [])));
        string? message = null;
        vm.AudioImportIncomplete += m => message = m;

        var ids = await vm.ImportAudioFilesAsync([track.FilePath, "/audio/missing.wav"]);

        Assert.Single(ids);
        Assert.Contains("добавлено: 1", vm.PlaylistIoStatus);
        Assert.Contains("не распознано: 1", vm.PlaylistIoStatus);
        Assert.Contains("missing.wav", message);
    }

    [Fact]
    public async Task ImportAudioFilesAsync_SuccessStatus_ClearsAfterTtl()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var track = new Track(TrackId.New(), "/audio/test.flac", "test", TimeSpan.FromMinutes(3), new TrackDefaults());
        var playlist = new Playlist(PlaylistId.New(), "Main", []);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([track], [playlist], playlist.Id));
        using var vm = new PlaylistsViewModel(bus, () => [track],
            audioImport: (_, _) => Task.FromResult(new Aria.App.ImportReport(0, 0, [])),
            transientStatusTtl: TimeSpan.FromMilliseconds(20));

        var ids = await vm.ImportAudioFilesAsync([track.FilePath]);

        Assert.Single(ids);
        Assert.Equal("в плейлист добавлено: 1", vm.PlaylistIoStatus);
        await Task.Delay(500);
        Assert.Equal(string.Empty, vm.PlaylistIoStatus);
    }

    [Fact]
    public async Task ImportAudioFilesAsync_Silent_SkipsProgressAndStatus()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var track = new Track(TrackId.New(), "/audio/test.flac", "test", TimeSpan.FromMinutes(3), new TrackDefaults());
        var found = new Track(TrackId.New(), "/audio/found.wav", "found", TimeSpan.FromMinutes(2), new TrackDefaults());
        var playlist = new Playlist(PlaylistId.New(), "Main", []);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([track, found], [playlist], playlist.Id));
        IProgress<string>? captured = new Progress<string>(_ => { });
        using var vm = new PlaylistsViewModel(bus, () => [track, found],
            audioImport: (_, progress) =>
            {
                captured = progress;
                return Task.FromResult(new Aria.App.ImportReport(1, 0, []));
            });

        var ids = await vm.ImportAudioFilesAsync([found.FilePath], silent: true);

        Assert.Equal(found.Id, Assert.Single(ids));
        Assert.Null(captured);
        Assert.Equal(string.Empty, vm.PlaylistIoStatus);
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
