namespace Aria.App.Views.SettingsSections;

using Aria.App.ViewModels.Settings;
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
        if (sender is Button { DataContext: HotkeysSectionVm.GestureRow row }
            && DataContext is HotkeysSectionVm settings)
        {
            settings.BeginRecord(row);
        }
    }
}
