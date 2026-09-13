namespace Aria.App.Views;

using Aria.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

public partial class LibrarySection : UserControl
{
    public LibrarySection()
    {
        InitializeComponent();
    }

    public ListBox TrackListBox => TrackList;

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is LibraryViewModel viewModel)
        {
            var rows = TrackList.SelectedItems?.OfType<LibraryViewModel.TrackVm>().ToList() ?? [];
            if (rows.Count > 0)
            {
                viewModel.EnqueueTracks(rows);
                viewModel.Play();
            }
        }
    }

    private void OnEnqueueClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item
            && item.DataContext is LibraryViewModel.TrackVm track
            && DataContext is LibraryViewModel viewModel)
        {
            viewModel.EnqueueTrack(track);
        }
    }

    private void OnRevealClick(object? sender, RoutedEventArgs e)
    {
    }

    private async void OnFilesDropped(object? sender, DragEventArgs e)
    {
        if (DataContext is not LibraryViewModel viewModel)
        {
            return;
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
            await viewModel.ImportAsync(paths);
        }
    }
}
