namespace Aria.App.Views;

using Aria.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

internal sealed class DragCoordinator
{
    private readonly ListBox _project;
    private readonly ListBox _queue;
    private ListBox? _source;
    private Point _pressPos;
    private bool _dragging;
    private ProjectsViewModel.EntryVm? _entry;
    private QueueViewModel.QueueItemVm? _queueItem;
    private Border? _rowHighlight;
    private ListBox? _listHighlight;
    private ListBoxItem? _dropContainer;
    private bool _dropAfter;

    public DragCoordinator(ListBox project, ListBox queue)
    {
        _project = project;
        _queue = queue;
        project.AddHandler(InputElement.PointerPressedEvent, OnPress, RoutingStrategies.Bubble, handledEventsToo: true);
        project.AddHandler(InputElement.PointerMovedEvent, OnMove, RoutingStrategies.Bubble, handledEventsToo: true);
        project.AddHandler(InputElement.PointerReleasedEvent, OnRelease, RoutingStrategies.Bubble, handledEventsToo: true);
        queue.AddHandler(InputElement.PointerPressedEvent, OnPress, RoutingStrategies.Bubble, handledEventsToo: true);
        queue.AddHandler(InputElement.PointerMovedEvent, OnMove, RoutingStrategies.Bubble, handledEventsToo: true);
        queue.AddHandler(InputElement.PointerReleasedEvent, OnRelease, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void OnPress(object? sender, PointerPressedEventArgs e)
    {
        Clear();
        if (sender is not ListBox list)
        {
            return;
        }
        var row = RowAt(list, e);
        if (row is null || !e.GetCurrentPoint(list).Properties.IsLeftButtonPressed)
        {
            return;
        }
        if (ReferenceEquals(list, _project) && row is ProjectsViewModel.EntryVm entry)
        {
            _entry = entry;
        }
        else if (ReferenceEquals(list, _queue) && row is QueueViewModel.QueueItemVm item)
        {
            _queueItem = item;
        }
        else
        {
            return;
        }
        _source = list;
        _pressPos = e.GetPosition(list);
        e.Pointer.Capture(list);
    }

    private void OnMove(object? sender, PointerEventArgs e)
    {
        if (_source is null || sender != _source)
        {
            return;
        }
        var pos = e.GetPosition(_source);
        if (!_dragging && (Math.Abs(pos.X - _pressPos.X) > 6 || Math.Abs(pos.Y - _pressPos.Y) > 6))
        {
            _dragging = true;
        }
        if (!_dragging)
        {
            return;
        }
        ClearHighlight();
        if (_entry is not null)
        {
            ShowInsertion(_project, e.GetPosition(_project));
        }
        else if (_queueItem is not null)
        {
            HighlightAt(_queue, e.GetPosition(_queue));
        }
    }

    private void OnRelease(object? sender, PointerReleasedEventArgs e)
    {
        var source = _source;
        var entry = _entry;
        var queueItem = _queueItem;
        var moved = _dragging;
        Clear();
        if (source is null || !moved)
        {
            return;
        }
        if (ReferenceEquals(source, _project) && entry is not null
            && source.DataContext is ProjectsViewModel projects
            && IsInside(_project, e.GetPosition(_project)))
        {
            var visible = projects.VisibleEntries;
            var visual = DropIndex(_project, e.GetPosition(_project), visible.Count);
            var model = visual >= visible.Count
                ? projects.SelectedProject?.Entries.Count ?? 0
                : projects.EntryIndex(visible[visual].Id);
            var old = projects.EntryIndex(entry.Id);
            if (old >= 0 && model >= 0)
            {
                var to = old < model ? model - 1 : model;
                if (to != old)
                {
                    projects.MoveEntry(entry.Id, to);
                }
            }
        }
        else if (ReferenceEquals(source, _queue) && queueItem is not null
            && source.DataContext is QueueViewModel queue
            && IsInside(_queue, e.GetPosition(_queue)))
        {
            var from = queue.Items.IndexOf(queueItem);
            var visual = DropIndex(_queue, e.GetPosition(_queue), queue.Items.Count);
            if (from >= 0)
            {
                var to = from < visual ? visual - 1 : visual;
                if (to != from)
                {
                    queue.MoveItem(from, to);
                }
            }
        }
        e.Handled = true;
    }

    public void Reset() => Clear();

    private void Clear()
    {
        _source = null;
        _dragging = false;
        _entry = null;
        _queueItem = null;
        ClearHighlight();
    }

    private void ClearHighlight()
    {
        if (_dropContainer is not null)
        {
            _dropContainer.Classes.Remove("dropBefore");
            _dropContainer.Classes.Remove("dropAfter");
            _dropContainer = null;
        }
        if (_rowHighlight is not null)
        {
            _rowHighlight.Background = Brushes.Transparent;
            _rowHighlight = null;
        }
        if (_listHighlight is not null)
        {
            _listHighlight.Background = Brushes.Transparent;
            _listHighlight = null;
        }
    }

    private void ShowInsertion(ListBox list, Point pos)
    {
        if (!IsInside(list, pos))
        {
            return;
        }
        var count = list.Items.Count;
        var index = DropIndex(list, pos, count);
        ListBoxItem? container = null;
        var after = false;
        if (count > 0 && list.ItemsPanelRoot is Panel panel)
        {
            var items = panel.Children.OfType<ListBoxItem>().ToList();
            if (index >= count && items.Count > 0)
            {
                container = items[^1];
                after = true;
            }
            else if (index < items.Count)
            {
                container = items[index];
            }
        }
        if (container is null)
        {
            if (_listHighlight == list)
            {
                return;
            }
            ClearHighlight();
            list.Background = new SolidColorBrush(Color.Parse("#2A2A2A"));
            _listHighlight = list;
            return;
        }
        if (_dropContainer == container && _dropAfter == after)
        {
            return;
        }
        ClearHighlight();
        container.Classes.Add(after ? "dropAfter" : "dropBefore");
        _dropContainer = container;
        _dropAfter = after;
    }

    private void HighlightAt(ListBox list, Point pos)
    {
        var container = ContainerAt(list, pos);
        var border = container?.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(b => b.Classes.Contains("plRow") || b.Classes.Contains("qRow"));
        if (border is not null)
        {
            if (_rowHighlight == border)
            {
                return;
            }
            ClearHighlight();
            border.Background = new SolidColorBrush(Color.Parse("#2A2A2A"));
            _rowHighlight = border;
            return;
        }
        if (_listHighlight == list)
        {
            return;
        }
        ClearHighlight();
        list.Background = new SolidColorBrush(Color.Parse("#2A2A2A"));
        _listHighlight = list;
    }

    private static object? RowAt(ListBox list, PointerPressedEventArgs e)
    {
        var container = ContainerAt(list, e.GetPosition(list));
        if (container?.DataContext is { } item
            && (item is ProjectsViewModel.EntryVm || item is QueueViewModel.QueueItemVm))
        {
            return item;
        }
        return null;
    }

    private static ListBoxItem? ContainerAt(ListBox list, Point pos)
    {
        if (list.ItemsPanelRoot is not Panel panel)
        {
            return null;
        }
        foreach (var child in panel.Children.OfType<ListBoxItem>())
        {
            var bounds = child.Bounds;
            var origin = child.TranslatePoint(new Point(0, 0), panel);
            if (origin is { } o
                && pos.X >= o.X && pos.X <= o.X + bounds.Width
                && pos.Y >= o.Y && pos.Y <= o.Y + bounds.Height)
            {
                return child;
            }
        }
        return null;
    }

    private static int DropIndex(ListBox list, Point pos, int count)
    {
        if (list.ItemsPanelRoot is not Panel panel)
        {
            return count;
        }
        var index = 0;
        foreach (var child in panel.Children.OfType<ListBoxItem>())
        {
            var bounds = child.Bounds;
            var origin = child.TranslatePoint(new Point(0, 0), panel);
            if (origin is { } o && pos.Y > o.Y + bounds.Height / 2)
            {
                index++;
            }
        }
        return Math.Min(index, count);
    }

    private static bool IsInside(ListBox list, Point pos) =>
        pos.X >= 0 && pos.Y >= 0 && pos.X <= list.Bounds.Width && pos.Y <= list.Bounds.Height;
}
