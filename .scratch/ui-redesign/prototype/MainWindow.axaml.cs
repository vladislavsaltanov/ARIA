using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace Aria.Prototype;

public sealed record DragTrack(string Source, string Name, string Duration, StreamGeometry Wave);

public partial class MainWindow : Window
{
    private Border? _dragSource;
    private Point _pressPos;
    private bool _dragging;
    private Border? _hint;

    public MainWindow()
    {
        InitializeComponent();
        UpdatePlayPauseTip(PlayPause.IsChecked == true);
        PlayPause.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty)
            {
                UpdatePlayPauseTip(PlayPause.IsChecked == true);
            }
        };
        Opened += (_, _) => SetWaveCursorFraction(0.4);
        AddHandler(KeyDownEvent, OnTunnelKey, RoutingStrategies.Tunnel);
    }

    private void OnTunnelKey(object? sender, KeyEventArgs e)
    {
        if (HelpOverlay.IsVisible)
        {
            e.Handled = true;
            if (e.Key is Key.F1 or Key.Escape)
            {
                CloseHelp();
            }
            return;
        }
        if (e.Key == Key.F1)
        {
            e.Handled = true;
            ShowHelp();
            return;
        }
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.T)
        {
            e.Handled = true;
            Drawer.IsPaneOpen = !Drawer.IsPaneOpen;
        }
    }

    private void UpdatePlayPauseTip(bool playing) =>
        ToolTip.SetTip(PlayPause, playing ? "ÃÂÃÂÃÂÃÂ°ÃÂÃÂÃÂÃÂ·ÃÂÃÂ° ÃÂ¢ÃÂÃÂ Esc" : "ÃÂÃÂÃÂÃÂ³ÃÂÃÂÃÂÃÂ°ÃÂÃÂÃÂÃÂ ÃÂ¢ÃÂÃÂ Space");

    private void OnHelpToggleClick(object? sender, RoutedEventArgs e) => ShowHelp();

    private void OnScenarioToggleClick(object? sender, RoutedEventArgs e) => Drawer.IsPaneOpen = !Drawer.IsPaneOpen;

    private void OnPaneCloseClick(object? sender, RoutedEventArgs e) => Drawer.IsPaneOpen = false;

    private void OnMuteClick(object? sender, RoutedEventArgs e)
    {
        bool muted = !MutedIcon.IsVisible;
        MutedIcon.IsVisible = muted;
        SoundIcon.IsVisible = !muted;
    }

    private void OnPlayPauseChanged(object? sender, RoutedEventArgs e) =>
        ToolTip.SetTip(PlayPause, PlayPause.IsChecked == true ? "ÃÂÃÂÃÂÃÂ°ÃÂÃÂÃÂÃÂ·ÃÂÃÂ° ÃÂ¢ÃÂÃÂ Esc" : "ÃÂÃÂÃÂÃÂ³ÃÂÃÂÃÂÃÂ°ÃÂÃÂÃÂÃÂ ÃÂ¢ÃÂÃÂ Space");

    private void OnHelpOverlayClick(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        CloseHelp();
    }

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e) => FocusSink.Focus();

    private void OnRowTap(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border row || row.Parent is not StackPanel stack)
        {
            return;
        }
        SelectRow(row, stack);
        TopLevel.GetTopLevel(this)?.FocusManager?.Focus(FocusSink, NavigationMethod.Unspecified, KeyModifiers.None);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragSource = row;
            _pressPos = e.GetPosition(this);
            _dragging = false;
            e.Pointer.Capture(row);
        }
        e.Handled = true;
    }

    private void OnRowMove(object? sender, PointerEventArgs e)
    {
        if (_dragSource is null || sender != _dragSource)
        {
            return;
        }
        var pos = e.GetPosition(this);
        if (!_dragging && (Math.Abs(pos.X - _pressPos.X) > 6 || Math.Abs(pos.Y - _pressPos.Y) > 6))
        {
            _dragging = true;
            _dragSource.Opacity = 0.45;
        }
        if (!_dragging)
        {
            return;
        }
        RemoveHint();
        var p = e.GetPosition(PlaylistStack);
        var q = e.GetPosition(QueuePanel);
        var overQueue = q.X >= 0 && q.Y >= 0 && q.X <= QueuePanel.Bounds.Width && q.Y <= QueuePanel.Bounds.Height;
        var overPlaylist = p.X >= 0 && p.Y >= 0 && p.X <= PlaylistStack.Bounds.Width && p.Y <= PlaylistStack.Bounds.Height;
        if (overQueue)
        {
            QueuePanel.Classes.Add("dropTarget");
        }
        else if (overPlaylist)
        {
            _hint = new Border
            {
                Classes = { "dropHint" },
                Height = 4,
                CornerRadius = new Avalonia.CornerRadius(2),
                Background = (IBrush)Application.Current!.Resources["BrushFg"]!,
                Opacity = 0.9,
                Margin = new Avalonia.Thickness(0, -2),
            };
            PlaylistStack.Children.Insert(Math.Clamp(DropIndex(p), 0, PlaylistStack.Children.Count), _hint);
        }
    }

    private void RemoveHint()
    {
        QueuePanel.Classes.Remove("dropTarget");
        if (_hint is { } hint && PlaylistStack.Children.Contains(hint))
        {
            PlaylistStack.Children.Remove(hint);
        }
        _hint = null;
    }

    private void OnRowRelease(object? sender, PointerReleasedEventArgs e)
    {
        var source = _dragSource;
        _dragSource = null;
        if (source is null)
        {
            return;
        }
        if (_dragging)
        {
            source.Opacity = 1;
        }
        _dragging = false;
        RemoveHint();
        var pos = e.GetPosition(this);
        var moved = Math.Abs(pos.X - _pressPos.X) > 6 || Math.Abs(pos.Y - _pressPos.Y) > 6;
        if (!moved)
        {
            return;
        }
        var q = e.GetPosition(QueuePanel);
        if (q.X >= 0 && q.Y >= 0 && q.X <= QueuePanel.Bounds.Width && q.Y <= QueuePanel.Bounds.Height)
        {
            var track = ExtractTrack(source);
            if (track is not null)
            {
                QueueStack.Children.Add(BuildQueueItem(track));
                StatusText.Text = "\u2192 \u043e\u0447\u0435\u0440\u0435\u0434\u044c: " + track.Name;
            }
            if (source.Classes.Contains("plRow") && source.Parent is StackPanel origin)
            {
                origin.Children.Remove(source);
                RenumberPlaylist();
            }
            e.Handled = true;
            return;
        }
        var p = e.GetPosition(PlaylistStack);
        if (p.X >= 0 && p.Y >= 0 && p.X <= PlaylistStack.Bounds.Width && p.Y <= PlaylistStack.Bounds.Height)
        {
            var index = Math.Clamp(DropIndex(p), 0, PlaylistStack.Children.Count);
            if (source.Classes.Contains("plRow") && source.Parent is StackPanel origin && origin == PlaylistStack)
            {
                var old = PlaylistStack.Children.IndexOf(source);
                PlaylistStack.Children.Remove(source);
                if (old < index)
                {
                    index--;
                }
                PlaylistStack.Children.Insert(Math.Clamp(index, 0, PlaylistStack.Children.Count), source);
                RenumberPlaylist();
                StatusText.Text = "\u2192 \u043f\u043e\u0437\u0438\u0446\u0438\u044f " + (index + 1) + ": " + ExtractTrack(source).Name;
            }
            else
            {
                var track = ExtractTrack(source);
                if (track is not null)
                {
                    PlaylistStack.Children.Insert(index, BuildPlaylistRow(track));
                    RenumberPlaylist();
                    StatusText.Text = "+ в плейлист позиция " + (index + 1) + ": " + track.Name;
                }
            }
        }
        e.Handled = true;
    }

    private static void SelectRow(Border row, StackPanel stack)
    {
        foreach (var child in stack.Children)
        {
            if (child is Border sibling)
            {
                sibling.Classes.Remove("selected");
            }
        }
        row.Classes.Add("selected");
    }

    private static DragTrack ExtractTrack(Border row)
    {
        if (row.Child is not Grid grid)
        {
            throw new InvalidOperationException("row layout");
        }
        var name = grid.Children.OfType<TextBlock>().First(t => Grid.GetColumn(t) == 1).Text ?? "";
        var duration = grid.Children.OfType<TextBlock>().Where(t => Grid.GetColumn(t) == 2)
            .Select(t => t.Text).FirstOrDefault(t => !string.IsNullOrEmpty(t)) ?? "";
        var wave = grid.Children.OfType<Path>().First().Data as StreamGeometry ?? new StreamGeometry();
        return new DragTrack(row.Classes.Contains("plRow") ? "pl" : "lib", name, duration, wave);
    }

    private void OnClearQueueClick(object? sender, RoutedEventArgs e)
    {
        while (QueueStack.Children.Count > 1)
        {
            QueueStack.Children.RemoveAt(QueueStack.Children.Count - 1);
        }
    }

    private int DropIndex(Point point)
    {
        int index = 0;
        foreach (var child in PlaylistStack.Children)
        {
            if (child is Border { Classes: var c } && c.Contains("dropHint"))
            {
                continue;
            }
            var center = child.Bounds.Y + child.Bounds.Height / 2;
            if (point.Y > center)
            {
                index++;
            }
        }
        return index;
    }

    private Border BuildPlaylistRow(DragTrack track)
    {
        var number = new TextBlock
        {
            Text = "00",
            FontFamily = (FontFamily)Application.Current!.Resources["MonoFont"]!,
            FontSize = 11,
            Foreground = (IBrush)Application.Current!.Resources["BrushDim"]!,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        var name = new TextBlock
        {
            Text = track.Name,
            FontSize = 13,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(8, 0, 0, 0),
        };
        var duration = new TextBlock
        {
            Text = track.Duration,
            FontFamily = (FontFamily)Application.Current!.Resources["MonoFont"]!,
            FontSize = 12,
            Foreground = (IBrush)Application.Current!.Resources["BrushDim"]!,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
        };
        var wave = new Path
        {
            Data = track.Wave,
            Stretch = Stretch.Fill,
            Width = 118,
            Height = 18,
            Fill = (IBrush)Application.Current!.Resources["BrushWave"]!,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(12, 0, 0, 0),
        };
        Grid.SetColumn(number, 0);
        Grid.SetColumn(name, 1);
        Grid.SetColumn(duration, 2);
        Grid.SetColumn(wave, 3);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*,58,130,30") };
        grid.Children.Add(number);
        grid.Children.Add(name);
        grid.Children.Add(duration);
        grid.Children.Add(wave);
        var border = new Border
        {
            Child = grid,
            Classes = { "plRow" },
            Height = 44,
            CornerRadius = new Avalonia.CornerRadius(6),
            Padding = new Avalonia.Thickness(10, 0, 12, 0),
            Background = Brushes.Transparent,
        };
        border.PointerPressed += OnRowTap;
        border.PointerMoved += OnRowMove;
        border.PointerReleased += OnRowRelease;
        return border;
    }

    private Border BuildQueueItem(DragTrack track)
    {
        var name = new TextBlock
        {
            Text = track.Name,
            FontSize = 12,
            Foreground = (IBrush)Application.Current!.Resources["BrushDim"]!,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(8, 0, 0, 0),
        };
        var duration = new TextBlock
        {
            Text = track.Duration,
            FontFamily = (FontFamily)Application.Current!.Resources["MonoFont"]!,
            FontSize = 10,
            Foreground = (IBrush)Application.Current!.Resources["BrushFaint"]!,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        Grid.SetColumn(name, 1);
        Grid.SetColumn(duration, 2);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("18,*,36") };
        grid.Children.Add(name);
        grid.Children.Add(duration);
        return new Border
        {
            Child = grid,
            Height = 36,
            CornerRadius = new Avalonia.CornerRadius(6),
            Padding = new Avalonia.Thickness(10, 0),
        };
    }

    private void RenumberPlaylist()
    {
        int i = 1;
        foreach (var child in PlaylistStack.Children.OfType<Border>())
        {
            if (child.Classes.Contains("dropHint"))
            {
                continue;
            }
            var grid = (Grid)child.Child!;
            var number = grid.Children.OfType<TextBlock>().First(t => Grid.GetColumn(t) == 0);
            number.Text = i.ToString("00");
            i++;
        }
    }

    private void OnWaveSeekPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetPosition(WaveSeek);
        var fraction = WaveSeek.Bounds.Width <= 0 ? 0 : Math.Clamp(point.X / WaveSeek.Bounds.Width, 0, 1);
        SetWaveCursorFraction(fraction);
        e.Handled = true;
    }

    private void SetWaveCursorFraction(double fraction) =>
        WaveCursor.RenderTransform = new TranslateTransform(fraction * WaveSeek.Bounds.Width - 1, 0);

    private void ShowHelp()
    {
        HelpOverlay.IsVisible = true;
        HelpOverlay.Focus();
    }

    private void CloseHelp() => HelpOverlay.IsVisible = false;
}
