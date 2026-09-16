namespace Aria.App.Tests;

using Aria.App.Views;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

[Collection("headless")]
public sealed class ScriptPanelHeadlessTests
{
    private static readonly Track TestTrack = new(
        TrackId.New(), "/audio/test.flac", "test", TimeSpan.FromMinutes(3), new TrackDefaults());

    private readonly HeadlessUnitTestSession _session;

    public ScriptPanelHeadlessTests(HeadlessSessionFixture fixture)
    {
        _session = fixture.Session;
    }

    [Fact]
    public async Task MainWindow_Composes_ScriptDrawer_ClosedByDefault()
    {
        await _session.Dispatch(() =>
        {
            var window = new MainWindow(null);
            window.Show();

            var drawer = window.FindControl<SplitView>("ScriptDrawer");
            Assert.NotNull(drawer);
            Assert.False(drawer.IsPaneOpen);
            Assert.Equal(340, drawer.OpenPaneLength);
            Assert.NotNull(window.FindControl<ScriptPanel>("ScriptPanel"));
            Assert.NotNull(window.FindControl<Border>("FocusSink"));
            var center = window.FindControl<ProjectCenter>("ProjectCenter");
            Assert.NotNull(center);
            Assert.NotNull(center.FindControl<Button>("ScenarioButton"));
            Assert.Null(center.FindControl<Button>("HelpButton"));

            window.Close();
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ToggleScriptPane_OpensAndCloses()
    {
        await _session.Dispatch(() =>
        {
            var window = new MainWindow(null);
            window.Show();
            var drawer = window.FindControl<SplitView>("ScriptDrawer");
            Assert.NotNull(drawer);

            window.ToggleScriptPane();
            Assert.True(drawer.IsPaneOpen);

            window.ToggleScriptPane();
            Assert.False(drawer.IsPaneOpen);

            window.Close();
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task QueueColumn_TogglesOpen_FromRailButtonAndClose()
    {
        await _session.Dispatch(() =>
        {
            var window = new MainWindow(null);
            window.Show();
            var queue = window.FindControl<QueueColumn>("QueueColumn");
            Assert.NotNull(queue);
            var railButton = window.FindControl<Button>("QueueButton");
            Assert.NotNull(railButton);
            var closeButton = queue.FindControl<Button>("QueueCloseButton");
            Assert.NotNull(closeButton);
            Assert.True(queue.IsVisible);

            closeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.False(queue.IsVisible);

            railButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.True(queue.IsVisible);

            railButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.False(queue.IsVisible);

            railButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.True(queue.IsVisible);

            window.Close();
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ScriptPanel_ManagementFooter_SitsBelowSeparator()
    {
        await _session.Dispatch(() =>
        {
            using var bus = NewBus();
            using var viewModel = new ViewModels.ScriptPanelViewModel(bus, () => [TestTrack]);
            var window = new Window { Width = 500, Height = 700, Content = new ScriptPanel { DataContext = viewModel } };
            window.Show();

            var panel = (ScriptPanel)window.Content!;
            viewModel.CreateScriptCommand.Execute(null);

            var separator = panel.FindControl<Separator>("ScriptFooterSeparator");
            Assert.NotNull(separator);
            Assert.True(separator.IsVisible);
            var create = panel.FindControl<Button>("NewScriptButton");
            var delete = panel.FindControl<Button>("DeleteScriptButton");
            Assert.NotNull(create);
            Assert.NotNull(delete);
            Assert.Same(viewModel.CreateScriptCommand, create.Command);
            Assert.Same(viewModel.DeleteSelectedCommand, delete.Command);
            var close = panel.FindControl<Button>("PaneCloseButton");
            Assert.NotNull(close);
            Assert.Null(close.Command);

            window.Close();
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ScriptPanel_ImportVisible_WithoutScripts()
    {
        await _session.Dispatch(() =>
        {
            using var bus = NewBus();
            using var viewModel = new ViewModels.ScriptPanelViewModel(bus, () => [TestTrack]);
            var window = new Window { Width = 500, Height = 700, Content = new ScriptPanel { DataContext = viewModel } };
            window.Show();

            var panel = (ScriptPanel)window.Content!;
            Assert.False(viewModel.HasScripts);
            var import = panel.FindControl<Button>("ImportScriptButton");
            Assert.NotNull(import);
            Assert.True(import.IsVisible);
            var create = panel.FindControl<Button>("NewScriptButton");
            Assert.NotNull(create);
            Assert.False(create.IsVisible);
            var delete = panel.FindControl<Button>("DeleteScriptButton");
            Assert.NotNull(delete);
            Assert.False(delete.IsVisible);

            window.Close();
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ScriptPanel_Binds_ScriptsAndLines()
    {
        await _session.Dispatch(() =>
        {
            using var bus = NewBus();
            using var viewModel = new ViewModels.ScriptPanelViewModel(bus, () => [TestTrack]);
            var window = new MainWindow(null, scriptViewModel: viewModel);
            window.Show();

            var panel = window.FindControl<ScriptPanel>("ScriptPanel");
            Assert.NotNull(panel);
            Assert.Same(viewModel, panel.DataContext);
            var tabs = panel.FindControl<ListBox>("TabsList");
            Assert.NotNull(tabs);

            viewModel.CreateScriptCommand.Execute(null);
            Assert.Equal(1, tabs.ItemCount);

            viewModel.AddLineCommand.Execute(null);
            var lines = panel.FindControl<ItemsControl>("LinesControl");
            Assert.NotNull(lines);
            Assert.Equal(1, lines.ItemCount);

            window.Close();
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PlayHint_AlwaysVisible_WithTimecode()
    {
        await _session.Dispatch(() =>
        {
            using var bus = NewBus();
            using var viewModel = new ViewModels.ScriptPanelViewModel(bus, () => [TestTrack]);
            var window = new Window { Width = 500, Height = 700, Content = new ScriptPanel { DataContext = viewModel } };
            window.Show();
            viewModel.CreateScriptCommand.Execute(null);
            CommitLine(viewModel, "0:10", "строка");
            Assert.False(viewModel.Lines[0].IsEditing);
            Assert.True(viewModel.Lines[0].HasText);

            var panel = (ScriptPanel)window.Content!;
            var row = RowAtPanel(panel, window);
            var go = FindByTag(row, "ScriptGo");
            Assert.NotNull(go);
            var time = FindByTag(row, "ScriptTime");
            Assert.NotNull(time);

            Assert.True(go.IsVisible);
            Assert.True(time.IsVisible);

            window.Close();
            return 0;
        }, CancellationToken.None);
    }

    private static Control RowAtPanel(ScriptPanel panel, Window window)
    {
        var lines = panel.FindControl<ItemsControl>("LinesControl");
        Assert.NotNull(lines);
        window.UpdateLayout();
        var row = lines.ContainerFromIndex(0) as Control;
        Assert.NotNull(row);
        return row;
    }

    [Fact]
    public async Task ClickOutside_KeepsDrawerOpen()
    {
        await _session.Dispatch(async () =>
        {
            using var bus = NewBus();
            using var viewModel = new ViewModels.ScriptPanelViewModel(bus, () => [TestTrack]);
            var window = new MainWindow(null, scriptViewModel: viewModel);
            window.Show();
            var drawer = window.FindControl<SplitView>("ScriptDrawer");
            Assert.NotNull(drawer);
            window.ToggleScriptPane();
            Assert.True(drawer.IsPaneOpen);

            var center = window.FindControl<ProjectCenter>("ProjectCenter");
            Assert.NotNull(center);
            var point = center.TranslatePoint(new Point(60, 200), window) ?? new Point(500, 400);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            await Task.Delay(200);

            Assert.True(drawer.IsPaneOpen);

            window.Close();
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ClickInsideEditBox_KeepsEditing()
    {
        await _session.Dispatch(async () =>
        {
            using var bus = NewBus();
            using var viewModel = new ViewModels.ScriptPanelViewModel(bus, () => [TestTrack]);
            var window = new Window { Width = 500, Height = 700, Content = new ScriptPanel { DataContext = viewModel } };
            window.Show();
            viewModel.CreateScriptCommand.Execute(null);
            viewModel.AddLineCommand.Execute(null);
            var line = Assert.Single(viewModel.Lines);
            Assert.True(line.IsEditing);
            line.EditText = "черновик";

            var panel = (ScriptPanel)window.Content!;
            var row = RowAtPanel(panel, window);
            var box = row.GetVisualDescendants().OfType<TextBox>()
                .FirstOrDefault(b => b.Text == "черновик");
            Assert.NotNull(box);
            var center = box.TranslatePoint(new Point(box.Bounds.Width / 2, box.Bounds.Height / 2), window);
            Assert.NotNull(center);
            window.MouseDown(center.Value, MouseButton.Left);
            window.MouseUp(center.Value, MouseButton.Left);
            await Task.Delay(100);

            Assert.True(line.IsEditing);
            Assert.Equal("черновик", line.EditText);

            window.Close();
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task OutsideClick_ReachesContentWhileDrawerOpen()
    {
        await _session.Dispatch(() =>
        {
            using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
            var a = new Project(ProjectId.New(), "A", [new ProjectEntry(EntryId.New(), TestTrack.Id)]);
            var b = new Project(ProjectId.New(), "B", [new ProjectEntry(EntryId.New(), TestTrack.Id)]);
            bus.Submit(new ClientId("setup"), 1, new LoadShow([TestTrack], [a, b], a.Id));
            using var projects = new ViewModels.ProjectsViewModel(bus, () => [TestTrack]);
            using var viewModel = new ViewModels.ScriptPanelViewModel(bus, () => [TestTrack]);
            var window = new MainWindow(null, projectsViewModel: projects, scriptViewModel: viewModel);
            window.Show();
            var drawer = window.FindControl<SplitView>("ScriptDrawer");
            Assert.NotNull(drawer);
            window.ToggleScriptPane();
            Assert.True(drawer.IsPaneOpen);

            var rail = window.FindControl<RailProjects>("RailProjects");
            Assert.NotNull(rail);
            var list = rail.FindControl<ListBox>("ProjectNames");
            Assert.NotNull(list);
            window.UpdateLayout();
            var row = list.ContainerFromIndex(1) as Control;
            Assert.NotNull(row);
            var local = new Point(row.Bounds.Width / 2, row.Bounds.Height / 2);
            var point = row.TranslatePoint(local, window);
            Assert.NotNull(point);
            window.MouseDown(point.Value, MouseButton.Left);
            window.MouseUp(point.Value, MouseButton.Left);

            Assert.Equal("B", projects.SelectedProject?.Name);
            Assert.True(drawer.IsPaneOpen);

            window.Close();
            return 0;
        }, CancellationToken.None);
    }
    private static CommandBus NewBus()
    {
        var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([TestTrack], [], null));
        return bus;
    }

    private static void CommitLine(ViewModels.ScriptPanelViewModel viewModel, string time, string text)
    {
        viewModel.AddLineCommand.Execute(null);
        var line = viewModel.Lines[^1];
        line.EditTimeText = time;
        line.EditText = text;
        viewModel.CommitEdit(line);
    }

    private static Point RowCenter(Window window, Control row)
    {
        var local = new Point(row.Bounds.Width / 2, row.Bounds.Height / 2);
        var mapped = row.TranslatePoint(local, window);
        Assert.NotNull(mapped);
        Assert.True(row.Bounds.Width > 0);
        return mapped.Value;
    }

    [Fact]
    public async Task PaneResizer_Drag_ChangesOpenPaneLength()
    {
        await _session.Dispatch(() =>
        {
            var window = new MainWindow(null);
            window.Show();
            var drawer = window.FindControl<SplitView>("ScriptDrawer");
            Assert.NotNull(drawer);
            drawer.IsPaneOpen = true;
            var grip = window.FindControl<Border>("PaneResizer");
            Assert.NotNull(grip);
            var before = drawer.OpenPaneLength;

            grip.RaiseEvent(new PointerPressedEventArgs(
                grip,
                new Pointer(1, PointerType.Mouse, true),
                window,
                new Point(1400, 450),
                0UL,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonPressed),
                KeyModifiers.None,
                1));
            window.UpdatePaneResize(1360);
            Assert.Equal(Math.Clamp(before + 40, 240, 600), drawer.OpenPaneLength);

            window.UpdatePaneResize(2400);
            Assert.Equal(240, drawer.OpenPaneLength);

            window.UpdatePaneResize(400);
            Assert.Equal(600, drawer.OpenPaneLength);
            window.EndPaneResize();

            window.Close();
            return 0;
        }, CancellationToken.None);
    }

    private static Control? FindByTag(Control root, string tag)
    {
        if (root.Tag as string == tag)
        {
            return root;
        }
        foreach (var child in root.GetVisualChildren())
        {
            if (child is Control control && FindByTag(control, tag) is { } found)
            {
                return found;
            }
        }
        return null;
    }
}
