namespace Aria.App.Views;

using Aria.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

public partial class QueueColumn : UserControl
{
    public QueueColumn()
    {
        InitializeComponent();
    }

    public ListBox QueueListBox => QueueList;

    public void ScrollToSelected()
    {
        if (QueueList.SelectedItem is not null)
        {
            QueueList.ScrollIntoView(QueueList.SelectedItem);
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
