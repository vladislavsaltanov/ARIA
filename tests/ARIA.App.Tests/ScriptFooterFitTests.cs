namespace Aria.App.Tests;

using Aria.App.Views;
using Aria.Core.Model;
using Aria.Core.Runtime;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;

[Collection("headless")]
public sealed class ScriptFooterFitTests
{
    private static readonly Track TestTrack = new(
        TrackId.New(), "/audio/test.flac", "test", TimeSpan.FromMinutes(3), new TrackDefaults());

    private readonly HeadlessUnitTestSession _session;

    public ScriptFooterFitTests(HeadlessSessionFixture fixture)
    {
        _session = fixture.Session;
    }

    [Theory]
    [InlineData(300)]
    [InlineData(330)]
    [InlineData(420)]
    public async Task ManagementFooter_AllActionsStayInsidePane(double width)
    {
        await _session.Dispatch(() =>
        {
            using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
            using var viewModel = new ViewModels.ScriptPanelViewModel(bus, () => [TestTrack]);
            var panel = new ScriptPanel { DataContext = viewModel };
            var window = new Window { Width = width, Height = 700, Content = panel };
            window.Show();
            viewModel.CreateScriptCommand.Execute(null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            foreach (var name in new[] { "NewScriptButton", "DeleteScriptButton", "ImportScriptButton", "ExportScriptButton" })
            {
                var button = panel.FindControl<Button>(name);
                Assert.NotNull(button);
                var origin = button.TranslatePoint(new Point(0, 0), panel);
                Assert.NotNull(origin);
                var right = origin.Value.X + button.Bounds.Width;
                Assert.True(right <= panel.Bounds.Width + 0.5, $"{name} clipped at width {width}: right={right:F1} pane={panel.Bounds.Width:F1}");
            }

            window.Close();
            return 0;
        }, CancellationToken.None);
    }
}
