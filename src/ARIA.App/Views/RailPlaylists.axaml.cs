namespace Aria.App.Views;

using Aria.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

public partial class RailPlaylists : UserControl
{
    public RailPlaylists()
    {
        InitializeComponent();
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
