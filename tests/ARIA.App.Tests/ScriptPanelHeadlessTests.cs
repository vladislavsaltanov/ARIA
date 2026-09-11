namespace Aria.App.Tests;

using Aria.App.Views;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;
using Avalonia.Controls;
using Avalonia.Headless;

public sealed class ScriptPanelHeadlessTests : IDisposable
{
    private static readonly Track TestTrack = new(
        TrackId.New(), "/audio/test.flac", "test", TimeSpan.FromMinutes(3), new TrackDefaults());

    private readonly HeadlessUnitTestSession _session = HeadlessUnitTestSession.StartNew(typeof(App));

    public void Dispose() => _session.Dispose();

    [Fact]
    public void MainWindow_Composes_ScriptDrawer_ClosedByDefault()
    {
        _session.Dispatch(() =>
        {
            var window = new MainWindow(null);
            window.Show();

            var drawer = window.FindControl<SplitView>("ScriptDrawer");
            Assert.NotNull(drawer);
            Assert.False(drawer.IsPaneOpen);
            Assert.Equal(340, drawer.OpenPaneLength);
            Assert.NotNull(window.FindControl<ScriptPanel>("ScriptPanel"));
            Assert.NotNull(window.FindControl<Border>("FocusSink"));
            Assert.NotNull(window.FindControl<Button>("ScenarioButton"));
            Assert.NotNull(window.FindControl<Button>("HelpButton"));

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public void ToggleScriptPane_OpensAndCloses()
    {
        _session.Dispatch(() =>
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
        }, CancellationToken.None);
    }

    [Fact]
    public void ScriptPanel_Binds_ScriptsAndLines()
    {
        _session.Dispatch(() =>
        {
            using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
            bus.Submit(new ClientId("setup"), 1, new LoadShow([TestTrack], [], null));
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
        }, CancellationToken.None);
    }
}
