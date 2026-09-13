namespace Aria.App.Tests;

using Aria.App.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;

[Collection("headless")]
public sealed class ScriptPaneResizeTests : IDisposable
{
    private readonly HeadlessUnitTestSession _session = HeadlessUnitTestSession.StartNew(typeof(App));

    public void Dispose() => _session.Dispose();

    [Fact]
    public async Task GripPointerPress_StartsDrag()
    {
        var after = 0.0;
        await _session.Dispatch(() =>
        {
            var window = new MainWindow(null);
            window.Show();
            var drawer = window.FindControl<SplitView>("ScriptDrawer");
            Assert.NotNull(drawer);
            drawer.IsPaneOpen = true;
            var grip = window.FindControl<Border>("PaneResizer");
            Assert.NotNull(grip);
            grip.RaiseEvent(new PointerPressedEventArgs(
                grip,
                new Pointer(1, PointerType.Mouse, true),
                window,
                new Point(1400, 450),
                0UL,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonPressed),
                KeyModifiers.None,
                1));
            window.UpdatePaneResize(1340);
            after = drawer.OpenPaneLength;
            window.EndPaneResize();
            window.Close();
            return 0;
        }, CancellationToken.None);

        Assert.Equal(400, after);
    }

    [Fact]
    public async Task GripPointerPress_ClosedPane_Ignored()
    {
        var after = 0.0;
        await _session.Dispatch(() =>
        {
            var window = new MainWindow(null);
            window.Show();
            var drawer = window.FindControl<SplitView>("ScriptDrawer");
            Assert.NotNull(drawer);
            drawer.IsPaneOpen = false;
            var grip = window.FindControl<Border>("PaneResizer");
            Assert.NotNull(grip);
            grip.RaiseEvent(new PointerPressedEventArgs(
                grip,
                new Pointer(1, PointerType.Mouse, true),
                window,
                new Point(1400, 450),
                0UL,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonPressed),
                KeyModifiers.None,
                1));
            window.UpdatePaneResize(1340);
            after = drawer.OpenPaneLength;
            window.EndPaneResize();
            window.Close();
            return 0;
        }, CancellationToken.None);

        Assert.Equal(340, after);
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
    public async Task KeyboardAltLeft_WidensPane()
    {
        var after = 0.0;
        var handled = false;
        await _session.Dispatch(() =>
        {
            var window = new MainWindow(null);
            window.Show();
            var drawer = window.FindControl<SplitView>("ScriptDrawer");
            Assert.NotNull(drawer);
            drawer.IsPaneOpen = true;
            var args = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Left,
                KeyModifiers = KeyModifiers.Alt,
                Source = window,
            };
            window.RaiseEvent(args);
            handled = args.Handled;
            after = drawer.OpenPaneLength;
            window.Close();
            return 0;
        }, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(360, after);
    }

    [Fact]
    public async Task KeyboardAltRight_NarrowsPane()
    {
        var after = 0.0;
        await _session.Dispatch(() =>
        {
            var window = new MainWindow(null);
            window.Show();
            var drawer = window.FindControl<SplitView>("ScriptDrawer");
            Assert.NotNull(drawer);
            drawer.IsPaneOpen = true;
            window.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Right,
                KeyModifiers = KeyModifiers.Alt,
                Source = window,
            });
            after = drawer.OpenPaneLength;
            window.Close();
            return 0;
        }, CancellationToken.None);

        Assert.Equal(320, after);
    }

    [Fact]
    public async Task KeyboardCtrlLeft_WidensPane()
    {
        var after = 0.0;
        var handled = false;
        await _session.Dispatch(() =>
        {
            var window = new MainWindow(null);
            window.Show();
            var drawer = window.FindControl<SplitView>("ScriptDrawer");
            Assert.NotNull(drawer);
            drawer.IsPaneOpen = true;
            var args = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Left,
                KeyModifiers = KeyModifiers.Control,
                Source = window,
            };
            window.RaiseEvent(args);
            handled = args.Handled;
            after = drawer.OpenPaneLength;
            window.Close();
            return 0;
        }, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(360, after);
    }

    [Fact]
    public async Task KeyboardCtrlRight_NarrowsPane()
    {
        var after = 0.0;
        await _session.Dispatch(() =>
        {
            var window = new MainWindow(null);
            window.Show();
            var drawer = window.FindControl<SplitView>("ScriptDrawer");
            Assert.NotNull(drawer);
            drawer.IsPaneOpen = true;
            window.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Right,
                KeyModifiers = KeyModifiers.Control,
                Source = window,
            });
            after = drawer.OpenPaneLength;
            window.Close();
            return 0;
        }, CancellationToken.None);

        Assert.Equal(320, after);
    }

    [Fact]
    public async Task KeyboardResize_ClampsPaneWidth()
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
            drawer.OpenPaneLength = 600;
            window.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Left,
                KeyModifiers = KeyModifiers.Control,
                Source = window,
            });
            wide = drawer.OpenPaneLength;
            drawer.OpenPaneLength = 240;
            window.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Right,
                KeyModifiers = KeyModifiers.Control,
                Source = window,
            });
            narrow = drawer.OpenPaneLength;
            window.Close();
            return 0;
        }, CancellationToken.None);

        Assert.Equal(600, wide);
        Assert.Equal(240, narrow);
    }

    [Fact]
    public async Task KeyboardResize_ClosedPaneNoOp()
    {
        var after = 0.0;
        await _session.Dispatch(() =>
        {
            var window = new MainWindow(null);
            window.Show();
            var drawer = window.FindControl<SplitView>("ScriptDrawer");
            Assert.NotNull(drawer);
            drawer.IsPaneOpen = false;
            window.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Left,
                KeyModifiers = KeyModifiers.Control,
                Source = window,
            });
            after = drawer.OpenPaneLength;
            window.Close();
            return 0;
        }, CancellationToken.None);

        Assert.Equal(340, after);
    }

    [Fact]
    public async Task KeyboardResize_InTextBoxSkipped()
    {
        var after = 0.0;
        await _session.Dispatch(() =>
        {
            var window = new MainWindow(null);
            window.Show();
            var drawer = window.FindControl<SplitView>("ScriptDrawer");
            Assert.NotNull(drawer);
            drawer.IsPaneOpen = true;
            var panel = window.FindControl<Control>("ScriptPanel");
            Assert.NotNull(panel);
            var box = panel.FindControl<TextBox>("ScriptNameBox");
            Assert.NotNull(box);
            box.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Left,
                KeyModifiers = KeyModifiers.Control,
                Source = box,
            });
            after = drawer.OpenPaneLength;
            window.Close();
            return 0;
        }, CancellationToken.None);

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
