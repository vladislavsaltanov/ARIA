namespace Aria.App.Tests;

using System.Collections.Immutable;
using Aria.App.ViewModels;
using Aria.Core.Commands;
using Aria.Core.Model;
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
    public void MoveEntryUp_DisabledAtTop()
    {
        var (bus, playlist, entry1) = Setup();
        using var vm = new PlaylistsViewModel(bus, () => [TestTrack]);

        Assert.False(vm.MoveEntryUpCommand.CanExecute(null));
        Assert.True(vm.MoveEntryDownCommand.CanExecute(null));

        vm.SelectedEntry = vm.Playlists[0].Entries[0];
        vm.MoveEntryUpCommand.Execute(null);
        Assert.Equal(playlist.Entries[0].Id, vm.Playlists[0].Entries[0].Id);
    }

    [Fact]
    public void MoveEntryDown_Reorders()
    {
        var (bus, _, entry1) = Setup();
        using var vm = new PlaylistsViewModel(bus, () => [TestTrack]);
        vm.SelectedEntry = vm.Playlists[0].Entries[0];

        vm.MoveEntryDownCommand.Execute(null);

        Assert.Equal(entry1.Id, vm.Playlists[0].Entries[1].Id);
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
    public void SetEntryName_OverridesAndClears()
    {
        var (bus, _, _) = Setup();
        using var vm = new PlaylistsViewModel(bus, () => [TestTrack]);
        vm.SelectedEntry = vm.Playlists[0].Entries[0];

        vm.SetEntryNameCommand.Execute("Финал");

        var entry = vm.Playlists[0].Entries[0];
        Assert.Equal("Финал", entry.DisplayName);
        Assert.True(entry.HasOverrides);

        vm.SetEntryNameCommand.Execute(" ");
        Assert.Equal("test", vm.Playlists[0].Entries[0].DisplayName);
    }

    [Fact]
    public void ClearEntryOverrides_ResetsDisplayName()
    {
        var (bus, _, _) = Setup();
        using var vm = new PlaylistsViewModel(bus, () => [TestTrack]);

        vm.SetEntryNameCommand.Execute("Финал");
        vm.ClearEntryOverridesCommand.Execute(null);

        Assert.Equal("test", vm.Playlists[0].Entries[0].DisplayName);
    }

    [Fact]
    public void Lock_BlocksEditing()
    {
        var (bus, _, _) = Setup();
        using var vm = new PlaylistsViewModel(bus, () => [TestTrack]);

        vm.Locked = true;

        Assert.False(vm.CreatePlaylistCommand.CanExecute(null));
        Assert.False(vm.RemoveEntryCommand.CanExecute(null));
        Assert.False(vm.MoveEntryDownCommand.CanExecute(null));

        vm.Locked = false;

        Assert.True(vm.CreatePlaylistCommand.CanExecute(null));
    }

    [Fact]
    public void UnknownTrack_FallsBackToPathHint()
    {
        var (bus, _, _) = Setup();
        using var vm = new PlaylistsViewModel(bus, null);

        var name = vm.Playlists[0].Entries[0].DisplayName;
        Assert.StartsWith("track", name);
    }
}
