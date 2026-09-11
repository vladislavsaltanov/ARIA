namespace Aria.App.Services;

using Avalonia;
using Aria.Core.Model;
using Aria.Persistence;
using Avalonia.Media;

public sealed class WaveformThumbs
{
    private const int BarCount = 64;

    private readonly IWaveformStore _waveforms;
    private readonly Dictionary<TrackId, StreamGeometry?> _cache = [];

    public WaveformThumbs(IWaveformStore waveforms) => _waveforms = waveforms;

    public StreamGeometry? For(TrackId trackId)
    {
        if (_cache.TryGetValue(trackId, out var cached))
        {
            return cached;
        }
        var geometry = Build(_waveforms.Load(trackId));
        _cache[trackId] = geometry;
        return geometry;
    }

    private static StreamGeometry? Build(WaveformPeaks? peaks)
    {
        if (peaks is not { Points.Length: > 0 })
        {
            return null;
        }
        var points = peaks.Points;
        var stride = Math.Max(1, (points.Length + BarCount - 1) / BarCount);
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        var bars = 0;
        for (var start = 0; start < points.Length && bars < BarCount; start += stride, bars++)
        {
            var peak = 0f;
            for (var i = start; i < Math.Min(start + stride, points.Length); i++)
            {
                peak = Math.Max(peak, Math.Max(Math.Abs(points[i].Min), Math.Abs(points[i].Max)));
            }
            var half = Math.Min(1.0, peak) * 0.5;
            var x0 = (double)bars / BarCount;
            var x1 = (double)(bars + 1) / BarCount - 0.01;
            context.BeginFigure(new Point(x0, 0.5 - half), true);
            context.LineTo(new Point(x1, 0.5 - half));
            context.LineTo(new Point(x1, 0.5 + half));
            context.LineTo(new Point(x0, 0.5 + half));
            context.EndFigure(true);
        }
        return geometry;
    }
}
