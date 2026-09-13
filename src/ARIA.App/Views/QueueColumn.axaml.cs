namespace Aria.App.Views;

using Aria.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

public partial class QueueColumn : UserControl
{
    public QueueColumn()
    {
        InitializeComponent();
    }

    public ListBox QueueListBox => QueueList;

    public event EventHandler? CloseRequested;

    public void ScrollToSelected()
    {
        if (QueueList.SelectedItem is not null)
        {
            QueueList.ScrollIntoView(QueueList.SelectedItem);
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox list
            && list.SelectedItem is QueueViewModel.QueueItemVm row
            && DataContext is QueueViewModel viewModel)
        {
            viewModel.PlayItem(row);
        }
    }

    private void OnPlayNowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item
            && item.DataContext is QueueViewModel.QueueItemVm row
            && DataContext is QueueViewModel viewModel)
        {
            viewModel.PlayItem(row);
        }
    }

    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item
            && item.DataContext is QueueViewModel.QueueItemVm row
            && DataContext is QueueViewModel viewModel)
        {
            viewModel.RemoveItem(row);
        }
    }
}
