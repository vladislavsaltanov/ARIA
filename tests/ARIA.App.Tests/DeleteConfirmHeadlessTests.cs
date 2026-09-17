namespace Aria.App.Tests;

using Aria.App.ViewModels;
using Aria.App.Views;
using Aria.Core.Runtime;
using Aria.Core.Model;
using Avalonia.Controls;
using Avalonia.Headless;

[Collection("headless")]
public sealed class DeleteConfirmHeadlessTests : IDisposable
{
    private static readonly Track TestTrack = new(
        TrackId.New(), "/audio/confirm.flac", "confirm", TimeSpan.FromMinutes(3), new TrackDefaults());

    private readonly HeadlessUnitTestSession _session = HeadlessUnitTestSession.StartNew(typeof(App));

    public void Dispose() => _session.Dispose();

    [Fact]
    public async Task ProjectCenter_BindsDeleteConfirm()
    {
        await _session.Dispatch(() =>
        {
            using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
            using var vm = new ProjectsViewModel(bus, () => [TestTrack]);
            var center = new ProjectCenter { DataContext = vm };
            var window = new Window { Content = center, Width = 1200, Height = 800 };
            window.Show();

            Assert.NotNull(vm.ConfirmDeleteProject);

            center.DataContext = null;

            Assert.Null(vm.ConfirmDeleteProject);

            window.Close();
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ProjectCenter_CloseButton_SaysCloseNotDelete()
    {
        await _session.Dispatch(() =>
        {
            using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
            using var vm = new ProjectsViewModel(bus, () => [TestTrack]);
            var center = new ProjectCenter { DataContext = vm };
            var window = new Window { Content = center, Width = 1200, Height = 800 };
            window.Show();

            var close = center.FindControl<Button>("CloseProjectButton");
            Assert.NotNull(close);
            Assert.Equal("Закрыть проект", ToolTip.GetTip(close));

            window.Close();
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RailProjects_DeleteMenu_SaysCloseNotDelete()
    {
        await _session.Dispatch(() =>
        {
            using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
            using var vm = new ProjectsViewModel(bus, () => [TestTrack]);
            var rail = new RailProjects { DataContext = vm };
            var window = new Window { Content = rail, Width = 300, Height = 600 };
            window.Show();
            window.UpdateLayout();

            var list = rail.FindControl<ListBox>("ProjectNames");
            Assert.NotNull(list);
            list.UpdateLayout();
            var row = list.ContainerFromIndex(0) as Control;
            Assert.NotNull(row);
            var menu = row.ContextMenu;
            Assert.NotNull(menu);
            var item = menu.Items.OfType<MenuItem>().FirstOrDefault();
            Assert.NotNull(item);
            Assert.Equal("Закрыть проект", item.Header);

            window.Close();
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ScriptPanel_DeleteButton_SaysDestroy()
    {
        await _session.Dispatch(() =>
        {
            using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
            using var vm = new ScriptPanelViewModel(bus, () => [TestTrack]);
            var panel = new ScriptPanel { DataContext = vm };
            var window = new Window { Content = panel, Width = 500, Height = 700 };
            window.Show();

            var delete = panel.FindControl<Button>("DeleteScriptButton");
            Assert.NotNull(delete);
            Assert.Equal("Удалить сценарий навсегда", ToolTip.GetTip(delete));

            window.Close();
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ScriptPanel_BindsDeleteConfirm()
    {
        await _session.Dispatch(() =>
        {
            using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
            using var vm = new ScriptPanelViewModel(bus, () => [TestTrack]);
            var panel = new ScriptPanel { DataContext = vm };
            var window = new Window { Content = panel, Width = 500, Height = 700 };
            window.Show();

            Assert.NotNull(vm.ConfirmDeleteScript);

            panel.DataContext = null;

            Assert.Null(vm.ConfirmDeleteScript);

            window.Close();
            return 0;
        }, CancellationToken.None);
    }
}
