namespace Aria.App.Views;

using Aria.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Interactivity;

public partial class RailPlaylists : UserControl
{
    public RailPlaylists()
    {
        InitializeComponent();
    }

    private void OnRailDragOver(object? sender, DragEventArgs e) => e.DragEffects = DragDropEffects.Copy;

    private async void OnRailDropped(object? sender, DragEventArgs e)
    {
        if (DataContext is not PlaylistsViewModel viewModel)
        {
            return;
        }
        if (viewModel.AudioImport is null && App.Host is { } host)
        {
            viewModel.AudioImport = host.ImportTracksAsync;
        }
        var files = e.DataTransfer.TryGetFiles();
        if (files is null)
        {
            return;
        }
        var paths = new List<string>();
        foreach (var file in files)
        {
            if (file.TryGetLocalPath() is { } path)
            {
                paths.Add(path);
            }
        }
        if (paths.Count > 0)
        {
            await viewModel.ImportDroppedPathsAsync(paths);
        }
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item
            && item.DataContext is PlaylistsViewModel.PlaylistVm playlist
            && DataContext is PlaylistsViewModel viewModel)
        {
            viewModel.DeletePlaylistAt(playlist);
        }
    }
}
