namespace Aria.App.Tests;

using Aria.App.ViewModels;
using Aria.App.Views;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;

[Collection("headless")]
public sealed class NoteEntryHeadlessTests : IDisposable
{
    private static readonly Track TestTrack = new(
        TrackId.New(), "/audio/note.flac", "note", TimeSpan.FromMinutes(3), new TrackDefaults());

    private readonly HeadlessUnitTestSession _session = HeadlessUnitTestSession.StartNew(typeof(App));

    public void Dispose() => _session.Dispose();

    [Fact]
    public async Task NoteRow_TogglesBodyAndEditor()
    {
        await _session.Dispatch(() =>
        {
            using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
            var note = new ProjectEntry(EntryId.New(), new NoteContent("первая строка\nвторая строка"));
            var project = new Project(ProjectId.New(), "Main", [new ProjectEntry(EntryId.New(), new TrackContent(TestTrack.Id)), note]);
            bus.Submit(new ClientId("setup"), 1, new LoadShow([TestTrack], [project], project.Id));
            using var vm = new ProjectsViewModel(bus, () => [TestTrack]);
            var center = new ProjectCenter { DataContext = vm };
            var window = new Window { Content = center, Width = 1200, Height = 800 };
            window.Show();
            window.UpdateLayout();
            center.EntryListBox.UpdateLayout();

            Assert.False(Find<Border>(center, 1, "NoteBody")?.IsVisible ?? false);
            Assert.True(Find<TextBox>(center, 1, "NoteEditor") is not { IsVisible: true });

            vm.ToggleNoteExpanded(vm.Projects[0].Entries[1]);
            window.UpdateLayout();
            center.EntryListBox.UpdateLayout();
            var body = Find<Border>(center, 1, "NoteBody");
            Assert.NotNull(body);
            Assert.True(body.IsVisible);
            var text = body.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault();
            Assert.NotNull(text);
            Assert.Equal("первая строка\nвторая строка", text.Text);

            vm.BeginNoteEdit(vm.Projects[0].Entries[1]);
            window.UpdateLayout();
            center.EntryListBox.UpdateLayout();
            var editor = Find<TextBox>(center, 1, "NoteEditor");
            Assert.NotNull(editor);
            Assert.True(editor.IsVisible);
            Assert.Equal("первая строка\nвторая строка", editor.Text);

            editor.Text = "новый текст";
            Assert.Equal("новый текст", vm.NoteDraft);
            vm.CommitNoteEdit();
            window.UpdateLayout();
            center.EntryListBox.UpdateLayout();

            var saved = Assert.IsType<NoteContent>(bus.Snapshot().Show.Projects[0].Entries[1].Content);
            Assert.Equal("новый текст", saved.Text);
            Assert.False(vm.Projects[0].Entries[1].IsEditing);
            var item = center.EntryListBox.ContainerFromIndex(1) as ListBoxItem;
            Assert.NotNull(item);
            Assert.Null(item.FocusAdorner);
            window.Close();
            return 0;
        }, CancellationToken.None);
    }

    private static T? Find<T>(ProjectCenter center, int index, string name) where T : Control =>
        (center.EntryListBox.ContainerFromIndex(index) as Control)?
            .GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(control => control.Name == name);
}
