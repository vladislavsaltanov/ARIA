using Aria.App.Tests;
using Aria.App.ViewModels;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;
using System.Collections.Immutable;

public sealed class RailDropPlaylistTests
{
    [Fact]
    public async Task DropFolder_CreatesPlaylistNamedAfterFolder_WithItsTracks()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var bandDir = Path.Combine(root, "band");
        Directory.CreateDirectory(bandDir);
        try
        {
            await DropFolder_CreatesPlaylistNamedAfterFolder_WithItsTracksCore(bandDir);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private async Task DropFolder_CreatesPlaylistNamedAfterFolder_WithItsTracksCore(string bandDir)
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var first = new Track(TrackId.New(), Path.Combine(bandDir, "track1.flac"), "track1", TimeSpan.FromMinutes(3), new TrackDefaults());
        var second = new Track(TrackId.New(), Path.Combine(bandDir, "track2.flac"), "track2", TimeSpan.FromMinutes(4), new TrackDefaults());
        var main = new Playlist(PlaylistId.New(), "Main", []);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([first, second], [main], main.Id));
        using var vm = new PlaylistsViewModel(bus, () => [first, second],
            audioImport: (_, _) => Task.FromResult(new Aria.App.ImportReport(2, 0, [])));

        await vm.ImportDroppedPathsAsync([bandDir]);

        Assert.Equal("band", vm.SelectedPlaylist?.Name);
        Assert.Equal(2, vm.SelectedPlaylist?.Entries.Count);
        Assert.Empty(vm.Playlists.Single(p => p.Name == "Main").Entries);
    }

    [Fact]
    public async Task DropFiles_GoesToSelectedPlaylist()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var track = new Track(TrackId.New(), "/audio/single.flac", "single", TimeSpan.FromMinutes(3), new TrackDefaults());
        var main = new Playlist(PlaylistId.New(), "Main", []);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([track], [main], main.Id));
        using var vm = new PlaylistsViewModel(bus, () => [track],
            audioImport: (_, _) => Task.FromResult(new Aria.App.ImportReport(1, 0, [])));

        await vm.ImportDroppedPathsAsync(["/audio/single.flac"]);

        Assert.Equal("Main", vm.SelectedPlaylist?.Name);
        Assert.Single(vm.Playlists);
        Assert.Single(vm.SelectedPlaylist?.Entries!);
    }

    [Fact]
    public async Task DropFolder_NameClash_Uniquifies()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var bandDir = Path.Combine(root, "band");
        Directory.CreateDirectory(bandDir);
        try
        {
            await DropFolder_NameClash_UniquifiesCore(bandDir);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private async Task DropFolder_NameClash_UniquifiesCore(string bandDir)
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var track = new Track(TrackId.New(), Path.Combine(bandDir, "track1.flac"), "track1", TimeSpan.FromMinutes(3), new TrackDefaults());
        var main = new Playlist(PlaylistId.New(), "Main", []);
        var band = new Playlist(PlaylistId.New(), "band", []);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([track], [main, band], main.Id));
        using var vm = new PlaylistsViewModel(bus, () => [track],
            audioImport: (_, _) => Task.FromResult(new Aria.App.ImportReport(1, 0, [])));

        await vm.ImportDroppedPathsAsync([bandDir]);

        Assert.Equal("band 2", vm.SelectedPlaylist?.Name);
        Assert.Equal(3, vm.Playlists.Count);
    }
}
