namespace Aria.App.Tests;

using System.IO;
using Aria.App.ViewModels;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.Runtime;

public sealed class FaultCauseMessageTests
{
    [Fact]
    public void UndecodableCause_BeatsMissingFileGuess()
    {
        var track = new Track(TrackId.New(), "/audio/definitely-absent.flac", "absent", TimeSpan.FromMinutes(3), new TrackDefaults());
        var engine = new StubEngine();
        var playlist = new Playlist(PlaylistId.New(), "Main", [new PlaylistEntry(EntryId.New(), track.Id)]);
        using var bus = new CommandBus(new ShowController(engine), BusMode.Inline);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([track], [playlist], playlist.Id));
        using var vm = new PlaylistsViewModel(bus, () => [track]);
        bus.Submit(new ClientId("setup"), 2, new Play());

        engine.Raise(new StreamEvent(new StreamHandle(1), StreamEventKind.Faulted, StreamEndReason.Faulted, "Undecodable"));

        var entry = vm.Playlists[0].Entries[0];
        Assert.Contains("декодировать", vm.DescribeFault(entry));
        Assert.False(vm.IsTrackMissing(entry));
    }

    [Fact]
    public void MissingCause_BeatsPresentFileGuess()
    {
        var path = Path.GetTempFileName();
        try
        {
            var track = new Track(TrackId.New(), path, "present", TimeSpan.FromMinutes(3), new TrackDefaults());
            var engine = new StubEngine();
            var playlist = new Playlist(PlaylistId.New(), "Main", [new PlaylistEntry(EntryId.New(), track.Id)]);
            using var bus = new CommandBus(new ShowController(engine), BusMode.Inline);
            bus.Submit(new ClientId("setup"), 1, new LoadShow([track], [playlist], playlist.Id));
            using var vm = new PlaylistsViewModel(bus, () => [track]);
            bus.Submit(new ClientId("setup"), 2, new Play());

            engine.Raise(new StreamEvent(new StreamHandle(1), StreamEventKind.Faulted, StreamEndReason.Faulted, "Missing"));

            var entry = vm.Playlists[0].Entries[0];
            Assert.Contains("не найден", vm.DescribeFault(entry));
            Assert.True(vm.IsTrackMissing(entry));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
