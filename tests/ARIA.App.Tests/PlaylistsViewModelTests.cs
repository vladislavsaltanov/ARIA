namespace Aria.App.Tests;

using System.Collections.Immutable;
using Aria.App.ViewModels;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.Runtime;
using Aria.Core.State;

public sealed class PlaylistsViewModelTests
{
    private static readonly Track TestTrack = new(
        TrackId.New(), "/audio/test.flac", "test", TimeSpan.FromMinutes(3), new TrackDefaults());

    private static (CommandBus Bus, Playlist Playlist, PlaylistEntry Entry1) Setup()
    {
        var entry1 = new PlaylistEntry(EntryId.New(), TestTrack.Id);
        var entry2 = new PlaylistEntry(EntryId.New(), TestTrack.Id, new PlaylistOverrides(Note: "заметка"));
        var playlist = new Playlist(PlaylistId.New(), "Main", [entry1, entry2]);
        var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([TestTrack], [playlist], playlist.Id));
        return (bus, playlist, entry1);
    }

    [Fact]
    public void Rebuilds_FromShowDelta()
    {
        var (bus, playlist, _) = Setup();
        using var vm = new PlaylistsViewModel(bus, () => [TestTrack]);

        var playlistVm = Assert.Single(vm.Playlists);
        Assert.Equal("Main", playlistVm.Name);
        Assert.True(playlistVm.IsActive);
        Assert.Equal(2, playlistVm.Entries.Count);
        Assert.Equal("test", playlistVm.Entries[0].DisplayName);
        Assert.Equal("заметка", playlistVm.Entries[1].Note);
    }

    [Fact]
    public void Create_Rename_Delete_ReflectInCollections()
    {
        var (bus, _, _) = Setup();
        using var vm = new PlaylistsViewModel(bus, () => [TestTrack]);

        vm.CreatePlaylistCommand.Execute(null);
        Assert.Equal(2, vm.Playlists.Count);

        vm.SelectedPlaylist = vm.Playlists[1];
        vm.RenamePlaylistCommand.Execute("Second");
        Assert.Equal("Second", vm.Playlists[1].Name);

        vm.DeletePlaylistCommand.Execute(null);
        Assert.Single(vm.Playlists);
    }

    [Fact]
    public void Activate_MarksActivePlaylist()
    {
        var (bus, _, _) = Setup();
        using var vm = new PlaylistsViewModel(bus, () => [TestTrack]);
        vm.CreatePlaylistCommand.Execute(null);
        vm.SelectedPlaylist = vm.Playlists[1];

        vm.ActivatePlaylistCommand.Execute(null);

        Assert.False(vm.Playlists[0].IsActive);
        Assert.True(vm.Playlists[1].IsActive);
    }

    [Fact]
    public void MoveEntry_Reorders()
    {
        var (bus, _, entry1) = Setup();
        using var vm = new PlaylistsViewModel(bus, () => [TestTrack]);

        vm.MoveEntry(entry1.Id, 1);

        Assert.Equal(entry1.Id, vm.Playlists[0].Entries[1].Id);
    }

    [Fact]
    public void MoveEntry_OutOfRange_IsIgnored()
    {
        var (bus, _, entry1) = Setup();
        using var vm = new PlaylistsViewModel(bus, () => [TestTrack]);

        vm.MoveEntry(entry1.Id, 9);
        vm.MoveEntry(EntryId.New(), 0);

        Assert.Equal(entry1.Id, vm.Playlists[0].Entries[0].Id);
    }

    [Fact]
    public void RemoveEntry_UpdatesCollection()
    {
        var (bus, _, _) = Setup();
        using var vm = new PlaylistsViewModel(bus, () => [TestTrack]);

        vm.RemoveEntryCommand.Execute(null);

        Assert.Single(vm.Playlists[0].Entries);
    }

    [Fact]
    public void RemoveEntryAt_RemovesGivenRow()
    {
        var (bus, _, _) = Setup();
        using var vm = new PlaylistsViewModel(bus, () => [TestTrack]);
        var second = vm.Playlists[0].Entries[1];

        vm.RemoveEntryAt(second);

        Assert.Single(vm.Playlists[0].Entries);
    }

    [Fact]
    public void AddEntryAt_InsertsAtPosition()
    {
        var (bus, _, _) = Setup();
        using var vm = new PlaylistsViewModel(bus, () => [TestTrack]);

        vm.AddEntryAt(TestTrack.Id, 0);

        Assert.Equal(3, vm.Playlists[0].Entries.Count);
        Assert.Equal(TestTrack.Id, vm.Playlists[0].Entries[0].TrackId);
    }

    [Fact]
    public void CenterSearch_FiltersVisibleRows()
    {
        var (bus, _, _) = Setup();
        using var vm = new PlaylistsViewModel(bus, () => [TestTrack]);
        Assert.Equal(2, vm.VisibleEntries.Count);

        vm.CenterSearchText = "no-such-name";

        Assert.Empty(vm.VisibleEntries);

        vm.CenterSearchText = string.Empty;

        Assert.Equal(2, vm.VisibleEntries.Count);
    }

    [Fact]
    public void EngineFault_MarksRowFaulted()
    {
        var engine = new StubEngine();
        var entry = new PlaylistEntry(EntryId.New(), TestTrack.Id);
        var playlist = new Playlist(PlaylistId.New(), "Main", [entry]);
        using var bus = new CommandBus(new ShowController(engine), BusMode.Inline);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([TestTrack], [playlist], playlist.Id));
        using var vm = new PlaylistsViewModel(bus, () => [TestTrack]);
        bus.Submit(new ClientId("setup"), 2, new Play());

        engine.Raise(new StreamEvent(new StreamHandle(1), StreamEventKind.Faulted, StreamEndReason.Faulted));

        Assert.True(vm.Playlists[0].Entries[0].IsFaulted);
    }

    [Fact]
    public void UnknownTrack_FallsBackToPathHint()
    {
        var (bus, _, _) = Setup();
        using var vm = new PlaylistsViewModel(bus, null);

        var name = vm.Playlists[0].Entries[0].DisplayName;
        Assert.StartsWith("track", name);
    }

    [Fact]
    public void SetLinkedTrack_MarksMatchingEntry()
    {
        var (bus, _, entry1) = Setup();
        using var vm = new PlaylistsViewModel(bus, () => [TestTrack]);

        vm.SetLinkedTrack(entry1.TrackId);

        Assert.True(vm.Playlists[0].Entries[0].IsLinked);
        Assert.True(vm.Playlists[0].Entries[1].IsLinked);

        vm.SetLinkedTrack(null);

        Assert.All(vm.Playlists[0].Entries, e => Assert.False(e.IsLinked));
    }
}
