using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Aria.Prototype;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnHelpToggleClick(object? sender, RoutedEventArgs e) => ShowHelp();

    private void OnScenarioToggleClick(object? sender, RoutedEventArgs e) => Drawer.IsPaneOpen = !Drawer.IsPaneOpen;

    private void OnPaneCloseClick(object? sender, RoutedEventArgs e) => Drawer.IsPaneOpen = false;

    private void OnMuteClick(object? sender, RoutedEventArgs e)
    {
        bool muted = !MutedIcon.IsVisible;
        MutedIcon.IsVisible = muted;
        SoundIcon.IsVisible = !muted;
    }

    private void OnPlayPauseChanged(object? sender, RoutedEventArgs e) =>
        ToolTip.SetTip(PlayPause, PlayPause.IsChecked == true ? "Пауза — Esc" : "Играть — Space");

    private void OnHelpOverlayClick(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        CloseHelp();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (HelpOverlay.IsVisible)
        {
            e.Handled = true;
            CloseHelp();
        }
        else if (e.Key == Key.F1)
        {
            e.Handled = true;
            ShowHelp();
        }
        else
        {
            base.OnKeyDown(e);
        }
    }

    private void ShowHelp()
    {
        HelpOverlay.IsVisible = true;
        HelpOverlay.Focus();
    }

    private void CloseHelp() => HelpOverlay.IsVisible = false;
}
