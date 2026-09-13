namespace Aria.App.Tests;

using Aria.App.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;

[Collection("headless")]
public sealed class ScriptPaneResizeTests : IDisposable
{
    private readonly HeadlessUnitTestSession _session = HeadlessUnitTestSession.StartNew(typeof(App));

    public void Dispose() => _session.Dispose();

    [Fact]
    public async Task GripDragDelta_UpdatesPaneWidth()
    {
        var after = 0.0;
        await _session.Dispatch(() =>
        {
            var window = new MainWindow(null);
            window.Show();
            var drawer = window.FindControl<SplitView>("ScriptDrawer");
            Assert.NotNull(drawer);
            drawer.IsPaneOpen = true;
            var thumb = window.FindControl<Thumb>("PaneResizer");
            Assert.NotNull(thumb);
            thumb.RaiseEvent(new VectorEventArgs
            {
                RoutedEvent = Thumb.DragDeltaEvent,
                Vector = new Vector(-60, 0),
            });
            after = drawer.OpenPaneLength;
            window.Close();
            return 0;
        }, CancellationToken.None);

        Assert.Equal(400, after);
    }

    [Fact]
    public async Task GripDragDelta_ClampsPaneWidth()
    {
        var after = 0.0;
        await _session.Dispatch(() =>
        {
            var window = new MainWindow(null);
            window.Show();
            var drawer = window.FindControl<SplitView>("ScriptDrawer");
            Assert.NotNull(drawer);
            drawer.IsPaneOpen = true;
            var thumb = window.FindControl<Thumb>("PaneResizer");
            Assert.NotNull(thumb);
            thumb.RaiseEvent(new VectorEventArgs
            {
                RoutedEvent = Thumb.DragDeltaEvent,
                Vector = new Vector(2000, 0),
            });
            after = drawer.OpenPaneLength;
            window.Close();
            return 0;
        }, CancellationToken.None);

        Assert.Equal(240, after);
    }

    [Fact]
    public async Task GripDrag_SuspendsPaneTransitions()
    {
        var during = -1;
        var restored = -1;
        await _session.Dispatch(() =>
        {
            var window = new MainWindow(null);
            window.Show();
            var drawer = window.FindControl<SplitView>("ScriptDrawer");
            Assert.NotNull(drawer);
            drawer.IsPaneOpen = true;
            var thumb = window.FindControl<Thumb>("PaneResizer");
            Assert.NotNull(thumb);
            thumb.RaiseEvent(new VectorEventArgs
            {
                RoutedEvent = Thumb.DragStartedEvent,
                Vector = new Vector(0, 0),
            });
            during = PaneTransitions(drawer).Count;
            thumb.RaiseEvent(new VectorEventArgs
            {
                RoutedEvent = Thumb.DragCompletedEvent,
                Vector = new Vector(0, 0),
            });
            restored = PaneTransitions(drawer).Count;
            window.Close();
            return 0;
        }, CancellationToken.None);

        Assert.Equal(0, during);
        Assert.Equal(1, restored);
    }

    private static System.Collections.Generic.IList<Avalonia.Animation.ITransition> PaneTransitions(SplitView drawer)
    {
        var pane = drawer.GetVisualDescendants().OfType<Panel>().FirstOrDefault(p => p.Name == "PART_PaneRoot");
        Assert.NotNull(pane);
        Assert.NotNull(pane.Transitions);
        return pane.Transitions;
    }
}
