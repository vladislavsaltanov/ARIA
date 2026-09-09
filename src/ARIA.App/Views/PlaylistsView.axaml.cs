namespace Aria.App.Views;

using Aria.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

public partial class PlaylistsView : UserControl
{
    public PlaylistsView()
    {
        InitializeComponent();
    }

    private void OnSetEntryName(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PlaylistsViewModel viewModel || EntryNameBox.Text is not { } name)
        {
            return;
        }
        viewModel.SetEntryNameCommand.Execute(name);
    }
}
