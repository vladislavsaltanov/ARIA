namespace Aria.App.Views.SettingsSections;

using Aria.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

public partial class HotkeysSection : UserControl
{
    public HotkeysSection()
    {
        InitializeComponent();
    }

    private void OnRecordClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: SettingsViewModel.GestureRow row }
            && DataContext is SettingsViewModel settings)
        {
            settings.BeginRecord(row);
        }
    }
}
