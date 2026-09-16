namespace Aria.App.Views;

using Aria.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Interactivity;

public partial class RailProjects : UserControl
{
    public RailProjects()
    {
        InitializeComponent();
    }

    private void OnRailDragOver(object? sender, DragEventArgs e) => e.DragEffects = DragDropEffects.Copy;

    private async void OnRailDropped(object? sender, DragEventArgs e)
    {
        if (DataContext is not ProjectsViewModel viewModel)
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
            && item.DataContext is ProjectsViewModel.ProjectVm project
            && DataContext is ProjectsViewModel viewModel)
        {
            viewModel.DeleteProjectAt(project);
        }
    }
}
