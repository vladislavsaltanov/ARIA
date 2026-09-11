namespace Aria.App.Views;

using Aria.App.Services;
using Aria.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

public partial class PlaylistCenter : UserControl
{
    public PlaylistCenter()
    {
        InitializeComponent();
    }

    public event EventHandler? ScenarioToggleRequested;

    public event EventHandler? HelpRequested;

    public ListBox EntryListBox => EntryList;

    public void ApplyGestures(HotkeyService hotkeys) =>
        ToolTip.SetTip(ScenarioButton, HotkeyLabels.Tip(HotkeyLabels.Label("toggle-script"), hotkeys.GestureFor("toggle-script")));

    private void OnScenarioClick(object? sender, RoutedEventArgs e) => ScenarioToggleRequested?.Invoke(this, EventArgs.Empty);

    private void OnHelpClick(object? sender, RoutedEventArgs e) => HelpRequested?.Invoke(this, EventArgs.Empty);

    private void OnTitleDoubleTapped(object? sender, TappedEventArgs e)
    {
        TitleText.IsVisible = false;
        TitleBox.IsVisible = true;
        TitleBox.Focus();
        TitleBox.SelectAll();
    }

    private void OnTitleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitTitle();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            TitleBox.IsVisible = false;
            TitleText.IsVisible = true;
            e.Handled = true;
        }
    }

    private void OnTitleCommit(object? sender, RoutedEventArgs e) => CommitTitle();

    private void CommitTitle()
    {
        if (DataContext is PlaylistsViewModel viewModel && TitleBox.IsVisible)
        {
            viewModel.RenamePlaylistCommand.Execute(TitleBox.Text);
        }
        TitleBox.IsVisible = false;
        TitleText.IsVisible = true;
    }

    private void OnEnqueueClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item
            && item.DataContext is PlaylistsViewModel.EntryVm entry
            && DataContext is PlaylistsViewModel viewModel)
        {
            viewModel.EnqueueEntry(entry);
        }
    }

    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item
            && item.DataContext is PlaylistsViewModel.EntryVm entry
            && DataContext is PlaylistsViewModel viewModel)
        {
            viewModel.RemoveEntryAt(entry);
        }
    }
}
