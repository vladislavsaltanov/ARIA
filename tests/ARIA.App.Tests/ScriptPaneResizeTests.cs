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

    [Fact]
    public async Task ManualDrag_UpdatesPaneWidthFromPointerDelta()
    {
        var after = 0.0;
        await _session.Dispatch(() =>
        {
            var window = new MainWindow(null);
            window.Show();
            var drawer = window.FindControl<SplitView>("ScriptDrawer");
            Assert.NotNull(drawer);
            drawer.IsPaneOpen = true;
            var start = drawer.OpenPaneLength;
            window.BeginPaneResize(1000, start);
            window.UpdatePaneResize(940);
            after = drawer.OpenPaneLength;
            window.EndPaneResize();
            window.Close();
            return 0;
        }, CancellationToken.None);

        Assert.Equal(400, after);
    }

    [Fact]
    public async Task ManualDrag_ClampsPaneWidth()
    {
        var wide = 0.0;
        var narrow = 0.0;
        await _session.Dispatch(() =>
        {
            var window = new MainWindow(null);
            window.Show();
            var drawer = window.FindControl<SplitView>("ScriptDrawer");
            Assert.NotNull(drawer);
            drawer.IsPaneOpen = true;
            window.BeginPaneResize(1000, drawer.OpenPaneLength);
            window.UpdatePaneResize(-2000);
            wide = drawer.OpenPaneLength;
            window.UpdatePaneResize(5000);
            narrow = drawer.OpenPaneLength;
            window.EndPaneResize();
            window.Close();
            return 0;
        }, CancellationToken.None);

        Assert.Equal(600, wide);
        Assert.Equal(240, narrow);
    }

    [Fact]
    public async Task ManualDrag_SuspendsAndRestoresTransitions()
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
            window.BeginPaneResize(1000, drawer.OpenPaneLength);
            during = PaneTransitions(drawer).Count;
            window.EndPaneResize();
            restored = PaneTransitions(drawer).Count;
            window.Close();
            return 0;
        }, CancellationToken.None);

        Assert.Equal(0, during);
        Assert.Equal(1, restored);
    }

    [Fact]
    public async Task ManualDrag_SuppressesDragDeltaFallback()
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
            var start = drawer.OpenPaneLength;
            window.BeginPaneResize(1000, start);
            thumb.RaiseEvent(new VectorEventArgs
            {
                RoutedEvent = Thumb.DragDeltaEvent,
                Vector = new Vector(-60, 0),
            });
            after = drawer.OpenPaneLength;
            window.EndPaneResize();
            window.Close();
            return 0;
        }, CancellationToken.None);

        Assert.Equal(340, after);
    }

    [Fact]
    public async Task TunnelPress_ZoneBoundaries()
    {
        var inside = false;
        var outside = false;
        await _session.Dispatch(() =>
        {
            var window = new MainWindow(null);
            window.Show();
            var drawer = window.FindControl<SplitView>("ScriptDrawer");
            Assert.NotNull(drawer);
            drawer.IsPaneOpen = true;
            window.Measure(new Size(1440, 900));
            window.Arrange(new Rect(0, 0, 1440, 900));
            var edge = window.PaneLeftEdge();
            Assert.False(double.IsNaN(edge));
            inside = window.TryBeginPaneEdgeResize(edge + 8, true);
            window.EndPaneResize();
            inside = inside && window.TryBeginPaneEdgeResize(edge - 8, true);
            window.EndPaneResize();
            outside = window.TryBeginPaneEdgeResize(edge + 9, true)
                || window.TryBeginPaneEdgeResize(edge - 9, true);
            window.EndPaneResize();
            window.Close();
            return 0;
        }, CancellationToken.None);

        Assert.True(inside);
        Assert.False(outside);
    }

    [Fact]
    public async Task TunnelPress_AtEdge_StartsDrag()
    {
        var started = false;
        var after = 0.0;
        await _session.Dispatch(() =>
        {
            var window = new MainWindow(null);
            window.Show();
            var drawer = window.FindControl<SplitView>("ScriptDrawer");
            Assert.NotNull(drawer);
            drawer.IsPaneOpen = true;
            window.Measure(new Size(1440, 900)); window.Arrange(new Rect(0, 0, 1440, 900));
            var edge = window.PaneLeftEdge();
            Assert.False(double.IsNaN(edge));
            started = window.TryBeginPaneEdgeResize(edge, true);
            window.UpdatePaneResize(edge - 60);
            after = drawer.OpenPaneLength;
            window.EndPaneResize();
            window.Close();
            return 0;
        }, CancellationToken.None);

        Assert.True(started);
        Assert.Equal(400, after);
    }

    [Fact]
    public async Task TunnelPress_OutsideZoneOrClosedPane_Ignored()
    {
        var started = false;
        var after = 0.0;
        await _session.Dispatch(() =>
        {
            var window = new MainWindow(null);
            window.Show();
            var drawer = window.FindControl<SplitView>("ScriptDrawer");
            Assert.NotNull(drawer);
            drawer.IsPaneOpen = true;
            window.Measure(new Size(1440, 900)); window.Arrange(new Rect(0, 0, 1440, 900));
            started = window.TryBeginPaneEdgeResize(0, true)
                || window.TryBeginPaneEdgeResize(window.PaneLeftEdge(), false);
            window.UpdatePaneResize(-5000);
            after = drawer.OpenPaneLength;
            window.EndPaneResize();
            drawer.IsPaneOpen = false;
            started = started || window.TryBeginPaneEdgeResize(window.PaneLeftEdge(), true);
            window.Close();
            return 0;
        }, CancellationToken.None);

        Assert.False(started);
        Assert.Equal(340, after);
    }

    private static System.Collections.Generic.IList<Avalonia.Animation.ITransition> PaneTransitions(SplitView drawer)
    {
        var pane = drawer.GetVisualDescendants().OfType<Panel>().FirstOrDefault(p => p.Name == "PART_PaneRoot");
        Assert.NotNull(pane);
        Assert.NotNull(pane.Transitions);
        return pane.Transitions;
    }
}
