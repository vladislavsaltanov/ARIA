namespace Aria.Audio.Tests;

using Aria.Core.Model;

public sealed class FadeCurvesTests
{
    private static readonly FadeCurve[] AllCurves = [FadeCurve.Linear, FadeCurve.Logarithmic, FadeCurve.Exponential, FadeCurve.SCurve];

    [Fact]
    public void AllCurves_MapZeroToOne()
    {
        foreach (var curve in AllCurves)
        {
            Assert.Equal(0.0, FadeCurves.Evaluate(curve, 0.0), 12);
            Assert.Equal(1.0, FadeCurves.Evaluate(curve, 1.0), 12);
        }
    }

    [Fact]
    public void AllCurves_AreMonotonic()
    {
        foreach (var curve in AllCurves)
        {
            var previous = -1.0;
            for (var i = 0; i <= 100; i++)
            {
                var value = FadeCurves.Evaluate(curve, i / 100.0);
                Assert.True(value >= previous, $"{curve} decreased at {i}");
                previous = value;
            }
        }
    }

    [Fact]
    public void SCurve_PassesThroughMidpoint()
    {
        Assert.Equal(0.5, FadeCurves.Evaluate(FadeCurve.SCurve, 0.5), 12);
    }

    [Fact]
    public void Logarithmic_AboveLinear_Exponential_BelowLinear_AtMidpoint()
    {
        Assert.True(FadeCurves.Evaluate(FadeCurve.Logarithmic, 0.5) > 0.5);
        Assert.True(FadeCurves.Evaluate(FadeCurve.Exponential, 0.5) < 0.5);
    }

    [Fact]
    public void OutOfRangeInput_IsClamped()
    {
        Assert.Equal(0.0, FadeCurves.Evaluate(FadeCurve.Linear, -0.5), 12);
        Assert.Equal(1.0, FadeCurves.Evaluate(FadeCurve.Linear, 1.5), 12);
    }
}
