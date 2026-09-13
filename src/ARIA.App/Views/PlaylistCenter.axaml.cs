namespace Aria.App.Views;

using Aria.App.Services;
using Aria.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

public partial class PlaylistCenter : UserControl
{
    public PlaylistCenter()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private PlaylistsViewModel? _bound;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_bound is not null)
        {
            _bound.ImportFailed -= OnImportFailed;
            _bound.ExportSucceeded -= OnExportSucceeded;
        }
        _bound = DataContext as PlaylistsViewModel;
        if (_bound is not null)
        {
            _bound.ImportFailed += OnImportFailed;
            _bound.ExportSucceeded += OnExportSucceeded;
        }
    }

    private async void OnImportFailed(string message)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }
        var dialog = new Window
        {
            Title = "Импорт плейлиста не удался",
            Width = 420,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Spacing = 12,
                Margin = new Thickness(16),
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right },
                },
            },
        };
        ((Button)((StackPanel)dialog.Content!).Children[1]).Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(owner);
    }

    private async void OnExportSucceeded(string fileName, string message)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }
        var dialog = new Window
        {
            Title = "Плейлист экспортирован",
            Width = 420,
            Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Spacing = 12,
                Margin = new Thickness(16),
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right },
                },
            },
        };
        ((Button)((StackPanel)dialog.Content!).Children[1]).Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(owner);
    }

    public event EventHandler? ScenarioToggleRequested;

    public event EventHandler? HelpRequested;

    public ListBox EntryListBox => EntryList;

    public void ApplyGestures(HotkeyService hotkeys) =>
        ToolTip.SetTip(ScenarioButton, HotkeyLabels.Tip(HotkeyLabels.Label("toggle-script"), hotkeys.GestureFor("toggle-script")));

    private void OnScenarioClick(object? sender, RoutedEventArgs e) => ScenarioToggleRequested?.Invoke(this, EventArgs.Empty);

    private void OnHelpClick(object? sender, RoutedEventArgs e) => HelpRequested?.Invoke(this, EventArgs.Empty);

    private void OnDeletePlaylistClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PlaylistsViewModel viewModel)
        {
            viewModel.DeletePlaylistCommand.Execute(null);
        }
    }

    private void OnEntryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox list
            && list.SelectedItem is PlaylistsViewModel.EntryVm entry
            && DataContext is PlaylistsViewModel viewModel)
        {
            viewModel.PlayEntry(entry);
        }
    }

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
