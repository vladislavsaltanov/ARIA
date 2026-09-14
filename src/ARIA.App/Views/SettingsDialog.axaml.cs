namespace Aria.App.Views;

using Aria.App.ViewModels;
using Aria.App.Views.SettingsSections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

public partial class SettingsDialog : Window
{
    private readonly SettingsViewModel? _settings;
    private readonly List<string> _keys = [];

    public SettingsDialog()
        : this(null, null)
    {
    }

    public SettingsDialog(SettingsViewModel? settings = null, RemotePanelViewModel? remote = null)
    {
        InitializeComponent();
        if (settings is not null)
        {
            _settings = settings;
            DataContext = settings;
        }
        BuildSections(remote);
        AddHandler(InputElement.KeyDownEvent, OnDialogKeyDown, RoutingStrategies.Tunnel);
    }

    private void BuildSections(RemotePanelViewModel? remote)
    {
        foreach (var descriptor in SettingsSectionRegistry.All)
        {
            if (descriptor.Key == "remote" && remote is null)
            {
                continue;
            }
            var control = descriptor.Create();
            if (control is RemotePanel remotePanel)
            {
                remotePanel.DataContext = remote;
            }
            else
            {
                control.DataContext = DataContext;
            }
            control.IsVisible = false;
            SectionHost.Children.Add(control);
            SectionNav.Items.Add(descriptor.Title);
            _keys.Add(descriptor.Key);
        }
        if (SectionNav.ItemCount > 0)
        {
            SectionNav.SelectedIndex = 0;
        }
    }

    private void OnSectionSelected(object? sender, SelectionChangedEventArgs e) =>
        ShowSection(SectionNav.SelectedIndex);

    private void ShowSection(int index)
    {
        for (var i = 0; i < _keys.Count && i < SectionHost.Children.Count; i++)
        {
            SectionHost.Children[i].IsVisible = i == index;
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
