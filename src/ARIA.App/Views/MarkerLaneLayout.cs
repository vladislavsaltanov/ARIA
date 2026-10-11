namespace Aria.App.Views;

using System.Collections.Immutable;
using Aria.Core.Model;

public static class MarkerLaneLayout
{
    public const double HitRadius = 4.0;
    public const double DragThreshold = 3.0;
    public const double LabelGap = 28.0;

    public static double MarkerX(TimeSpan position, TimeSpan duration, double width)
    {
        var fraction = duration > TimeSpan.Zero ? position.TotalSeconds / duration.TotalSeconds : 0.0;
        return Math.Clamp(fraction, 0.0, 1.0) * width;
    }

    public static TimeSpan PositionFor(double x, TimeSpan duration, double width)
    {
        if (width <= 0 || duration <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }
        return TimeSpan.FromSeconds(Math.Clamp(x / width, 0.0, 1.0) * duration.TotalSeconds);
    }

    public static Marker? HitTest(ImmutableArray<Marker> markers, double x, TimeSpan duration, double width)
    {
        if (markers.IsDefaultOrEmpty || width <= 0 || duration <= TimeSpan.Zero)
        {
            return null;
        }
        Marker? best = null;
        var bestDistance = double.MaxValue;
        foreach (var marker in markers)
        {
            var distance = Math.Abs(MarkerX(marker.Position, duration, width) - x);
            if (distance <= HitRadius && distance < bestDistance)
            {
                best = marker;
                bestDistance = distance;
            }
        }
        return best;
    }

    public static ImmutableArray<int> LabeledIndices(ImmutableArray<Marker> markers, TimeSpan duration, double width)
    {
        if (markers.IsDefaultOrEmpty || width <= 0 || duration <= TimeSpan.Zero)
        {
            return [];
        }
        var builder = ImmutableArray.CreateBuilder<int>(markers.Length);
        var lastNamed = double.NegativeInfinity;
        for (var index = 0; index < markers.Length; index++)
        {
            var x = MarkerX(markers[index].Position, duration, width);
            if (x - lastNamed >= LabelGap)
            {
                builder.Add(index);
                lastNamed = x;
            }
        }
        return builder.ToImmutable();
    }
}
