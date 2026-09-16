namespace Aria.App.Tests;

using Aria.App.ViewModels;
using Aria.App.Views;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

[Collection("headless")]
public sealed class EntryEndActionMenuTests : IDisposable
{
    private static readonly Track TestTrack = new(
        TrackId.New(), "/audio/menu.flac", "menu", TimeSpan.FromMinutes(3), new TrackDefaults());

    private readonly HeadlessUnitTestSession _session = HeadlessUnitTestSession.StartNew(typeof(App));

    public void Dispose() => _session.Dispose();

    [Fact]
    public async Task EntryEndActionMenu_PauseClick_SetsOverride()
    {
        await _session.Dispatch(() =>
        {
            using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
            var entry = new ProjectEntry(EntryId.New(), TestTrack.Id);
            var project = new Project(ProjectId.New(), "Main", [entry]);
            bus.Submit(new ClientId("setup"), 1, new LoadShow([TestTrack], [project], project.Id));
            using var vm = new ProjectsViewModel(bus, () => [TestTrack]);
            var center = new ProjectCenter { DataContext = vm };
            var window = new Window { Content = center, Width = 1200, Height = 800 };
            window.Show();
            window.UpdateLayout();
            center.EntryListBox.UpdateLayout();

            var row = center.EntryListBox.ContainerFromIndex(0) as Control;
            Assert.NotNull(row);
            var cell = row.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.ContextMenu is not null);
            Assert.NotNull(cell);
            var menu = cell.ContextMenu;
            Assert.NotNull(menu);
            menu.Open(cell);
            var sub = menu.Items.OfType<MenuItem>().FirstOrDefault(m => (string?)m.Header == "Действие в конце");
            Assert.NotNull(sub);
            sub.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent));
            var inherit = sub.Items.OfType<MenuItem>().FirstOrDefault(m => (string?)m.Tag == "inherit");
            var pause = sub.Items.OfType<MenuItem>().FirstOrDefault(m => (string?)m.Tag == "pause");
            Assert.NotNull(inherit);
            Assert.NotNull(pause);
            Assert.True(inherit.IsChecked);
            Assert.False(pause.IsChecked);

            pause.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            var updated = vm.Projects[0].Entries[0];
            Assert.Equal(EndAction.Pause, updated.Overrides?.EndAction);
            Assert.Equal(EndAction.Pause, updated.EffectiveEndAction);
            window.Close();
            return 0;
        }, CancellationToken.None);
    }
}
