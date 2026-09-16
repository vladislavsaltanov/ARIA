namespace Aria.App.Views;

using System.ComponentModel;
using System.Text;
using Aria.App.Services;
using Aria.App.ViewModels;
using Aria.Core.Playback;
using Aria.Persistence;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Controls.Shapes;

public partial class MainWindow : Window
{
    private readonly HotkeyService? _hotkeys;
    private readonly Func<Window>? _settingsDialogFactory;
    private ProjectsViewModel? _projects;
    private QueueViewModel? _queue;
    private DragCoordinator? _drag;
    private bool _paneResizing;
    private bool _explicitPaneClose;
    private double _paneResizeStartX;
    private double _paneResizeStartWidth;
    private const double PaneKeyboardStep = 20.0;

    public MainWindow() : this(null)
    {
    }

    public MainWindow(
        HotkeyService? hotkeys,
        ProjectsViewModel? projectsViewModel = null,
        QueueViewModel? queueViewModel = null,
        Func<Window>? settingsDialogFactory = null,
        ScriptPanelViewModel? scriptViewModel = null)
    {
        InitializeComponent();
        _hotkeys = hotkeys;
        _settingsDialogFactory = settingsDialogFactory;
        if (projectsViewModel is not null)
        {
            _projects = projectsViewModel;
            RailProjects.DataContext = projectsViewModel;
            ProjectCenter.DataContext = projectsViewModel;
        }
        if (queueViewModel is not null)
        {
            _queue = queueViewModel;
            QueueColumn.DataContext = queueViewModel;
        }
        if (scriptViewModel is not null)
        {
            ScriptPanel.DataContext = scriptViewModel;
            ScriptPanel.CloseRequested += (_, _) => ToggleScriptPane();
            ScriptDrawer.PaneClosing += OnScriptPaneClosing;
            scriptViewModel.PropertyChanged += OnScriptPropertyChanged;
        }
        ProjectCenter.ScenarioToggleRequested += (_, _) => ToggleScriptPane();
        QueueColumn.CloseRequested += (_, _) => SetQueueOpen(false);
        TransportBar.SettingsRequested += OnSettingsRequested;
        Opened += OnOpened;
    }

    private void OnSettingsRequested(object? sender, EventArgs e)
    {
        if (_settingsDialogFactory?.Invoke() is { } dialog)
        {
            dialog.ShowDialog(this);
        }
    }

    public void ToggleScriptPane()
    {
        if (ScriptDrawer.IsPaneOpen)
        {
            ScriptPanel.CommitOpenEdit();
            _explicitPaneClose = true;
            try
            {
                ScriptDrawer.IsPaneOpen = false;
            }
            finally
            {
                _explicitPaneClose = false;
            }
            FocusSink.Focus();
        }
        else
        {
            ScriptDrawer.IsPaneOpen = true;
            HideDismissLayer();
        }
    }

    private void HideDismissLayer()
    {
        foreach (var layer in ScriptDrawer.GetVisualDescendants().OfType<Rectangle>().Where(r => r.Name == "LightDismissLayer"))
        {
            layer.IsVisible = false;
        }
    }

    private void OnScriptPaneClosing(object? sender, CancelRoutedEventArgs e)
    {
        if (!_explicitPaneClose)
        {
            e.Cancel = true;
        }
    }

    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!ScriptDrawer.IsPaneOpen
            || e.GetCurrentPoint(PaneResizer).Properties.PointerUpdateKind is not PointerUpdateKind.LeftButtonPressed)
        {
            return;
        }
        BeginPaneResize(e.GetPosition(this).X, ScriptDrawer.OpenPaneLength);
        e.Pointer.Capture(PaneResizer);
        e.Handled = true;
    }

    internal void BeginPaneResize(double startX, double startWidth)
    {
        _paneResizeStartX = startX;
        _paneResizeStartWidth = startWidth;
        _paneResizing = true;
        SuspendPaneTransitions();
    }

    internal void UpdatePaneResize(double currentX)
    {
        if (!_paneResizing)
        {
            return;
        }
        SetPaneWidth(_paneResizeStartWidth - (currentX - _paneResizeStartX));
    }

    internal void EndPaneResize()
    {
        _paneResizing = false;
        RestorePaneTransitions();
    }

    private void SetPaneWidth(double width) => ScriptDrawer.OpenPaneLength = Math.Clamp(width, 240, 600);

    private void OnResizeTunnelPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (point.Properties.PointerUpdateKind is PointerUpdateKind.LeftButtonPressed
            && ScriptDrawer.IsPaneOpen && IsGripSource(e.Source))
        {
            BeginPaneResize(e.GetPosition(this).X, ScriptDrawer.OpenPaneLength);
            e.Pointer.Capture(this);
        }
    }

    private static bool IsGripSource(object? source)
    {
        var current = source as Control;
        while (current is not null)
        {
            if (current.Name is "PaneResizer" or "PaneGrip")
            {
                return true;
            }
            current = current.Parent as Control;
        }
        return false;
    }

    private void OnResizeTunnelMoved(object? sender, PointerEventArgs e)
    {
        if (!_paneResizing)
        {
            return;
        }
        UpdatePaneResize(e.GetPosition(this).X);
    }

    private void OnResizeTunnelReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_paneResizing)
        {
            return;
        }
        EndPaneResize();
    }

    private void SuspendPaneTransitions()
    {
        if (PaneRoot() is { } pane)
        {
            pane.SetValue(Animatable.TransitionsProperty, new Transitions());
        }
    }

    private void RestorePaneTransitions() => PaneRoot()?.ClearValue(Animatable.TransitionsProperty);

    private Panel? PaneRoot() =>
        ScriptDrawer.GetVisualDescendants().OfType<Panel>().FirstOrDefault(p => p.Name == "PART_PaneRoot");

    private void OnScriptPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScriptPanelViewModel.HighlightedTrack)
            && sender is ScriptPanelViewModel viewModel)
        {
            _projects?.SetLinkedTrack(viewModel.HighlightedTrack);
        }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
        PaneResizer.AddHandler(InputElement.PointerPressedEvent, OnGripPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(InputElement.PointerPressedEvent, OnResizeTunnelPressed, RoutingStrategies.Tunnel);
        AddHandler(InputElement.PointerMovedEvent, OnResizeTunnelMoved, RoutingStrategies.Tunnel);
        AddHandler(InputElement.PointerReleasedEvent, OnResizeTunnelReleased, RoutingStrategies.Tunnel);
        if (_hotkeys is not null)
        {
            TransportBar.ApplyGestures(_hotkeys);
            ProjectCenter.ApplyGestures(_hotkeys);
        }
        _drag = new DragCoordinator(
            ProjectCenter.EntryListBox,
            QueueColumn.QueueListBox);
    }

    public void ResetDrag() => _drag?.Reset();

    public void AttachPlaybackHeader(PlaybackMonitor monitor, IWaveformStore? waveforms)
    {
        if (PlaybackHeader is { } header)
        {
            header.Attach(monitor, waveforms);
        }
    }

    public void SetQueueOpen(bool open)
    {
        QueueColumn.IsVisible = open;
        ContentGrid.ColumnDefinitions = new ColumnDefinitions(open ? "260,*,300" : "260,*,0");
    }

    private void OnQueueJumpClick(object? sender, RoutedEventArgs e)
    {
        if (QueueColumn.IsVisible)
        {
            SetQueueOpen(false);
            return;
        }
        SetQueueOpen(true);
        _queue?.FocusPlaying();
        QueueColumn.ScrollToSelected();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.None)
        {
            if (!IsTextInput(e.Source)
                && TransportBar?.DataContext is TransportViewModel transport
                && transport.TogglePlayPauseCommand.CanExecute(null))
            {
                transport.TogglePlayPauseCommand.Execute(null);
                e.Handled = true;
            }
            return;
        }
        if (e.Key is Key.Left or Key.Right
            && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            && ScriptDrawer.IsPaneOpen && !IsTextInput(e.Source))
        {
            SetPaneWidth(ScriptDrawer.OpenPaneLength + (e.Key == Key.Left ? PaneKeyboardStep : -PaneKeyboardStep));
            e.Handled = true;
            return;
        }
        if (_hotkeys is null)
        {
            return;
        }
        if (_hotkeys.TryHandle(HotkeyInput.GestureFor(e.Key, e.KeyModifiers)))
        {
            e.Handled = true;
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.None && !IsTextInput(e.Source))
        {
            e.Handled = true;
        }
    }

    private static bool IsTextInput(object? source)
    {
        var current = source as Control;
        while (current is not null)
        {
            if (current is TextBox)
            {
                return true;
            }
            current = current.Parent as Control;
        }
        return false;
    }
}
