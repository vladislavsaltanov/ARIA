namespace Aria.App.Views;

using Aria.App.ViewModels;
using Aria.Core.Model;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

public partial class ScriptPanel : UserControl
{
    private ScriptPanelViewModel.ScriptLineVm? _pressLine;
    private Point _pressPoint;
    private bool _dragging;
    private bool _suppressTap;

    public ScriptPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        LinesControl.PointerPressed += OnLinesPointerPressed;
        LinesControl.PointerMoved += OnLinesPointerMoved;
        LinesControl.PointerReleased += OnLinesPointerReleased;
    }

    private ScriptPanelViewModel? _bound;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_bound is not null)
        {
            _bound.ScriptImportFailed -= OnScriptImportFailed;
        }
        _bound = DataContext as ScriptPanelViewModel;
        if (_bound is not null)
        {
            _bound.ScriptImportFailed += OnScriptImportFailed;
        }
    }

    private async void OnScriptImportFailed(string message)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }
        var dialog = new Window
        {
            Title = "Импорт сценария не удался",
            Width = 460,
            MinWidth = 380,
            MinHeight = 140,
            MaxWidth = 640,
            SizeToContent = SizeToContent.Height,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Spacing = 12,
                Margin = new Thickness(16),
                Children =
                {
                    new ScrollViewer
                    {
                        MaxHeight = 320,
                        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                        Content = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    },
                    new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right },
                },
            },
        };
        ((Button)((StackPanel)dialog.Content!).Children[1]).Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(owner);
    }

    public event EventHandler? CloseRequested;

    public void CommitOpenEdit()    {
        if (DataContext is ScriptPanelViewModel viewModel)
        {
            foreach (var line in viewModel.Lines)
            {
                line.SuggestionsVisible = false;
            }
            viewModel.CommitOpenEdit();
        }
    }

    private ScriptPanelViewModel? ViewModel => DataContext as ScriptPanelViewModel;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void OnNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || ViewModel is not { } viewModel)
        {
            return;
        }
        if (e.Key == Key.Enter)
        {
            viewModel.CommitOpenEdit();
            viewModel.RenameSelected(box.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            box.Text = viewModel.SelectedScript?.Name ?? string.Empty;
            e.Handled = true;
        }
    }

    private void OnNameCommit(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox box && ViewModel is { } viewModel)
        {
            viewModel.CommitOpenEdit();
            viewModel.RenameSelected(box.Text);
        }
    }

    private void OnTabSelection(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox)
        {
            return;
        }
        if (sender is ListBox { Selection.SelectedItem: ScriptPanelViewModel.ScriptVm current })
        {
            SelectTab(current);
        }
    }

    private void SelectTab(ScriptPanelViewModel.ScriptVm? script)
    {
        if (script is null || ViewModel is not { } viewModel)
        {
            return;
        }
        if (viewModel.SelectedScript?.Id == script.Id)
        {
            return;
        }
        viewModel.SelectScript(script);
    }

    private void OnPanelTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Button)
        {
            return;
        }
        if (LineOf(e.Source) is null)
        {
            ViewModel?.CommitOpenEdit();
        }
    }

    private void OnRowHover(object? sender, PointerEventArgs e)
    {
        if (sender is Control row)
        {
            SetHoverHint(row, e.RoutedEvent == InputElement.PointerEnteredEvent);
        }
    }

    private static void SetHoverHint(Control row, bool hover)
    {
        if (FindDescendant(row, "ScriptGo") is { } go)
        {
            go.IsVisible = hover;
        }
        if (FindDescendant(row, "ScriptTime") is { } time)
        {
            time.IsVisible = !hover;
        }
    }

    private static bool IsInside<T>(object? source) where T : Visual
    {
        var current = source as Visual;
        while (current is not null)
        {
            if (current is T)
            {
                return true;
            }
            current = current.GetVisualParent();
        }
        return false;
    }

    private static Control? FindDescendant(Control root, string tag)
    {
        if (root.Tag as string == tag)
        {
            return root;
        }
        foreach (var child in root.GetVisualChildren())
        {
            if (child is Control control && FindDescendant(control, tag) is { } found)
            {
                return found;
            }
        }
        return null;
    }

    private void OnLineTapped(object? sender, TappedEventArgs e)    {
        if (_suppressTap)
        {
            _suppressTap = false;
            return;
        }
        if (IsInside<Button>(e.Source) || IsInside<TextBox>(e.Source))
        {
            return;
        }
        var line = LineOf(e.Source);
        if (line is null || ViewModel is not { } viewModel)
        {
            return;
        }
        if (line.IsEditing)
        {
            return;
        }
        if ((e.Source as Control)?.Tag as string == "ScriptText")
        {
            viewModel.BeginEdit(line);
            return;
        }
        viewModel.ExecuteLine(line);
    }

    private void OnExecuteClick(object? sender, RoutedEventArgs e)
    {
        var line = LineOf(sender);
        if (line is not null)
        {
            ViewModel?.ExecuteLine(line);
        }
    }

    private void OnChipClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ScriptPanelViewModel.MentionVm mention })
        {
            ViewModel?.ExecuteMention(mention);
        }
    }

    private void OnChipEnter(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: ScriptPanelViewModel.MentionVm mention }
            && ViewModel is { } viewModel)
        {
            viewModel.HighlightedTrack = mention.Track;
        }
    }

    private void OnChipLeave(object? sender, PointerEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.HighlightedTrack = null;
        }
    }

    private void OnCandidateClick(object? sender, RoutedEventArgs e)
    {
        var line = LineOf(sender);
        if (line is not null
            && sender is Control { DataContext: ScriptPanelViewModel.MentionVm mention })
        {
            ViewModel?.ChooseCandidate(line, mention);
        }
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        var line = LineOf(sender);
        if (line is not null)
        {
            ViewModel?.RemoveLine(line);
        }
    }

    private void OnMarkNowClick(object? sender, RoutedEventArgs e)
    {
        var line = LineOf(sender);
        if (line is not null)
        {
            ViewModel?.MarkNow(line);
        }
    }

    private void OnEditKeyDown(object? sender, KeyEventArgs e)
    {
        var line = LineOf(sender);
        if (line is null || ViewModel is not { } viewModel)
        {
            return;
        }
        if (e.Key == Key.Enter)
        {
            viewModel.CommitEdit(line);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            viewModel.CancelEdit(line);
            e.Handled = true;
        }
    }

    private void OnEditTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox box || ViewModel is not { } viewModel)
        {
            return;
        }
        var line = LineOf(sender);
        if (line is null || !line.IsEditing)
        {
            return;
        }
        var text = box.Text ?? string.Empty;
        var at = text.LastIndexOf('@');
        if (at < 0)
        {
            viewModel.CloseSuggestions(line);
            return;
        }
        var end = text.IndexOfAny([' ', '\n', '\t'], at);
        var query = end < 0 ? text[(at + 1)..] : text.Substring(at + 1, end - at - 1);
        viewModel.RefreshSuggestions(line, query);
    }

    private void OnStagedClick(object? sender, RoutedEventArgs e)
    {
        var line = LineOf(sender);
        if (line is not null
            && sender is Control { DataContext: ScriptPanelViewModel.MentionVm mention })
        {
            ViewModel?.RemoveStagedMention(line, mention);
        }
    }

    private void OnSuggestClick(object? sender, RoutedEventArgs e)
    {
        var line = LineOf(sender);
        if (line is null
            || sender is not Control { DataContext: Track track }
            || ViewModel is not { } viewModel)
        {
            return;
        }
        var text = line.EditText;
        var at = text.LastIndexOf('@');
        if (at >= 0)
        {
            var end = text.IndexOfAny([' ', '\n', '\t'], at);
            line.EditText = (end < 0 ? text[..at] : text[..at] + text[end..]).TrimEnd();
        }
        viewModel.InsertMention(line, track.Id);
        viewModel.CloseSuggestions(line);
    }

    private void OnLinesPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _suppressTap = false;
        if (e.Source is Button or TextBox || LineOf(e.Source) is not { } line)
        {
            _pressLine = null;
            return;
        }
        _pressLine = line;
        _pressPoint = e.GetPosition(LinesControl);
        _dragging = false;
    }

    private void OnLinesPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressLine is null || ViewModel is null)
        {
            return;
        }
        var point = e.GetPosition(LinesControl);
        if (!_dragging)
        {
            if (Math.Abs(point.Y - _pressPoint.Y) < 6)
            {
                return;
            }
            _dragging = true;
        }
        var insertion = InsertionIndex(point.Y);
        var rowTop = RowTop(insertion);
        DropLine.Width = Math.Max(0, DropIndicator.Bounds.Width - 4);
        Canvas.SetLeft(DropLine, 2);
        Canvas.SetTop(DropLine, rowTop - 5);
        DropIndicator.IsVisible = true;
        e.Handled = true;
    }

    private void OnLinesPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging && _pressLine is not null && ViewModel is { } viewModel)
        {
            var from = viewModel.Lines.IndexOf(_pressLine);
            var insertion = InsertionIndex(e.GetPosition(LinesControl).Y);
            viewModel.MoveLine(from, insertion);
            _suppressTap = true;
        }
        _dragging = false;
        _pressLine = null;
        DropIndicator.IsVisible = false;
    }

    private int InsertionIndex(double y)
    {
        if (ViewModel is null)
        {
            return 0;
        }
        var insertion = 0;
        foreach (var (top, height) in ContentRows())
        {
            if (top + height / 2 <= y)
            {
                insertion++;
            }
        }
        return Math.Min(insertion, ViewModel.Lines.Count - 1);
    }

    private double RowTop(int insertion)
    {
        var rows = ContentRows();
        if (insertion < rows.Count)
        {
            return rows[insertion].Top;
        }
        if (rows.Count > 0)
        {
            return rows[^1].Top + rows[^1].Height + 8;
        }
        return 0;
    }

    private List<(double Top, double Height)> ContentRows()
    {
        var rows = new List<(double Top, double Height)>();
        foreach (var row in RowContainers())
        {
            if (row.DataContext is ScriptPanelViewModel.ScriptLineVm line && line != _pressLine)
            {
                var top = row.TranslatePoint(new Point(0, 0), DropIndicator);
                if (top is { } origin)
                {
                    rows.Add((origin.Y, row.Bounds.Height));
                }
            }
        }
        rows.Sort();
        return rows;
    }

    private IEnumerable<Control> RowContainers()
    {
        if (LinesControl.Presenter?.Panel is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                yield return child;
            }
        }
    }

    private static ScriptPanelViewModel.ScriptLineVm? LineOf(object? source)
    {
        var current = source as Visual;
        while (current is not null)
        {
            if (current is Control { DataContext: ScriptPanelViewModel.ScriptLineVm line })
            {
                return line;
            }
            current = current.GetVisualParent();
        }
        return null;
    }
}
