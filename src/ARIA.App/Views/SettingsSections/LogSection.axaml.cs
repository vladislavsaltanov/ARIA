namespace Aria.App.Views.SettingsSections;

using Aria.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

public partial class LogSection : UserControl
{
    public LogSection()
    {
        InitializeComponent();
    }

    private void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel settings)
        {
            settings.Logging.OpenFolder();
        }
    }
}
