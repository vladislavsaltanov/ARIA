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
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;

public partial class PlaybackHeader : UserControl
{
    private static readonly IBrush WaveBrush = new ImmutableSolidColorBrush(Color.Parse("#ECECEC"));

    private IWaveformStore? _waveforms;
    private PlaybackMonitor? _monitor;
    private TransportViewModel? _viewModel;
    private PositionSnapshot? _latest;
    private TrackId? _currentTrackId;
    private WaveformPeaks? _peaks;
    private bool _dragging;
    private TimeSpan _dragTarget;

    public PlaybackHeader()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        WaveformBorder.PointerPressed += OnWavePressed;
        WaveformBorder.PointerMoved += OnWaveMoved;
        WaveformBorder.PointerReleased += OnWaveReleased;
        WaveformBorder.PointerCaptureLost += OnWaveCaptureLost;
        WaveformCanvas.SizeChanged += OnWaveCanvasSizeChanged;
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
        Avalonia.Threading.Dispatcher.UIThread.Post(() => WaveCursor.IsVisible = false);

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
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
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
        WaveCursor.IsVisible = _peaks is not null;
        DrawWaveform();
    }

    private void UpdateCursor(PositionSnapshot snapshot)
    {
        if (_peaks is null || WaveformCanvas.Bounds.Width <= 0)
        {
            return;
        }
        var fraction = FileFraction(snapshot.Deck, snapshot.FilePosition);
        Canvas.SetLeft(WaveCursor, fraction * WaveformCanvas.Bounds.Width);
        WaveCursor.IsVisible = true;
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
            context.BeginFigure(new Point(0, center), false);
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
        if (_latest is { } snapshot)
        {
            UpdateCursor(snapshot);
        }
    }

    private void OnWavePressed(object? sender, PointerPressedEventArgs e)
    {
        if (_peaks is null || _latest is null)
        {
            return;
        }
        _dragging = true;
        e.Pointer.Capture(WaveformBorder);
        DragCursor(e.GetPosition(WaveformBorder));
    }

    private void OnWaveMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }
        DragCursor(e.GetPosition(WaveformBorder));
    }

    private void OnWaveReleased(object? sender, PointerReleasedEventArgs e)
    {
        ReleaseDrag();
    }

    private void OnWaveCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _dragging = false;
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
        Canvas.SetLeft(WaveCursor, fraction * width);
        WaveCursor.IsVisible = true;
    }

    private static double FileFraction(DeckContent deck, TimeSpan filePosition)
    {
        var duration = deck.Duration.TotalSeconds;
        return duration > 0 ? Math.Clamp(filePosition.TotalSeconds / duration, 0.0, 1.0) : 0.0;
    }
}