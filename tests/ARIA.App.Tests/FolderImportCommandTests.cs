namespace Aria.App.Tests;

using Aria.App.Services;
using Aria.App.ViewModels;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;

public sealed class FolderImportCommandTests
{
    [Fact]
    public async Task ImportAudioFolderCommand_PassesPickedFolderToPipeline()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var track = new Track(TrackId.New(), "/audio/test.flac", "test", TimeSpan.FromMinutes(3), new TrackDefaults());
        var playlist = new Playlist(PlaylistId.New(), "Main", []);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([track], [playlist], playlist.Id));
        IReadOnlyList<string>? received = null;
        using var vm = new PlaylistsViewModel(bus, () => [track],
            audioImport: (inputs, _) =>
            {
                received = inputs;
                return Task.FromResult(new Aria.App.ImportReport(0, 0, []));
            },
            folderPicker: () => Task.FromResult<IReadOnlyList<string>>(["/audio/folder"]));

        await vm.ImportAudioFolderCommand.ExecuteAsync(null);

        Assert.NotNull(received);
        Assert.Equal("/audio/folder", Assert.Single(received));
    }

    [Fact]
    public async Task ImportAudioFolderCommand_EmptyPick_SkipsPipeline()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var track = new Track(TrackId.New(), "/audio/test.flac", "test", TimeSpan.FromMinutes(3), new TrackDefaults());
        var playlist = new Playlist(PlaylistId.New(), "Main", []);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([track], [playlist], playlist.Id));
        var called = false;
        using var vm = new PlaylistsViewModel(bus, () => [track],
            audioImport: (_, _) =>
            {
                called = true;
                return Task.FromResult(new Aria.App.ImportReport(0, 0, []));
            },
            folderPicker: () => Task.FromResult<IReadOnlyList<string>>([]));

        await vm.ImportAudioFolderCommand.ExecuteAsync(null);

        Assert.False(called);
        Assert.Equal(string.Empty, vm.PlaylistIoStatus);
    }

    [Fact]
    public void AudioFileTypes_CoversHostAndPickers()
    {
        Assert.True(AudioFileTypes.Extensions.SequenceEqual([".wav", ".flac", ".mp3", ".ogg"], StringComparer.OrdinalIgnoreCase));
        Assert.True(AudioFileTypes.Filter.Patterns!.SequenceEqual(["*.wav", "*.flac", "*.mp3", "*.ogg"], StringComparer.Ordinal));
    }
}
