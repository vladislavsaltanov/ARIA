namespace Aria.Core.Tests;

using Aria.Core.Model;

public sealed class BeatGridTests
{
    private static readonly TimeSpan ThreeMinutes = TimeSpan.FromMinutes(3);

    [Fact]
    public void Validate_InRange_Null()
    {
        Assert.Null(BeatGridValidation.Validate(new BeatGrid(120, 4, TimeSpan.Zero), ThreeMinutes));
        Assert.Null(BeatGridValidation.Validate(new BeatGrid(20, 1, TimeSpan.FromSeconds(179.999)), ThreeMinutes));
        Assert.Null(BeatGridValidation.Validate(new BeatGrid(300, 16, TimeSpan.FromMilliseconds(250)), ThreeMinutes));
    }

    [Theory]
    [InlineData(19.99)]
    [InlineData(300.01)]
    [InlineData(double.NaN)]
    public void Validate_Bpm_OutOfRange(double bpm)
    {
        Assert.Equal("bpm-out-of-range", BeatGridValidation.Validate(new BeatGrid(bpm, 4, TimeSpan.Zero), ThreeMinutes));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public void Validate_Beats_OutOfRange(int beats)
    {
        Assert.Equal("beats-out-of-range", BeatGridValidation.Validate(new BeatGrid(120, beats, TimeSpan.Zero), ThreeMinutes));
    }

    [Fact]
    public void Validate_FirstBeat_Negative_And_AtDuration()
    {
        Assert.Equal("first-beat-out-of-range", BeatGridValidation.Validate(new BeatGrid(120, 4, TimeSpan.FromMilliseconds(-1)), ThreeMinutes));
        Assert.Equal("first-beat-out-of-range", BeatGridValidation.Validate(new BeatGrid(120, 4, ThreeMinutes), ThreeMinutes));
    }

    [Fact]
    public void Validate_ZeroDuration_AllowsZeroFirstBeat()
    {
        Assert.Null(BeatGridValidation.Validate(new BeatGrid(120, 4, TimeSpan.Zero), TimeSpan.Zero));
        Assert.Null(BeatGridValidation.Validate(new BeatGrid(120, 4, TimeSpan.FromSeconds(1)), TimeSpan.Zero));
    }
}
