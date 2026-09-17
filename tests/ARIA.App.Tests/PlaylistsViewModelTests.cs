namespace Aria.App.Tests;

using Aria.App.ViewModels;
using Aria.App.Services;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.Runtime;

public sealed class ProjectsViewModelTests
{
    private static readonly Track TestTrack = new(
        TrackId.New(), "/audio/test.flac", "test", TimeSpan.FromMinutes(3), new TrackDefaults());

    [Fact]
    public void CreateEntryAudioEditor_InheritsTrackAudioByDefault()
    {
        var trackAudio = new TrackAudioSettings(-6, 0.5, AudioEq.Flat);
        var track = new Track(TestTrack.Id, TestTrack.FilePath, TestTrack.DefaultName, TestTrack.Duration, new TrackDefaults(Audio: trackAudio));
        var (bus, _, _) = Setup();
        using var vm = new ProjectsViewModel(bus, () => [track]);

        var editor = vm.CreateEntryAudioEditor(vm.Projects[0].Entries[0]);

        Assert.True(editor.IsEntry);
        Assert.True(editor.InheritTrackSettings);
        Assert.Equal(-6, editor.GainDb);
        Assert.Equal(0.5, editor.Pan);
        Assert.Equal(-16.0, editor.NormalizeTargetLufs);
    }

    [Fact]
    public void CreateEntryAudioEditor_UsesEntryOverride()
    {
        var entryAudio = new TrackAudioSettings(-3, 0, AudioEq.Flat);
        var entry1 = new ProjectEntry(EntryId.New(), TestTrack.Id, new ProjectOverrides(Audio: entryAudio));
        var project = new Project(ProjectId.New(), "Main", [entry1]);
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([TestTrack], [project], project.Id));
        using var vm = new ProjectsViewModel(bus, () => [TestTrack]);

        var editor = vm.CreateEntryAudioEditor(vm.Projects[0].Entries[0]);

        Assert.False(editor.InheritTrackSettings);
        Assert.Equal(-3, editor.GainDb);

        bus.Submit(new ClientId("setup"), 2, new SetGlobalAudio(GlobalAudioSettings.Default with { NormalizeTargetLufs = -23.0 }));
        var follower = vm.CreateEntryAudioEditor(vm.Projects[0].Entries[0]);

        Assert.Equal(-23.0, follower.NormalizeTargetLufs);
    }

    [Fact]
    public void CreateEntryAudioEditor_PrefersControllerAudio()
    {
        var (bus, _, _) = Setup();
        bus.Submit(new ClientId("setup"), 9, new SetGlobalAudio(GlobalAudioSettings.Default with { NormalizeEnabled = true }));
        var measured = new TrackAudioSettings(0, 0, AudioEq.Flat, true, -10.0);
        using var vm = new ProjectsViewModel(bus, () => [TestTrack], trackAudio: _ => measured);

        var editor = vm.CreateEntryAudioEditor(vm.Projects[0].Entries[0]);

        Assert.Equal("Измерено -10.0 LUFS, поправка -6.0 дБ", editor.NormalizeStatus);
    }

    [Fact]
    public void CreateEntryAudioEditor_RefreshPicksUpMeasurement()
    {
        var (bus, _, _) = Setup();
        bus.Submit(new ClientId("setup"), 9, new SetGlobalAudio(GlobalAudioSettings.Default with { NormalizeEnabled = true }));
        var current = new TrackAudioSettings(0, 0, AudioEq.Flat, true, null);
        using var vm = new ProjectsViewModel(bus, () => [TestTrack], trackAudio: _ => current);

        var editor = vm.CreateEntryAudioEditor(vm.Projects[0].Entries[0]);
        Assert.Equal("Включена, измерение не выполнено", editor.NormalizeStatus);

        current = new TrackAudioSettings(0, 0, AudioEq.Flat, true, -10.0);
        editor.RefreshMeasurement();

        Assert.Equal("Измерено -10.0 LUFS, поправка -6.0 дБ", editor.NormalizeStatus);
    }

    [Fact]
    public async Task ImportDocument_NfdReference_MatchesNfcTrack()
    {
        var nfc = "/audio/трек-café-№1.flac";
        var nfd = nfc.Normalize(System.Text.NormalizationForm.FormD);
        Assert.NotEqual(nfc, nfd);
        var track = new Track(TrackId.New(), nfc, "трек-café-№1", TimeSpan.FromMinutes(3), new TrackDefaults());
        var project = new Project(ProjectId.New(), "Main", []);
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([track], [project], project.Id));
        using var vm = new ProjectsViewModel(bus, () => [track]);

        var report = await vm.ImportDocumentAsync(ProjectFormat.Export("Doc", [new ProjectExportEntry(nfd)]));

        Assert.Equal(1, report.Added);
        Assert.Empty(report.MissingFiles);
    }

    private static (CommandBus Bus, Project Project, ProjectEntry Entry1) Setup()
    {
        var entry1 = new ProjectEntry(EntryId.New(), TestTrack.Id);
        var entry2 = new ProjectEntry(EntryId.New(), TestTrack.Id, new ProjectOverrides(Note: "заметка"));
        var project = new Project(ProjectId.New(), "Main", [entry1, entry2]);
        var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([TestTrack], [project], project.Id));
        return (bus, project, entry1);
    }

    [Fact]
    public void ShowDeltaWithoutProjectChanges_KeepsProjectVms()
    {
        var (bus, _, _) = Setup();
        using var vm = new ProjectsViewModel(bus, () => [TestTrack]);
        var before = vm.Projects[0];

        bus.Submit(new ClientId("test"), 2, new SetLocked(true));

        Assert.Same(before, vm.Projects[0]);
        Assert.Same(before, vm.SelectedProject);
        Assert.Equal(2, before.Entries.Count);
    }

    [Fact]
    public void Rebuilds_FromShowDelta()
    {
        var (bus, project, _) = Setup();
        using var vm = new ProjectsViewModel(bus, () => [TestTrack]);

        var projectVm = Assert.Single(vm.Projects);
        Assert.Equal("Main", projectVm.Name);
        Assert.True(projectVm.IsActive);
        Assert.Equal(2, projectVm.Entries.Count);
        Assert.Equal("test", projectVm.Entries[0].DisplayName);
        Assert.Equal("заметка", projectVm.Entries[1].Note);
    }

    [Fact]
    public void Create_Rename_Delete_ReflectInCollections()
    {
        var (bus, _, _) = Setup();
        using var vm = new ProjectsViewModel(bus, () => [TestTrack]);

        vm.CreateProjectCommand.Execute(null);
        Assert.Equal(2, vm.Projects.Count);

        vm.SelectedProject = vm.Projects[1];
        vm.RenameProjectCommand.Execute("Second");
        Assert.Equal("Second", vm.Projects[1].Name);

        vm.DeleteProjectCommand.Execute(null);
        Assert.Single(vm.Projects);
    }

    [Fact]
    public async Task DeleteProject_Denied_KeepsProject()
    {
        var (bus, _, _) = Setup();
        using var vm = new ProjectsViewModel(bus, () => [TestTrack]);
        vm.CreateProjectCommand.Execute(null);
        Assert.Equal(2, vm.Projects.Count);
        vm.SelectedProject = vm.Projects[1];
        vm.ConfirmDeleteProject = _ => Task.FromResult(false);

        vm.DeleteProjectCommand.Execute(null);
        await Task.Delay(50);

        Assert.Equal(2, vm.Projects.Count);
    }

    [Fact]
    public async Task DeleteProject_Confirmed_RemovesProject()
    {
        var (bus, _, _) = Setup();
        using var vm = new ProjectsViewModel(bus, () => [TestTrack]);
        vm.CreateProjectCommand.Execute(null);
        vm.SelectedProject = vm.Projects[1];
        vm.ConfirmDeleteProject = _ => Task.FromResult(true);

        vm.DeleteProjectCommand.Execute(null);
        await Task.Delay(50);

        Assert.Single(vm.Projects);
    }

    [Fact]
    public void Activate_MarksActiveProject()
    {
        var (bus, _, _) = Setup();
        using var vm = new ProjectsViewModel(bus, () => [TestTrack]);
        vm.CreateProjectCommand.Execute(null);
        vm.SelectedProject = vm.Projects[1];

        vm.ActivateProjectCommand.Execute(null);

        Assert.False(vm.Projects[0].IsActive);
        Assert.True(vm.Projects[1].IsActive);
    }

    [Fact]
    public void CreateProject_SelectsNewProject()
    {
        var (bus, _, _) = Setup();
        using var vm = new ProjectsViewModel(bus, () => [TestTrack]);

        vm.CreateProjectCommand.Execute(null);

        Assert.Equal("Новый проект 2", vm.SelectedProject?.Name);
    }

    [Fact]
    public void MoveEntry_Reorders()
    {
        var (bus, _, entry1) = Setup();
        using var vm = new ProjectsViewModel(bus, () => [TestTrack]);

        vm.MoveEntry(entry1.Id, 1);

        Assert.Equal(entry1.Id, vm.Projects[0].Entries[1].Id);
    }

    [Fact]
    public void MoveEntry_OutOfRange_IsIgnored()
    {
        var (bus, _, entry1) = Setup();
        using var vm = new ProjectsViewModel(bus, () => [TestTrack]);

        vm.MoveEntry(entry1.Id, 9);
        vm.MoveEntry(EntryId.New(), 0);

        Assert.Equal(entry1.Id, vm.Projects[0].Entries[0].Id);
    }

    [Fact]
    public void RemoveEntry_UpdatesCollection()
    {
        var (bus, _, _) = Setup();
        using var vm = new ProjectsViewModel(bus, () => [TestTrack]);

        vm.RemoveEntryCommand.Execute(null);

        Assert.Single(vm.Projects[0].Entries);
    }

    [Fact]
    public void RemoveEntryAt_RemovesGivenRow()
    {
        var (bus, _, _) = Setup();
        using var vm = new ProjectsViewModel(bus, () => [TestTrack]);
        var second = vm.Projects[0].Entries[1];

        vm.RemoveEntryAt(second);

        Assert.Single(vm.Projects[0].Entries);
    }

    [Fact]
    public void AddEntryAt_InsertsAtPosition()
    {
        var (bus, _, _) = Setup();
        using var vm = new ProjectsViewModel(bus, () => [TestTrack]);

        vm.AddEntryAt(TestTrack.Id, 0);

        Assert.Equal(3, vm.Projects[0].Entries.Count);
        Assert.Equal(TestTrack.Id, vm.Projects[0].Entries[0].TrackId);
    }

    [Fact]
    public void CenterSearch_FiltersVisibleRows()
    {
        var (bus, _, _) = Setup();
        using var vm = new ProjectsViewModel(bus, () => [TestTrack]);
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
        var entry = new ProjectEntry(EntryId.New(), TestTrack.Id);
        var project = new Project(ProjectId.New(), "Main", [entry]);
        using var bus = new CommandBus(new ShowController(engine), BusMode.Inline);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([TestTrack], [project], project.Id));
        using var vm = new ProjectsViewModel(bus, () => [TestTrack]);
        bus.Submit(new ClientId("setup"), 2, new Play());

        engine.Raise(new StreamEvent(new StreamHandle(1), StreamEventKind.Faulted, StreamEndReason.Faulted));

        Assert.True(vm.Projects[0].Entries[0].IsFaulted);
    }

    [Fact]
    public void UnknownTrack_FallsBackToPathHint()
    {
        var (bus, _, _) = Setup();
        using var vm = new ProjectsViewModel(bus, null);

        var name = vm.Projects[0].Entries[0].DisplayName;
        Assert.StartsWith("track", name);
    }

    [Fact]
    public void SetLinkedTrack_MarksMatchingEntry()
    {
        var (bus, _, entry1) = Setup();
        using var vm = new ProjectsViewModel(bus, () => [TestTrack]);

        vm.SetLinkedTrack(entry1.TrackId);

        Assert.True(vm.Projects[0].Entries[0].IsLinked);
        Assert.True(vm.Projects[0].Entries[1].IsLinked);

        vm.SetLinkedTrack(null);

        Assert.All(vm.Projects[0].Entries, e => Assert.False(e.IsLinked));
    }

    [Fact]
    public void RowText_FollowsFormatSettings()
    {
        var track = new Track(TrackId.New(), "/audio/rain.flac", "Осенний дождь", TimeSpan.FromSeconds(222), new TrackDefaults());
        var project = new Project(ProjectId.New(), "Main", [new ProjectEntry(EntryId.New(), track.Id)]);
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([track], [project], project.Id));
        using var vm = new ProjectsViewModel(bus, () => [track]);

        Assert.Equal("Осенний дождь", vm.Projects[0].Entries[0].RowText);

        vm.UpdateRowSettings(new AppSettings(true, "{position} {filename}", Smoothing.Default));

        Assert.Equal("01 rain.flac", vm.Projects[0].Entries[0].RowText);
        Assert.Equal("Осенний дождь", vm.Projects[0].Entries[0].DisplayName);
        bus.Dispose();
    }

    [Fact]
    public async Task ExportSelectedDocument_RoundTrips_ThroughImport()
    {
        var (bus, _, _) = Setup();
        using var vm = new ProjectsViewModel(bus, () => [TestTrack]);

        var json = vm.ExportSelectedDocument();
        var document = ProjectFormat.Import(json);

        Assert.Equal("Main", document.Name);
        Assert.Equal(2, document.Entries.Length);
        Assert.All(document.Entries, e => Assert.Equal("/audio/test.flac", e.File));
        Assert.Equal("заметка", document.Entries[1].Note);
    }

    [Fact]
    public async Task ImportDocumentAsync_CreatesProject_ResolvesByFile()
    {
        var (bus, _, _) = Setup();
        using var vm = new ProjectsViewModel(bus, () => [TestTrack]);
        var json = ProjectFormat.Export("Вечер", [
            new ProjectExportEntry("/audio/test.flac", new ProjectOverrides("Утро", GainDb: -3)),
            new ProjectExportEntry("/audio/missing.flac", Transition: new ProjectFileTransition("crossfade", 4)),
        ]);

        var report = await vm.ImportDocumentAsync(json);

        Assert.Null(report.Error);
        Assert.Equal("Вечер", report.ProjectName);
        Assert.Equal(1, report.Added);
        Assert.Equal("/audio/missing.flac", Assert.Single(report.MissingFiles));
        Assert.Equal(0, report.PendingTransitions);
        var imported = vm.Projects.First(p => p.Name == "Вечер");
        Assert.Equal("Утро", imported.Entries[0].DisplayName);
    }

    [Fact]
    public async Task ImportDocumentAsync_CountsPendingTransitions()
    {
        var (bus, _, _) = Setup();
        using var vm = new ProjectsViewModel(bus, () => [TestTrack]);
        var json = ProjectFormat.Export("Вечер", [
            new ProjectExportEntry("/audio/test.flac", Transition: new ProjectFileTransition("gap", 2)),
        ]);

        var report = await vm.ImportDocumentAsync(json);

        Assert.Null(report.Error);
        Assert.Equal(1, report.Added);
        Assert.Equal(1, report.PendingTransitions);
    }

    [Fact]
    public async Task FinishExportAsync_RaisesSuccess_AndClearsStickyStatus()
    {
        var (bus, _, _) = Setup();
        using var vm = new ProjectsViewModel(bus, () => [TestTrack]);
        var events = new List<(string File, string Message)>();
        vm.ExportSucceeded += (file, message) => events.Add((file, message));
        using var sink = new MemoryStream();

        await vm.FinishExportAsync(() => Task.FromResult<Stream>(sink), "Main.aria-playlist.json");

        var raised = Assert.Single(events);
        Assert.Equal("Main.aria-playlist.json", raised.File);
        Assert.Contains("2", raised.Message);
        Assert.Equal(string.Empty, vm.ProjectIoStatus);
        Assert.Contains("Main", System.Text.Encoding.UTF8.GetString(sink.ToArray()));
    }

    [Fact]
    public async Task ImportDocumentAsync_BadJson_ReportsError()
    {
        var (bus, _, _) = Setup();
        using var vm = new ProjectsViewModel(bus, () => [TestTrack]);

        var report = await vm.ImportDocumentAsync("не json");

        Assert.NotNull(report.Error);
        Assert.Equal(0, report.Added);
        Assert.DoesNotContain(vm.Projects, p => p.Name == string.Empty);
    }

    [Fact]
    public async Task ImportAudioFilesAsync_RevealsAddedEntry()
    {
        var (bus, _, _) = Setup();
        using var vm = new ProjectsViewModel(
            bus,
            () => [TestTrack],
            audioImport: (_, _) => Task.FromResult(new Aria.App.ImportReport(0, 0, [])));
        ProjectsViewModel.EntryVm? revealed = null;
        vm.RevealRequested += row => revealed = row;

        var added = await vm.ImportAudioFilesAsync([TestTrack.FilePath]);

        Assert.Single(added);
        Assert.NotNull(revealed);
        Assert.Equal(TestTrack.Id, revealed.TrackId);
        Assert.Equal(revealed.Id, vm.SelectedEntry?.Id);
        Assert.Contains(revealed, vm.VisibleEntries);
    }

    [Fact]
    public async Task ImportAudioFilesAsync_ReportsSummaryFromEngineReport()
    {
        var (bus, _, _) = Setup();
        var broken = "/audio/broken.wav";
        using var vm = new ProjectsViewModel(
            bus,
            () => [TestTrack],
            audioImport: (_, _) => Task.FromResult(new Aria.App.ImportReport(2, 1, [broken])));
        string? dialog = null;
        vm.AudioImportIncomplete += message => dialog = message;

        var added = await vm.ImportAudioFilesAsync(["/audio/folder"]);

        Assert.Empty(added);
        Assert.Equal("импортировано: 2, пропущено: 1, ошибок: 1", vm.ProjectIoStatus);
        Assert.NotNull(dialog);
        Assert.Contains(broken, dialog);
        Assert.DoesNotContain("/audio/folder", dialog);
    }

    [Fact]
    public void SetEntryEndAction_SetsAction_AndKeepsNote()
    {
        var (bus, _, _) = Setup();
        using var vm = new ProjectsViewModel(bus, () => [TestTrack]);
        var row = vm.Projects[0].Entries[1];

        vm.SetEntryEndAction(row, EndAction.Pause);

        var updated = vm.Projects[0].Entries[1];
        Assert.Equal(EndAction.Pause, updated.Overrides?.EndAction);
        Assert.Equal("заметка", updated.Note);
    }

    [Fact]
    public void DescribeFault_LostFile_NamesMissingPath()
    {
        var lost = new Track(TrackId.New(), "/nonexistent/missing.flac", "missing", TimeSpan.FromMinutes(2), new TrackDefaults());
        var entry = new ProjectEntry(EntryId.New(), lost.Id);
        var project = new Project(ProjectId.New(), "Main", [entry]);
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([lost], [project], project.Id));
        using var vm = new ProjectsViewModel(bus, () => [lost]);
        var row = vm.Projects[0].Entries[0];

        Assert.True(vm.IsTrackMissing(row));
        Assert.Contains("/nonexistent/missing.flac", vm.DescribeFault(row));
    }

    [Fact]
    public void EndActionBadge_ReflectsOverride()
    {
        var (bus, _, _) = Setup();
        using var vm = new ProjectsViewModel(bus, () => [TestTrack]);
        var plain = vm.Projects[0].Entries[0];
        Assert.False(plain.HasEndActionOverride);
        Assert.Equal(string.Empty, plain.EndActionBadge);

        vm.SetEntryEndAction(plain, EndAction.Pause);
        var updated = vm.Projects[0].Entries[0];
        Assert.True(updated.HasEndActionOverride);
        Assert.Equal("Пауза", updated.EndActionBadge);
        Assert.Contains("Пауза", updated.EndActionTip);
    }

    [Fact]
    public void CenterSearch_ShowsFilteredCount()
    {
        var (bus, _, _) = Setup();
        using var vm = new ProjectsViewModel(bus, () => [TestTrack]);
        Assert.Equal("2 трека · 6:00", vm.EntryCountText);

        vm.CenterSearchText = "no-such-name";
        Assert.Equal("0 из 2 · 6:00", vm.EntryCountText);
    }

    [Fact]
    public void ClearSearchCommand_ClearsText()
    {
        var (bus, _, _) = Setup();
        using var vm = new ProjectsViewModel(bus, () => [TestTrack]);
        vm.CenterSearchText = "test";
        vm.ClearSearchCommand.Execute(null);
        Assert.Equal(string.Empty, vm.CenterSearchText);
        Assert.Equal(2, vm.VisibleEntries.Count);
    }

    [Fact]
    public void DescribeFault_PresentFile_ReportsDecodeFailure()
    {
        var path = Path.GetTempFileName();
        try
        {
            var broken = new Track(TrackId.New(), path, "broken", TimeSpan.FromMinutes(2), new TrackDefaults());
            var entry = new ProjectEntry(EntryId.New(), broken.Id);
            var project = new Project(ProjectId.New(), "Main", [entry]);
            using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
            bus.Submit(new ClientId("setup"), 1, new LoadShow([broken], [project], project.Id));
            using var vm = new ProjectsViewModel(bus, () => [broken]);
            var row = vm.Projects[0].Entries[0];

            Assert.False(vm.IsTrackMissing(row));
            Assert.Contains("декодировать", vm.DescribeFault(row));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RelinkEntryAsync_UsesDelegate()
    {
        var (bus, _, _) = Setup();
        using var vm = new ProjectsViewModel(bus, () => [TestTrack]);
        var row = vm.Projects[0].Entries[0];
        TrackId? capturedId = null;
        string? capturedPath = null;
        vm.TrackRelink = (id, path) =>
        {
            capturedId = id;
            capturedPath = path;
            return Task.FromResult(true);
        };

        var ok = await vm.RelinkEntryAsync(row, "/audio/found.flac");

        Assert.True(ok);
        Assert.Equal(TestTrack.Id, capturedId);
        Assert.Equal("/audio/found.flac", capturedPath);
    }

    [Fact]
    public async Task RelinkEntryAsync_NullDelegate_ReturnsFalse()
    {
        var (bus, _, _) = Setup();
        using var vm = new ProjectsViewModel(bus, () => [TestTrack]);
        var row = vm.Projects[0].Entries[0];

        var ok = await vm.RelinkEntryAsync(row, "/audio/found.flac");

        Assert.False(ok);
    }

    [Fact]
    public void MergeTracksReplace_ClearsRowFault()
    {
        var engine = new StubEngine();
        var entry = new ProjectEntry(EntryId.New(), TestTrack.Id);
        var project = new Project(ProjectId.New(), "Main", [entry]);
        using var bus = new CommandBus(new ShowController(engine), BusMode.Inline);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([TestTrack], [project], project.Id));
        using var vm = new ProjectsViewModel(bus, () => [TestTrack]);
        bus.Submit(new ClientId("setup"), 2, new Play());
        engine.Raise(new StreamEvent(new StreamHandle(1), StreamEventKind.Faulted, StreamEndReason.Faulted));
        Assert.True(vm.Projects[0].Entries[0].IsFaulted);

        bus.Submit(new ClientId("setup"), 3, new MergeTracks([TestTrack with { FilePath = "/audio/test-relinked.flac" }]));

        Assert.False(vm.Projects[0].Entries[0].IsFaulted);
    }

    [Fact]
    public void Constructor_PicksUpPreexistingFaults()
    {
        var engine = new StubEngine();
        var entry = new ProjectEntry(EntryId.New(), TestTrack.Id);
        var project = new Project(ProjectId.New(), "Main", [entry]);
        using var bus = new CommandBus(new ShowController(engine), BusMode.Inline);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([TestTrack], [project], project.Id));
        bus.Submit(new ClientId("setup"), 2, new MarkMissing([TestTrack.Id]));

        using var vm = new ProjectsViewModel(bus, () => [TestTrack]);

        Assert.True(vm.Projects[0].Entries[0].IsFaulted);
    }

    [Fact]
    public async Task ImportDocumentAsync_MissingFiles_RaisesMissingEvent()
    {
        var (bus, _, _) = Setup();
        using var vm = new ProjectsViewModel(bus, () => [TestTrack]);
        var raised = new List<ProjectsViewModel.ProjectImportReport>();
        vm.ProjectImportMissing += raised.Add;
        var json = ProjectFormat.Export("Вечер", [
            new ProjectExportEntry("/audio/test.flac", Transition: new ProjectFileTransition("gap", 2)),
            new ProjectExportEntry("/audio/missing.flac", Transition: new ProjectFileTransition("gap", 2)),
        ]);

        var report = await vm.ImportDocumentAsync(json);

        var fired = Assert.Single(raised);
        Assert.Same(report, fired);
        Assert.Equal("/audio/missing.flac", Assert.Single(fired.MissingFiles));
    }

    [Fact]
    public async Task ImportDocumentAsync_MissingFiles_SelectsImportedProject()
    {
        var (bus, _, _) = Setup();
        using var vm = new ProjectsViewModel(bus, () => [TestTrack]);
        var json = ProjectFormat.Export("Вечер", [
            new ProjectExportEntry("/audio/test.flac", Transition: new ProjectFileTransition("gap", 2)),
            new ProjectExportEntry("/audio/missing.flac", Transition: new ProjectFileTransition("gap", 2)),
        ]);

        await vm.ImportDocumentAsync(json);

        Assert.Equal("Вечер", vm.SelectedProject?.Name);
    }

    [Fact]
    public async Task RelinkEntryAsync_Success_SetsReplacedStatus()
    {
        var (bus, _, _) = Setup();
        using var vm = new ProjectsViewModel(bus, () => [TestTrack]);
        var row = vm.Projects[0].Entries[0];
        vm.TrackRelink = (_, _) => Task.FromResult(true);

        var ok = await vm.RelinkEntryAsync(row, "/audio/found.flac");

        Assert.True(ok);
        Assert.Contains("замен", vm.ProjectIoStatus);
    }

    [Fact]
    public async Task RelinkEntryAsync_ClearsStatusAfterTtl_WithPostedSyncContext()
    {
        var pump = new PumpContext();
        var (bus, _, _) = Setup();
        using var vm = new ProjectsViewModel(bus, () => [TestTrack], sync: pump, transientStatusTtl: TimeSpan.FromMilliseconds(50));
        var row = vm.Projects[0].Entries[0];
        vm.TrackRelink = (_, _) => Task.FromResult(true);

        await vm.RelinkEntryAsync(row, "/audio/found.flac");

        Assert.Contains("замен", vm.ProjectIoStatus);
        await Task.Delay(500);
        pump.PumpAll();

        Assert.Equal(string.Empty, vm.ProjectIoStatus);
    }

    [Fact]
    public async Task ImportDocumentAsync_SuccessSummary_ClearsAfterTtl()
    {
        var (bus, _, _) = Setup();
        using var vm = new ProjectsViewModel(bus, () => [TestTrack], transientStatusTtl: TimeSpan.FromMilliseconds(50));
        var json = ProjectFormat.Export("Вечер", [
            new ProjectExportEntry("/audio/test.flac", Transition: new ProjectFileTransition("gap", 2)),
        ]);

        await vm.ImportDocumentAsync(json);

        Assert.Contains("импортировано", vm.ProjectIoStatus);
        await Task.Delay(500);

        Assert.Equal(string.Empty, vm.ProjectIoStatus);
    }

    [Fact]
    public async Task SilentRepair_AfterImportSummary_LeavesNoStickyStatus()
    {
        var (bus, _, _) = Setup();
        var found = new Track(TrackId.New(), "/audio/found.wav", "found", TimeSpan.FromMinutes(2), new TrackDefaults());
        using var vm = new ProjectsViewModel(bus, () => [TestTrack, found],
            audioImport: (_, _) => Task.FromResult(new Aria.App.ImportReport(1, 0, [])),
            transientStatusTtl: TimeSpan.FromMilliseconds(50));
        var json = ProjectFormat.Export("Вечер", [
            new ProjectExportEntry("/audio/test.flac", Transition: new ProjectFileTransition("gap", 2)),
            new ProjectExportEntry("/audio/missing.flac", Transition: new ProjectFileTransition("gap", 2)),
        ]);

        await vm.ImportDocumentAsync(json);
        await vm.ImportAudioFilesAsync([found.FilePath], silent: true);
        await Task.Delay(500);

        Assert.Equal(string.Empty, vm.ProjectIoStatus);
    }

    private sealed class PumpContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));

        public void PumpAll()
        {
            for (var guard = 0; guard < 1000 && _queue.Count > 0; guard++)
            {
                var (callback, state) = _queue.Dequeue();
                callback(state);
            }
        }
    }
}
