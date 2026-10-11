namespace Aria.App.Tests;

using System.Collections.Immutable;
using Aria.App.Views;
using Aria.Core.Model;

public sealed class MarkerLaneLayoutTests
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(90);
    private const double Width = 600.0;

    [Fact]
    public void MarkerX_MapsStartMiddleEnd()
    {
        Assert.Equal(0.0, MarkerLaneLayout.MarkerX(TimeSpan.Zero, Duration, Width));
        Assert.Equal(300.0, MarkerLaneLayout.MarkerX(TimeSpan.FromSeconds(45), Duration, Width));
        Assert.Equal(600.0, MarkerLaneLayout.MarkerX(Duration, Duration, Width));
    }

    [Fact]
    public void MarkerX_ClampsOutsideRange()
    {
        Assert.Equal(600.0, MarkerLaneLayout.MarkerX(TimeSpan.FromSeconds(120), Duration, Width));
        Assert.Equal(0.0, MarkerLaneLayout.MarkerX(TimeSpan.FromSeconds(-5), Duration, Width));
        Assert.Equal(0.0, MarkerLaneLayout.MarkerX(TimeSpan.FromSeconds(45), TimeSpan.Zero, Width));
    }

    [Fact]
    public void PositionFor_MapsAndClamps()
    {
        Assert.Equal(TimeSpan.Zero, MarkerLaneLayout.PositionFor(0, Duration, Width));
        Assert.Equal(45.0, MarkerLaneLayout.PositionFor(300, Duration, Width).TotalSeconds, 3);
        Assert.Equal(90.0, MarkerLaneLayout.PositionFor(900, Duration, Width).TotalSeconds, 3);
        Assert.Equal(TimeSpan.Zero, MarkerLaneLayout.PositionFor(-10, Duration, Width));
        Assert.Equal(TimeSpan.Zero, MarkerLaneLayout.PositionFor(300, Duration, 0));
        Assert.Equal(TimeSpan.Zero, MarkerLaneLayout.PositionFor(300, TimeSpan.Zero, Width));
    }

    [Fact]
    public void HitTest_FindsNearestWithinRadius()
    {
        var markers = Markers(("a", 45), ("b", 46));
        var hit = MarkerLaneLayout.HitTest(markers, 300.0, Duration, Width);
        Assert.Equal("a", hit?.Name);
    }

    [Fact]
    public void HitTest_ReturnsNullOutsideRadius()
    {
        var markers = Markers(("a", 45));
        Assert.Null(MarkerLaneLayout.HitTest(markers, 310.0, Duration, Width));
        Assert.Null(MarkerLaneLayout.HitTest(markers, 300.0, Duration, 0));
        Assert.Null(MarkerLaneLayout.HitTest(ImmutableArray<Marker>.Empty, 300.0, Duration, Width));
    }

    [Fact]
    public void LabeledIndices_SkipsCrowdedNames()
    {
        var markers = Markers(("first", 45), ("second", 45.05));
        Assert.Equal([0], MarkerLaneLayout.LabeledIndices(markers, Duration, Width).ToArray());
    }

    [Fact]
    public void LabeledIndices_KeepsSpacedNames_AndBoundaryGap()
    {
        var markers = Markers(("a", 0), ("b", 4.2), ("c", 45));
        Assert.Equal([0, 1, 2], MarkerLaneLayout.LabeledIndices(markers, Duration, Width).ToArray());
        Assert.Empty(MarkerLaneLayout.LabeledIndices(ImmutableArray<Marker>.Empty, Duration, Width));
    }

    private static ImmutableArray<Marker> Markers(params (string Name, double Seconds)[] items)
    {
        var builder = ImmutableArray.CreateBuilder<Marker>(items.Length);
        foreach (var (name, seconds) in items)
        {
            builder.Add(new Marker(name, TimeSpan.FromSeconds(seconds), MarkerAction.Stop));
        }
        return builder.ToImmutable();
    }
}
