namespace Aria.App.Tests;

using Aria.Core.Model;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

[Collection("headless")]
public sealed class MarkerActionPopupHeadlessTests
{
    private static readonly TrackId TestTrackId = TrackId.New();

    private readonly HeadlessUnitTestSession _session;

    public MarkerActionPopupHeadlessTests(HeadlessSessionFixture fixture)
    {
        _session = fixture.Session;
    }

    [Fact]
    public async Task HoverThenBorderExit_PopupStaysOpenDuringGrace()
    {
        await _session.Dispatch(async () =>
        {
            using var lane = new TestLane(TestTrackId, null, [Marker("вход", 45)]);
            lane.Window.MouseMove(lane.PointAt(45));
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(650);
            Assert.True(lane.Header.IsActionPopupOpen);

            lane.Window.MouseMove(new Point(5, 5));
            Dispatcher.UIThread.RunJobs();
            Assert.True(lane.Header.IsActionPopupOpen);

            await Task.Delay(300);
            Assert.False(lane.Header.IsActionPopupOpen);
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PopupContent_UsesDarkTheme()
    {
        await _session.Dispatch(async () =>
        {
            using var lane = new TestLane(TestTrackId, null, [Marker("вход", 45)]);
            lane.Window.MouseMove(lane.PointAt(45));
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(650);
            var content = lane.Header.ActionPopupContent;
            Assert.NotNull(content);
            var button = content!.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b => (string?)b.Content == "Пауза");
            Assert.NotNull(button);
            Assert.Equal(ThemeVariant.Dark, button.ActualThemeVariant);
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ActionPopup_ShouldUseOverlayLayer_SoCornersAreNotBackedByNativeWindow()
    {
        await _session.Dispatch(async () =>
        {
            using var lane = new TestLane(TestTrackId, null, [Marker("вход", 45)]);
            lane.Window.MouseMove(lane.PointAt(45));
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(650);
            Assert.True(lane.Header.IsActionPopupOpen);
            Assert.True(lane.Header.ActionPopupShouldUseOverlayLayer);
            return 0;
        }, CancellationToken.None);
    }

    private static Marker Marker(string name, double seconds) =>
        new(name, TimeSpan.FromSeconds(seconds), MarkerAction.Pause);
}
