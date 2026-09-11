namespace Aria.App.Views;

using Aria.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

public partial class SettingsDialog : Window
{
    private readonly SettingsViewModel? _settings;

    public SettingsDialog(SettingsViewModel? settings = null, RemotePanelViewModel? remote = null)
    {
        InitializeComponent();
        if (settings is not null)
        {
            _settings = settings;
            DataContext = settings;
        }
        if (remote is not null)
        {
            PairingPanel.DataContext = remote;
        }
        else
        {
            PairingPanel.IsVisible = false;
        }
        AddHandler(InputElement.KeyDownEvent, OnDialogKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnRecordClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: SettingsViewModel.GestureRow row })
        {
            _settings?.BeginRecord(row);
        }
    }

    private void OnDialogKeyDown(object? sender, KeyEventArgs e)
    {
        if (_settings is null)
        {
            return;
        }
        if (_settings.RecordingRow is null)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
            return;
        }
        switch (e.Key)
        {
            case Key.Escape:
                _settings.CancelRecord();
                break;
            case Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin:
                break;
            default:
                _settings.RecordGesture(HotkeyInput.GestureFor(e.Key, e.KeyModifiers));
                break;
        }
        e.Handled = true;
    }
}
