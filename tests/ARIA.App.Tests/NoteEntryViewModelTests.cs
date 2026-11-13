namespace Aria.App.Tests;

using Aria.App.ViewModels;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;

public sealed class NoteEntryViewModelTests
{
    private static readonly Track TestTrack = new(
        TrackId.New(), "/audio/test.flac", "test", TimeSpan.FromMinutes(3), new TrackDefaults());

    private static (CommandBus Bus, Project Project, ProjectsViewModel Vm) Setup(params ProjectEntry[] entries)
    {
        var project = new Project(ProjectId.New(), "Main", [.. entries]);
        var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([TestTrack], [project], project.Id));
        var vm = new ProjectsViewModel(bus, () => [TestTrack]);
        return (bus, project, vm);
    }

    private static ProjectEntry TrackEntry() => new(EntryId.New(), new TrackContent(TestTrack.Id));

    private static ProjectEntry NoteEntry(string text) => new(EntryId.New(), new NoteContent(text));

    [Fact]
    public void NoteRow_RendersTextWithoutAudioFields()
    {
        var (_, _, vm) = Setup(TrackEntry(), NoteEntry("первая строка\nвторая строка"));
        using var _ = vm;

        var row = vm.Projects[0].Entries[1];

        Assert.True(row.IsNote);
        Assert.Equal("первая строка вторая строка", row.RowText);
        Assert.Equal("первая строка\nвторая строка", row.Note);
        Assert.Equal(TimeSpan.Zero, row.Duration);
        Assert.Null(row.Waveform);
        Assert.False(row.IsFaulted);
        Assert.False(row.IsLinked);
        Assert.Equal("02", row.Position);
    }

    [Fact]
    public void ToggleNoteExpanded_FlipsExpansionState()
    {
        var (_, _, vm) = Setup(TrackEntry(), NoteEntry("ремарка"));
        using var _ = vm;
        Assert.False(vm.Projects[0].Entries[1].IsExpanded);

        vm.ToggleNoteExpanded(vm.Projects[0].Entries[1]);
        Assert.True(vm.Projects[0].Entries[1].IsExpanded);

        vm.ToggleNoteExpanded(vm.Projects[0].Entries[1]);
        Assert.False(vm.Projects[0].Entries[1].IsExpanded);
    }

    [Fact]
    public void AddNote_InsertsAfterSelectedEntry_AndSelectsIt()
    {
        var (bus, _, vm) = Setup(TrackEntry());
        using var _ = vm;
        vm.SelectedEntry = vm.Projects[0].Entries[0];

        vm.AddNote(vm.Projects[0]);

        var entries = bus.Snapshot().Show.Projects[0].Entries;
        Assert.Equal(2, entries.Length);
        Assert.IsType<NoteContent>(entries[1].Content);
        Assert.True(vm.SelectedEntry!.IsNote);
    }

    [Fact]
    public void CountText_ListsTracksAndNotes()
    {
        var (_, _, vm) = Setup(TrackEntry(), NoteEntry("один"), NoteEntry("два"));
        using var _ = vm;

        Assert.Contains("1 трек", vm.EntryCountText);
        Assert.Contains("2 заметки", vm.EntryCountText);
    }

    [Fact]
    public void Search_MatchesNoteText()
    {
        var (_, _, vm) = Setup(TrackEntry(), NoteEntry("после фанфар — свет"));
        using var _ = vm;

        vm.CenterSearchText = "фанфар";

        var row = Assert.Single(vm.VisibleEntries);
        Assert.True(row.IsNote);
    }

    [Fact]
    public void SetNoteText_UpdatesShow()
    {
        var (bus, _, vm) = Setup(TrackEntry(), NoteEntry("старый"));
        using var _ = vm;

        vm.SetNoteText(vm.Projects[0].Entries[1], "новый");

        Assert.Equal("новый", Assert.IsType<NoteContent>(bus.Snapshot().Show.Projects[0].Entries[1].Content).Text);
    }

    [Fact]
    public void TrackCommands_AreIgnoredForNotes()
    {
        var (bus, _, vm) = Setup(TrackEntry(), NoteEntry("ремарка"));
        using var _ = vm;
        var note = vm.Projects[0].Entries[1];

        vm.EnqueueEntry(note);
        vm.PlayEntry(note);
        vm.SetEntryEndAction(note, EndAction.Pause);

        Assert.Empty(bus.Snapshot().Queue.Items);
        Assert.IsType<NoteContent>(bus.Snapshot().Show.Projects[0].Entries[1].Content);
    }

    [Fact]
    public void ExportDocument_KeepsNoteTextInProjectFile()
    {
        var (_, _, vm) = Setup(TrackEntry(), NoteEntry("кусок сценария"));
        using var _ = vm;

        var json = vm.ExportSelectedDocument();
        var document = ProjectFormat.Import(json);

        var note = Assert.Single(document.Entries, e => e.Kind == "note");
        Assert.Equal("кусок сценария", note.Text);
        Assert.Null(note.File);
    }
}
