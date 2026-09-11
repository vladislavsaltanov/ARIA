namespace Aria.App.Views;

using System.ComponentModel;
using Aria.App.Services;
using Aria.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Threading;

public partial class TransportBar : UserControl
{
    private HotkeyService? _hotkeys;
    private TransportViewModel? _viewModel;
    private IDisposable? _clockTimer;

    public TransportBar()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public void ApplyGestures(HotkeyService hotkeys)
    {
        _hotkeys = hotkeys;
        ApplyTips();
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is TransportViewModel viewModel)
        {
            _viewModel = viewModel;
            viewModel.PropertyChanged += OnViewModelChanged;
        }
        ApplyTips();
        _clockTimer = DispatcherTimer.Run(
            () =>
            {
                (_viewModel ?? DataContext as TransportViewModel)?.RefreshWallClock();
                return true;
            },
            TimeSpan.FromSeconds(1));
    }

    private void OnUnloaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelChanged;
            _viewModel = null;
        }
        _clockTimer?.Dispose();
        _clockTimer = null;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TransportViewModel.IsPlaying))
        {
            ApplyPlayPauseTip();
        }
    }

    private void ApplyTips()
    {
        if (_hotkeys is null)
        {
            return;
        }
        ToolTip.SetTip(StopButton, HotkeyLabels.Tip(HotkeyLabels.Label("stop"), _hotkeys.GestureFor("stop")));
        ToolTip.SetTip(NextButton, HotkeyLabels.Tip(HotkeyLabels.Label("next"), _hotkeys.GestureFor("next")));
        ToolTip.SetTip(MuteButton, "Мьют — только мышь");
        ToolTip.SetTip(SettingsButton, HotkeyLabels.Label("settings"));
        ToolTip.SetTip(PanicButton, HotkeyLabels.Tip(HotkeyLabels.Label("panic"), _hotkeys.GestureFor("panic")));
        ToolTip.SetTip(LockIcon, HotkeyLabels.Tip(HotkeyLabels.Label("lock"), _hotkeys.GestureFor("lock")));
        ApplyPlayPauseTip();
    }

    private void ApplyPlayPauseTip()
    {
        if (_hotkeys is null)
        {
            return;
        }
        var playing = _viewModel?.IsPlaying ?? false;
        var tip = playing
            ? HotkeyLabels.Tip(HotkeyLabels.Label("pause"), _hotkeys.GestureFor("pause"))
            : HotkeyLabels.Tip(HotkeyLabels.Label("play"), _hotkeys.GestureFor("play"));
        ToolTip.SetTip(PlayPauseButton, tip);
    }
}
