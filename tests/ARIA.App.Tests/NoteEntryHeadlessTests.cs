namespace Aria.App.Tests;

using Aria.App.ViewModels;
using Aria.App.Views;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Media;
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

    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    [Fact]
    public async Task Rows_PaintOnlyThemselves_NotTheContainer()
    {
        await _session.Dispatch(async () =>
        {
            using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
            var project = new Project(ProjectId.New(), "Main", [new ProjectEntry(EntryId.New(), new TrackContent(TestTrack.Id)), new ProjectEntry(EntryId.New(), new NoteContent("ремарка"))]);
            bus.Submit(new ClientId("setup"), 1, new LoadShow([TestTrack], [project], project.Id));
            using var vm = new ProjectsViewModel(bus, () => [TestTrack]);
            var center = new ProjectCenter { DataContext = vm };
            var window = new Window { Content = center, Width = 1200, Height = 800 };
            window.Show();
            window.UpdateLayout();
            center.EntryListBox.UpdateLayout();

            var item = Assert.IsType<ListBoxItem>(center.EntryListBox.ContainerFromIndex(0));
            var presenter = Assert.Single(item.GetVisualDescendants().OfType<ContentPresenter>(), p => p.Name == "PART_ContentPresenter");
            Assert.Equal(Colors.Transparent, ColorOf(presenter.Background));

            center.EntryListBox.SelectedItem = null;
            var point = item.TranslatePoint(new Point(60, 22), window);
            Assert.NotNull(point);
            window.MouseMove(point.Value);
            await Task.Delay(300);
            window.UpdateLayout();

            var row = item.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("plRow"));
            Assert.Equal(Color.Parse("#FF1A1A1A"), ColorOf(row.Background));
            Assert.Equal(Colors.Transparent, ColorOf(presenter.Background));

            center.EntryListBox.SelectedItem = vm.Projects[0].Entries[0];
            await Task.Delay(300);
            window.UpdateLayout();

            Assert.Contains("selected", row.Classes);
            Assert.Equal(Color.Parse("#FF242424"), ColorOf(row.Background));
            Assert.Equal(Colors.Transparent, ColorOf(presenter.Background));

            center.EntryListBox.SelectedItem = null;
            await Task.Delay(300);
            window.UpdateLayout();

            Assert.DoesNotContain("selected", row.Classes);
            Assert.Equal(Color.Parse("#FF1A1A1A"), ColorOf(row.Background));

            window.MouseMove(new Point(2, 2));
            center.EntryListBox.SelectedItem = vm.Projects[0].Entries[0];
            await Task.Delay(300);
            window.UpdateLayout();

            Assert.Contains("selected", row.Classes);
            Assert.Equal(Color.Parse("#FF1F1F1F"), ColorOf(row.Background));
            window.Close();
            return 0;
        }, CancellationToken.None);
    }
}
