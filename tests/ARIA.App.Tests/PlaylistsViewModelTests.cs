namespace Aria.App.Tests;

using Aria.App.ViewModels;
using Aria.App.Services;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.Runtime;

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
    public void ShowDeltaWithoutPlaylistChanges_KeepsPlaylistVms()
    {
        var (bus, _, _) = Setup();
        using var vm = new PlaylistsViewModel(bus, () => [TestTrack]);
        var before = vm.Playlists[0];

        bus.Submit(new ClientId("test"), 2, new SetLocked(true));

        Assert.Same(before, vm.Playlists[0]);
        Assert.Same(before, vm.SelectedPlaylist);
        Assert.Equal(2, before.Entries.Count);
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
    public void CreatePlaylist_SelectsNewPlaylist()
    {
        var (bus, _, _) = Setup();
        using var vm = new PlaylistsViewModel(bus, () => [TestTrack]);

        vm.CreatePlaylistCommand.Execute(null);

        Assert.Equal("Новый плейлист 2", vm.SelectedPlaylist?.Name);
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

    [Fact]
    public void RowText_FollowsFormatSettings()
    {
        var track = new Track(TrackId.New(), "/audio/rain.flac", "Осенний дождь", TimeSpan.FromSeconds(222), new TrackDefaults());
        var playlist = new Playlist(PlaylistId.New(), "Main", [new PlaylistEntry(EntryId.New(), track.Id)]);
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([track], [playlist], playlist.Id));
        using var vm = new PlaylistsViewModel(bus, () => [track]);

        Assert.Equal("Осенний дождь", vm.Playlists[0].Entries[0].RowText);

        vm.UpdateRowSettings(new AppSettings(true, "{position} {filename}", Smoothing.Default));

        Assert.Equal("01 rain.flac", vm.Playlists[0].Entries[0].RowText);
        Assert.Equal("Осенний дождь", vm.Playlists[0].Entries[0].DisplayName);
        bus.Dispose();
    }

    [Fact]
    public async Task ExportSelectedDocument_RoundTrips_ThroughImport()
    {
        var (bus, _, _) = Setup();
        using var vm = new PlaylistsViewModel(bus, () => [TestTrack]);

        var json = vm.ExportSelectedDocument();
        var document = PlaylistFormat.Import(json);

        Assert.Equal("Main", document.Name);
        Assert.Equal(2, document.Entries.Length);
        Assert.All(document.Entries, e => Assert.Equal("/audio/test.flac", e.File));
        Assert.Equal("заметка", document.Entries[1].Note);
    }

    [Fact]
    public async Task ImportDocumentAsync_CreatesPlaylist_ResolvesByFile()
    {
        var (bus, _, _) = Setup();
        using var vm = new PlaylistsViewModel(bus, () => [TestTrack]);
        var json = PlaylistFormat.Export("Вечер", [
            new PlaylistExportEntry("/audio/test.flac", new PlaylistOverrides("Утро", GainDb: -3)),
            new PlaylistExportEntry("/audio/missing.flac", Transition: new PlaylistFileTransition("crossfade", 4)),
        ]);

        var report = await vm.ImportDocumentAsync(json);

        Assert.Null(report.Error);
        Assert.Equal("Вечер", report.PlaylistName);
        Assert.Equal(1, report.Added);
        Assert.Equal("/audio/missing.flac", Assert.Single(report.MissingFiles));
        Assert.Equal(0, report.PendingTransitions);
        var imported = vm.Playlists.First(p => p.Name == "Вечер");
        Assert.Equal("Утро", imported.Entries[0].DisplayName);
    }

    [Fact]
    public async Task ImportDocumentAsync_CountsPendingTransitions()
    {
        var (bus, _, _) = Setup();
        using var vm = new PlaylistsViewModel(bus, () => [TestTrack]);
        var json = PlaylistFormat.Export("Вечер", [
            new PlaylistExportEntry("/audio/test.flac", Transition: new PlaylistFileTransition("gap", 2)),
        ]);

        var report = await vm.ImportDocumentAsync(json);

        Assert.Null(report.Error);
        Assert.Equal(1, report.Added);
        Assert.Equal(1, report.PendingTransitions);
    }

    [Fact]
    public async Task ImportDocumentAsync_BadJson_ReportsError()
    {
        var (bus, _, _) = Setup();
        using var vm = new PlaylistsViewModel(bus, () => [TestTrack]);

        var report = await vm.ImportDocumentAsync("не json");

        Assert.NotNull(report.Error);
        Assert.Equal(0, report.Added);
        Assert.DoesNotContain(vm.Playlists, p => p.Name == string.Empty);
    }
}
