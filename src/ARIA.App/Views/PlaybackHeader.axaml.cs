namespace Aria.App.Views;

using System.ComponentModel;
using System.Collections.Immutable;
using Aria.App.Services;
using Aria.App.ViewModels;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.State;
using Aria.Persistence;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using System.Linq;
using Avalonia.Threading;

public partial class PlaybackHeader : UserControl
{
    private const double MarkerTimeRowHeight = 14.0;
    private const double MarkerWaveHeight = 56.0;
    private const double MarkerTextMaxWidth = 140.0;
    private const double MarkerEditorMaxWidth = 200.0;
    private const double MarkerEditorMinWidth = 80.0;
    private const string MarkerHint = "ЛКМ — перейти · 2×ЛКМ — переименовать · ПКМ — удалить · ПКМ+протяжка — сдвинуть · навести — действие";
    private const double MarkerActionDelayMs = 500.0;
    private const double MarkerCloseDelayMs = 150.0;

    private static readonly IBrush WaveBrush = new ImmutableSolidColorBrush(Color.Parse("#ECECEC"));
    private static readonly IBrush MarkerBrush = new ImmutableSolidColorBrush(Color.Parse("#8A8A8A"));
    private static readonly IBrush MarkerHoverBrush = new ImmutableSolidColorBrush(Color.Parse("#ECECEC"));

    private IWaveformStore? _waveforms;
    private PlaybackMonitor? _monitor;
    private TransportViewModel? _viewModel;
    private PositionSnapshot? _latest;
    private TrackId? _currentTrackId;
    private WaveformPeaks? _peaks;
    private bool _dragging;
    private TimeSpan _dragTarget;
    private Marker? _pendingMarker;
    private Marker? _hoverMarker;
    private DispatcherTimer? _hoverTimer;
    private Marker? _hoverTimerMarker;
    private DispatcherTimer? _closeTimer;
    private Popup? _actionPopup;
    private Marker? _actionMarker;
    private IBrush _actionHoverBrush = new ImmutableSolidColorBrush(Color.Parse("#1F1F1F"));

    internal bool IsActionPopupOpen => _actionPopup?.IsOpen == true;

    internal Control? ActionPopupContent => _actionPopup?.Child;
    private static readonly MarkerAction[] MarkerActions = [MarkerAction.None, MarkerAction.Pause, MarkerAction.RepeatFromPrevious, MarkerAction.Restart];
    private Marker? _dragMarker;
    private bool _markerDragging;
    private Point _markerPressPoint;
    private TimeSpan _markerDragTarget;
    private TextBox? _editor;
    private Marker? _editorMarker;

    public PlaybackHeader()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        WaveformBorder.PointerPressed += OnWavePressed;
        WaveformBorder.PointerMoved += OnWaveMoved;
        WaveformBorder.PointerExited += OnWaveExited;
        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(MarkerActionDelayMs) };
        _hoverTimer.Tick += OnHoverTimerTick;
        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(MarkerCloseDelayMs) };
        _closeTimer.Tick += OnCloseTimerTick;
        WaveformBorder.PointerReleased += OnWaveReleased;
        WaveformBorder.PointerCaptureLost += OnWaveCaptureLost;
        WaveformCanvas.SizeChanged += OnWaveCanvasSizeChanged;
        MarkerCanvas.SizeChanged += OnWaveCanvasSizeChanged;
    }

    public void Attach(PlaybackMonitor monitor, IWaveformStore? waveforms)
    {
        _monitor = monitor;
        _waveforms = waveforms;
        if (_peaks is null)
        {
            _currentTrackId = null;
        }
        monitor.Changed += OnPosition;
        monitor.Cleared += OnMonitorCleared;
        if (monitor.Latest is { } latest)
        {
            Apply(latest);
        }
        else if (_viewModel?.CurrentTrackId is { } trackId)
        {
            UpdateForTrack(trackId);
        }
    }

    private void OnMonitorCleared() =>
        Avalonia.Threading.Dispatcher.UIThread.Post(HidePlayhead);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
        _viewModel = DataContext as TransportViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            if (_latest is null)
            {
                UpdateForTrack(_viewModel.CurrentTrackId);
            }
        }
        DrawMarkers();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TransportViewModel.Markers) or nameof(TransportViewModel.CurrentDuration))
        {
            DrawMarkers();
            return;
        }
        if (e.PropertyName == nameof(TransportViewModel.CurrentTrackId)
            && sender is TransportViewModel viewModel)
        {
            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            {
                UpdateForTrack(viewModel.CurrentTrackId);
            }
            else
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (ReferenceEquals(_viewModel, viewModel))
                    {
                        UpdateForTrack(viewModel.CurrentTrackId);
                    }
                });
            }
        }
    }

    private void OnPosition(PositionSnapshot snapshot)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Apply(snapshot));
    }

    private void Apply(PositionSnapshot snapshot)
    {
        _latest = snapshot;
        UpdateForTrack(snapshot.Deck.TrackId);
        UpdateCursor(snapshot);
    }

    private void UpdateForTrack(TrackId? trackId)
    {
        if (_currentTrackId == trackId && _peaks is not null)
        {
            return;
        }
        _currentTrackId = trackId;
        _peaks = trackId is null ? null : _waveforms?.Load(trackId.Value);
        if (_peaks is not null)
        {
            WaveCursor.IsVisible = true;
        }
        else
        {
            HidePlayhead();
        }
        DrawWaveform();
    }

    private void UpdateCursor(PositionSnapshot snapshot)
    {
        if (_peaks is null || WaveformCanvas.Bounds.Width <= 0 || _dragging)
        {
            return;
        }
        SetPlayhead(FileFraction(snapshot.Deck, snapshot.FilePosition));
    }

    private void SetPlayhead(double fraction)
    {
        var width = WaveformCanvas.Bounds.Width;
        var at = Math.Clamp(fraction, 0.0, 1.0) * width;
        Canvas.SetLeft(WaveCursor, at);
        WaveCursor.IsVisible = true;
        WavePlayed.Width = at;
        Canvas.SetLeft(WaveRemain, at);
        WaveRemain.Width = Math.Max(0.0, width - at);
        WavePlayed.IsVisible = true;
        WaveRemain.IsVisible = true;
    }

    private void HidePlayhead()
    {
        WaveCursor.IsVisible = false;
        WavePlayed.IsVisible = false;
        WaveRemain.IsVisible = false;
    }

    private void DrawWaveform()
    {
        for (var index = WaveformCanvas.Children.Count - 1; index >= 0; index--)
        {
            if (WaveformCanvas.Children[index] is Path)
            {
                WaveformCanvas.Children.RemoveAt(index);
            }
        }
        if (_peaks is null)
        {
            return;
        }
        var width = WaveformCanvas.Bounds.Width;
        var height = WaveformCanvas.Bounds.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }
        var points = _peaks.Points;
        var count = points.Length;
        if (count == 0)
        {
            return;
        }
        var step = width / count;
        var center = height / 2.0;
        var scale = center * 0.9;

        WaveformCanvas.Children.Add(WavePath(BuildHalf(points, step, center, scale, useMax: true)));
        WaveformCanvas.Children.Add(WavePath(BuildHalf(points, step, center, scale, useMax: false)));
    }

    private static StreamGeometry BuildHalf(ImmutableArray<PeakPoint> points, double step, double center, double scale, bool useMax)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(0, center), true);
            for (var index = 0; index < points.Length; index++)
            {
                var peak = useMax ? points[index].Max : points[index].Min;
                context.LineTo(new Point(index * step, center - peak * scale));
            }
            context.LineTo(new Point(points.Length * step, center));
        }
        return geometry;
    }

    private static Path WavePath(StreamGeometry geometry) => new()
    {
        Data = geometry,
        Fill = WaveBrush,
        Opacity = 0.6,
    };

    private void OnWaveCanvasSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        DrawWaveform();
        DrawMarkers();
        if (_latest is { } snapshot)
        {
            UpdateCursor(snapshot);
        }
    }

    private void OnWavePressed(object? sender, PointerPressedEventArgs e)
    {
        _hoverTimer?.Stop();
        _hoverTimerMarker = null;
        CloseActionPopup();
        if (_peaks is null || _latest is null || _viewModel is null)
        {
            return;
        }
        var point = e.GetPosition(WaveformBorder);
        var properties = e.GetCurrentPoint(WaveformBorder).Properties;
        var hit = MarkerAt(point.X, VisibleMarkers());
        if (properties.IsRightButtonPressed)
        {
            e.Handled = true;
            if (hit is { } existing)
            {
                _dragMarker = existing;
                _markerDragging = false;
                _markerPressPoint = point;
                _markerDragTarget = existing.Position;
                e.Pointer.Capture(WaveformBorder);
            }
            else
            {
                BeginMarker(point.X);
            }
            return;
        }
        if (!properties.IsLeftButtonPressed)
        {
            return;
        }
        if (hit is { } marker)
        {
            e.Handled = true;
            if (e.ClickCount >= 2)
            {
                OpenEditor(marker);
            }
            else
            {
                _viewModel.SeekToFilePosition(marker.Position);
            }
            return;
        }
        _dragging = true;
        e.Pointer.Capture(WaveformBorder);
        DragCursor(point);
    }

    private void OnWaveMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(WaveformBorder);
        if (_dragMarker is not null)
        {
            if (!_markerDragging && Math.Abs(point.X - _markerPressPoint.X) >= MarkerLaneLayout.DragThreshold)
            {
                _markerDragging = true;
            }
            if (_markerDragging)
            {
                var target = PositionFor(point.X);
                if (target != _markerDragTarget)
                {
                    _markerDragTarget = target;
                    DrawMarkers();
                }
            }
            return;
        }
        if (_dragging)
        {
            DragCursor(point);
            return;
        }
        UpdateHover(point.X, VisibleMarkers());
    }

    private void OnWaveReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragMarker is { } dragged)
        {
            var moved = _markerDragging;
            var target = _markerDragTarget;
            _dragMarker = null;
            _markerDragging = false;
            e.Pointer.Capture(null);
            if (_viewModel is { } viewModel)
            {
                if (moved)
                {
                    viewModel.MoveMarker(dragged, target);
                }
                else
                {
                    viewModel.RemoveMarker(dragged);
                }
            }
            DrawMarkers();
            return;
        }
        ReleaseDrag();
    }

    private void OnWaveCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _dragging = false;
        _dragMarker = null;
        _markerDragging = false;
    }

    private void ReleaseDrag()
    {
        if (!_dragging)
        {
            return;
        }
        _dragging = false;
        if (DataContext is TransportViewModel viewModel)
        {
            viewModel.SeekToFilePosition(_dragTarget);
        }
    }

    private void DragCursor(Point point)
    {
        var width = WaveformCanvas.Bounds.Width;
        if (_peaks is null || _latest is null || width <= 0)
        {
            return;
        }
        var fraction = Math.Clamp(point.X / width, 0.0, 1.0);
        _dragTarget = TimeSpan.FromSeconds(fraction * _latest.Deck.Duration.TotalSeconds);
        SetPlayhead(fraction);
    }

    private static double FileFraction(DeckContent deck, TimeSpan filePosition)
    {
        var duration = deck.Duration.TotalSeconds;
        return duration > 0 ? Math.Clamp(filePosition.TotalSeconds / duration, 0.0, 1.0) : 0.0;
    }

    private void DrawMarkers()
    {
        MarkerCanvas.Children.Clear();
        var width = WaveformCanvas.Bounds.Width;
        var duration = _viewModel?.CurrentDuration ?? TimeSpan.Zero;
        var markers = VisibleMarkers();
        if (markers.IsDefaultOrEmpty || width <= 0 || duration <= TimeSpan.Zero)
        {
            return;
        }
        var labels = MarkerLaneLayout.LabeledIndices(markers, duration, width);
        foreach (var marker in markers)
        {
            var x = MarkerLaneLayout.MarkerX(marker.Position, duration, width);
            var brush = _hoverMarker is { } hover && hover == marker ? MarkerHoverBrush : MarkerBrush;
            MarkerCanvas.Children.Add(MarkerLine(x, brush));
            MarkerCanvas.Children.Add(MarkerDiamond(x, brush));
            var time = new TextBlock
            {
                Text = FormatMarkerTime(marker.Position),
                FontSize = 10,
                FontFamily = MonoFont,
                Foreground = brush,
                IsHitTestVisible = false,
            };
            MarkerCanvas.Children.Add(time);
            Place(time, x, 0, width);
        }
        foreach (var index in labels)
        {
            var marker = markers[index];
            var x = MarkerLaneLayout.MarkerX(marker.Position, duration, width);
            var brush = _hoverMarker is { } hover && hover == marker ? MarkerHoverBrush : MarkerBrush;
            var block = new TextBlock
            {
                Text = marker.Name,
                FontSize = 11,
                FontFamily = MonoFont,
                Foreground = brush,
                MaxWidth = MarkerTextMaxWidth,
                TextTrimming = TextTrimming.CharacterEllipsis,
                IsHitTestVisible = false,
            };
            MarkerCanvas.Children.Add(block);
            Place(block, x, MarkerTimeRowHeight + MarkerWaveHeight, width);
        }
    }

    private ImmutableArray<Marker> VisibleMarkers()
    {
        var markers = _viewModel?.Markers ?? default;
        var list = markers.IsDefaultOrEmpty ? ImmutableArray<Marker>.Empty : markers;
        if (_markerDragging && _dragMarker is { } dragged)
        {
            var index = list.IndexOf(dragged);
            if (index >= 0)
            {
                list = list.SetItem(index, dragged with { Position = _markerDragTarget });
            }
        }
        return _pendingMarker is { } pending ? list.Add(pending) : list;
    }

    private static Rectangle MarkerLine(double x, IBrush brush)
    {
        var line = new Rectangle
        {
            Width = 1,
            Height = MarkerWaveHeight,
            Fill = brush,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(line, x);
        Canvas.SetTop(line, MarkerTimeRowHeight);
        return line;
    }

    private static Path MarkerDiamond(double x, IBrush brush)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(3.5, 0), true);
            context.LineTo(new Point(7, 3.5));
            context.LineTo(new Point(3.5, 7));
            context.LineTo(new Point(0, 3.5));
        }
        var diamond = new Path
        {
            Data = geometry,
            Fill = brush,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(diamond, x - 3.5);
        Canvas.SetTop(diamond, MarkerTimeRowHeight - 1);
        return diamond;
    }

    private void Place(Control control, double centerX, double top, double width)
    {
        control.Measure(Size.Infinity);
        var textWidth = control.DesiredSize.Width;
        Canvas.SetLeft(control, Math.Clamp(centerX - (textWidth / 2.0), 0.0, Math.Max(0.0, width - textWidth)));
        Canvas.SetTop(control, top);
    }


    private static string FormatMarkerTime(TimeSpan position) =>
        $"{(int)position.TotalMinutes:00}:{position.Seconds:00}.{position.Milliseconds / 100}";

    private FontFamily MonoFont =>
        this.TryFindResource("MonoFont", out var value) && value is FontFamily family ? family : FontFamily.Default;

    private Marker? MarkerAt(double x, ImmutableArray<Marker> markers)
    {
        var width = WaveformCanvas.Bounds.Width;
        var duration = _viewModel?.CurrentDuration ?? TimeSpan.Zero;
        return MarkerLaneLayout.HitTest(markers, x, duration, width);
    }

    private TimeSpan PositionFor(double x)
    {
        var width = WaveformCanvas.Bounds.Width;
        var duration = _viewModel?.CurrentDuration ?? TimeSpan.Zero;
        return MarkerLaneLayout.PositionFor(x, duration, width);
    }

    private void UpdateHover(double x, ImmutableArray<Marker> markers)
    {
        var marker = MarkerAt(x, markers);
        if (marker == _hoverMarker)
        {
            return;
        }
        _hoverMarker = marker;
        if (marker is null)
        {
            RestartCloseTimer();
            DrawMarkers();
            return;
        }
        CloseActionPopup();
        DrawMarkers();
        RestartHoverTimer();
    }

    private void RestartHoverTimer()
    {
        if (_hoverTimer is null)
        {
            return;
        }
        _hoverTimer.Stop();
        var marker = _hoverMarker;
        if (marker is null || _editor is not null || _dragMarker is not null)
        {
            return;
        }
        _hoverTimerMarker = marker;
        _hoverTimer.Start();
    }

    private void OnHoverTimerTick(object? sender, EventArgs e)
    {
        _hoverTimer?.Stop();
        var marker = _hoverTimerMarker;
        _hoverTimerMarker = null;
        if (marker is null || _editor is not null || _dragMarker is not null || _dragging)
        {
            return;
        }
        if (_hoverMarker is null || !_hoverMarker.Equals(marker) || VisibleMarkers().IndexOf(marker) < 0)
        {
            return;
        }
        OpenActionPopup(marker);
    }

    private void OnWaveExited(object? sender, PointerEventArgs e)
    {
        _hoverMarker = null;
        _hoverTimer?.Stop();
        _hoverTimerMarker = null;
        RestartCloseTimer();
        DrawMarkers();
    }

    private void RestartCloseTimer()
    {
        if (_closeTimer is null || _actionPopup is null)
        {
            return;
        }
        _closeTimer.Stop();
        _closeTimer.Start();
    }

    private void OnCloseTimerTick(object? sender, EventArgs e)
    {
        _closeTimer?.Stop();
        CloseActionPopup();
    }

    private void OnPopupEntered(object? sender, PointerEventArgs e) => _closeTimer?.Stop();

    private void OnPopupExited(object? sender, PointerEventArgs e) => RestartCloseTimer();

    private static string ActionLabel(MarkerAction action) => action switch
    {
        MarkerAction.None => "Ничего",
        MarkerAction.Pause => "Пауза",
        MarkerAction.RepeatFromPrevious => "Повтор от прошлой",
        MarkerAction.Restart => "С начала",
        _ => action.ToString(),
    };

    private void OpenActionPopup(Marker marker)
    {
        CloseActionPopup();
        if (_viewModel is null)
        {
            return;
        }
        var panel = new StackPanel { Spacing = 2, MinWidth = 170 };
        var foreground = MarkerHoverBrush;
        if (this.TryFindResource("BrushFg", out var fg) && fg is IBrush fgBrush)
        {
            foreground = fgBrush;
        }
        _actionHoverBrush = new ImmutableSolidColorBrush(Color.Parse("#1F1F1F"));
        if (this.TryFindResource("BrushSurfaceAlt", out var alt) && alt is IBrush altBrush)
        {
            _actionHoverBrush = altBrush;
        }
        var closer = new Button
        {
            Content = "×",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Padding = new Thickness(8, 0, 8, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = MarkerBrush,
            FontSize = 12,
        };
        closer.Click += OnPopupCloseClick;
        panel.Children.Add(closer);
        foreach (var action in MarkerActions)
        {
            var button = new Button
            {
                Content = ActionLabel(action),
                Tag = action,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 6, 10, 6),
                MinHeight = 28,
                FontSize = 13,
                Foreground = foreground,
            };
            if (action == marker.Action)
            {
                button.FontWeight = FontWeight.Bold;
            }
            button.Click += OnActionClick;
            button.PointerEntered += OnActionButtonEntered;
            button.PointerExited += OnActionButtonExited;
            panel.Children.Add(button);
        }
        var footer = new TextBlock
        {
            Text = MarkerHint,
            FontSize = 10,
            Foreground = MarkerBrush,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 260,
            Margin = new Thickness(0, 4, 0, 0),
        };
        panel.Children.Add(footer);
        var background = MarkerBrush;
        if (this.TryFindResource("BrushSurface", out var surface) && surface is IBrush surfaceBrush)
        {
            background = surfaceBrush;
        }
        var line = MarkerBrush;
        if (this.TryFindResource("BrushLine", out var lineRes) && lineRes is IBrush lineBrush)
        {
            line = lineBrush;
        }
        var frame = new Border
        {
            Child = panel,
            Background = background,
            BorderBrush = line,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(6),
        };
        panel.Measure(Size.Infinity);
        var popup = new Popup
        {
            PlacementTarget = WaveformBorder,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementAnchor = Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.TopLeft,
            PlacementGravity = Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.BottomRight,
            Child = new ThemeVariantScope
            {
                RequestedThemeVariant = ThemeVariant.Dark,
                Child = frame,
            },
        };
        frame.PointerEntered += OnPopupEntered;
        frame.PointerExited += OnPopupExited;
        _actionPopup = popup;
        _actionMarker = marker;
        PositionActionPopup(popup, marker, panel);
        popup.Open();
    }

    private void PositionActionPopup(Popup popup, Marker marker, Control content)
    {
        var waveWidth = WaveformCanvas.Bounds.Width;
        var duration = _viewModel?.CurrentDuration ?? TimeSpan.Zero;
        var x = MarkerLaneLayout.MarkerX(marker.Position, duration, waveWidth);
        var popupWidth = content.DesiredSize.Width;
        var borderWidth = WaveformBorder.Bounds.Width;
        popup.HorizontalOffset = Math.Clamp(x - (popupWidth / 2.0), 0.0, Math.Max(0.0, borderWidth - popupWidth));
        var strip = WaveformStrip;
        var anchor = strip.TranslatePoint(new Point(x, strip.Bounds.Height), WaveformBorder);
        popup.VerticalOffset = anchor?.Y ?? (MarkerTimeRowHeight + MarkerWaveHeight + 14.0);
    }

    private void OnActionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MarkerAction action } && _actionMarker is { } marker && _viewModel is not null)
        {
            _viewModel.SetMarkerAction(marker, action);
        }
        _hoverMarker = null;
        CloseActionPopup();
        DrawMarkers();
    }

    private void OnActionButtonEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Button button)
        {
            button.Background = _actionHoverBrush;
        }
    }

    private void OnActionButtonExited(object? sender, PointerEventArgs e)
    {
        if (sender is Button button)
        {
            button.Background = Brushes.Transparent;
        }
    }

    private void OnPopupCloseClick(object? sender, RoutedEventArgs e)
    {
        _hoverMarker = null;
        CloseActionPopup();
        DrawMarkers();
    }

    private void CloseActionPopup()
    {
        _closeTimer?.Stop();
        if (_actionPopup is { } popup)
        {
            popup.Close();
            if (popup.Child is ThemeVariantScope scope && scope.Child is Border frame)
            {
                frame.PointerEntered -= OnPopupEntered;
                frame.PointerExited -= OnPopupExited;
                if (frame.Child is StackPanel panel)
                {
                    foreach (var button in panel.Children.OfType<Button>())
                    {
                        button.Click -= OnActionClick;
                        button.Click -= OnPopupCloseClick;
                        button.PointerEntered -= OnActionButtonEntered;
                        button.PointerExited -= OnActionButtonExited;
                    }
                }
            }
        }
        _actionPopup = null;
        _actionMarker = null;
    }

    private void BeginMarker(double x)
    {
        if (_viewModel is null || _viewModel.CurrentDuration <= TimeSpan.Zero)
        {
            return;
        }
        var marker = new Marker(NextMarkerName(), PositionFor(x), MarkerAction.Pause);
        _pendingMarker = marker;
        DrawMarkers();
        OpenEditor(marker);
    }

    private string NextMarkerName() =>
        _viewModel?.Markers is { IsDefaultOrEmpty: false } markers ? $"Метка {markers.Length + 1}" : "Метка 1";

    private void OpenEditor(Marker marker)
    {
        CloseActionPopup();
        CloseEditor();
        var editor = new TextBox
        {
            Text = marker.Name,
            MaxLength = 48,
            Classes = { "markerEditor" },
        };
        editor.KeyDown += OnEditorKeyDown;
        editor.LostFocus += OnEditorLostFocus;
        MarkerEditorCanvas.Children.Add(editor);
        _editor = editor;
        _editorMarker = marker;
        PositionEditor(editor, marker);
        Dispatcher.UIThread.Post(() =>
        {
            editor.Focus();
            editor.SelectAll();
        });
    }

    private void PositionEditor(TextBox editor, Marker marker)
    {
        var width = WaveformCanvas.Bounds.Width;
        var duration = _viewModel?.CurrentDuration ?? TimeSpan.Zero;
        var x = MarkerLaneLayout.MarkerX(marker.Position, duration, width);
        editor.Measure(Size.Infinity);
        var editorWidth = Math.Clamp(editor.DesiredSize.Width + 16.0, MarkerEditorMinWidth, MarkerEditorMaxWidth);
        editor.Width = editorWidth;
        Canvas.SetLeft(editor, Math.Clamp(x - (editorWidth / 2.0), 0.0, Math.Max(0.0, width - editorWidth)));
        Canvas.SetTop(editor, MarkerTimeRowHeight + MarkerWaveHeight);
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            CommitEditor();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CancelEditor();
        }
    }

    private void OnEditorLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_editorMarker is not null)
        {
            CommitEditor();
        }
    }

    private void CommitEditor()
    {
        if (_editor is not { } editor || _editorMarker is not { } marker)
        {
            CloseEditor();
            return;
        }
        var typed = editor.Text?.Trim();
        var name = string.IsNullOrEmpty(typed) ? marker.Name : typed;
        var pending = _pendingMarker is not null && ReferenceEquals(marker, _pendingMarker);
        CloseEditor();
        if (_viewModel is null)
        {
            _pendingMarker = null;
            DrawMarkers();
            return;
        }
        if (pending)
        {
            _pendingMarker = null;
            _viewModel.PlaceMarker(marker.Position, name);
        }
        else if (name != marker.Name)
        {
            _viewModel.RenameMarker(marker, name);
        }
        DrawMarkers();
    }

    private void CancelEditor()
    {
        var pending = _pendingMarker is not null && _editorMarker is not null && ReferenceEquals(_editorMarker, _pendingMarker);
        CloseEditor();
        if (pending)
        {
            _pendingMarker = null;
            DrawMarkers();
        }
    }

    private void CloseEditor()
    {
        if (_editor is { } editor)
        {
            editor.KeyDown -= OnEditorKeyDown;
            editor.LostFocus -= OnEditorLostFocus;
            MarkerEditorCanvas.Children.Remove(editor);
        }
        _editor = null;
        _editorMarker = null;
    }
}
