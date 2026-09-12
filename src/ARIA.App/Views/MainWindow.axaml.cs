namespace Aria.App.Views;

using System.ComponentModel;
using System.Text;
using Aria.App.Services;
using Aria.App.ViewModels;
using Aria.Core.Playback;
using Aria.Persistence;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

public partial class MainWindow : Window
{
    private readonly HotkeyService? _hotkeys;
    private readonly Func<Window>? _settingsDialogFactory;
    private LibraryViewModel? _library;
    private PlaylistsViewModel? _playlists;
    private QueueViewModel? _queue;
    private DragCoordinator? _drag;

    public MainWindow() : this(null)
    {
    }

    public MainWindow(
        HotkeyService? hotkeys,
        LibraryViewModel? libraryViewModel = null,
        PlaylistsViewModel? playlistsViewModel = null,
        QueueViewModel? queueViewModel = null,
        Func<Window>? settingsDialogFactory = null,
        ScriptPanelViewModel? scriptViewModel = null)
    {
        InitializeComponent();
        _hotkeys = hotkeys;
        _settingsDialogFactory = settingsDialogFactory;
        if (libraryViewModel is not null)
        {
            _library = libraryViewModel;
            LibrarySection.DataContext = libraryViewModel;
            ImportButton.Command = libraryViewModel.ImportCommand;
        }
        if (playlistsViewModel is not null)
        {
            _playlists = playlistsViewModel;
            RailPlaylists.DataContext = playlistsViewModel;
            PlaylistCenter.DataContext = playlistsViewModel;
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
            scriptViewModel.PropertyChanged += OnScriptPropertyChanged;
        }
        PlaylistCenter.ScenarioToggleRequested += (_, _) => ToggleScriptPane();
        PlaylistCenter.HelpRequested += (_, _) => HelpOverlay.IsVisible = true;
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
            ScriptDrawer.IsPaneOpen = false;
            FocusSink.Focus();
        }
        else
        {
            ScriptDrawer.IsPaneOpen = true;
        }
    }

    private void OnPaneResize(object? sender, VectorEventArgs e) =>
        ScriptDrawer.OpenPaneLength = Math.Clamp(ScriptDrawer.OpenPaneLength - e.Vector.X, 240, 600);

    private void OnScriptPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScriptPanelViewModel.HighlightedTrack)
            && sender is ScriptPanelViewModel viewModel)
        {
            _library?.SetLinkedTrack(viewModel.HighlightedTrack);
            _playlists?.SetLinkedTrack(viewModel.HighlightedTrack);
        }
    }

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!ScriptDrawer.IsPaneOpen)
        {
            return;
        }
        var current = e.Source as Control;
        while (current is not null)
        {
            if (current == ScriptPanel || current == PaneResizer || current.Name == "ScenarioButton")
            {
                return;
            }
            current = current.Parent as Control;
        }
        ScriptPanel.CommitOpenEdit();
        ScriptDrawer.IsPaneOpen = false;
        FocusSink.Focus();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(InputElement.PointerPressedEvent, OnRootPointerPressed, RoutingStrategies.Tunnel);
        if (_hotkeys is not null)
        {
            TransportBar.ApplyGestures(_hotkeys);
            PlaylistCenter.ApplyGestures(_hotkeys);
            BuildHotkeyTable();
        }
        _drag = new DragCoordinator(
            LibrarySection.TrackListBox,
            PlaylistCenter.EntryListBox,
            QueueColumn.QueueListBox);
    }

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
        if (!QueueColumn.IsVisible)
        {
            SetQueueOpen(true);
            return;
        }
        _queue?.FocusPlaying();
        QueueColumn.ScrollToSelected();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (HelpOverlay.IsVisible)
        {
            HelpOverlay.IsVisible = false;
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F1)
        {
            HelpOverlay.IsVisible = true;
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

    private void OnHelpOverlayClick(object? sender, PointerPressedEventArgs e) => HelpOverlay.IsVisible = false;

    private void BuildHotkeyTable()
    {
        if (_hotkeys is null)
        {
            return;
        }
        string[] actions = ["play", "pause", "panic", "next", "replay", "lock", "toggle-script", "reset-clock"];
        for (var row = 0; row < actions.Length; row++)
        {
            var action = new TextBlock
            {
                Text = HotkeyLabels.Label(actions[row]),
                FontSize = 13,
                Foreground = Avalonia.Media.Brushes.Gainsboro,
                Margin = new Avalonia.Thickness(0, 9, 0, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            Grid.SetRow(action, row + 1);
            var gesture = new TextBlock
            {
                Text = _hotkeys.GestureFor(actions[row]),
                FontFamily = new FontFamily("Consolas, Menlo"),
                FontSize = 12,
                Foreground = Avalonia.Media.Brushes.DimGray,
                TextAlignment = TextAlignment.Right,
                Margin = new Avalonia.Thickness(0, 9, 0, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            Grid.SetRow(gesture, row + 1);
            Grid.SetColumn(gesture, 1);
            HotkeyTable.Children.Add(action);
            HotkeyTable.Children.Add(gesture);
        }
        for (var row = 0; row <= actions.Length; row++)
        {
            HotkeyTable.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }
    }
}
