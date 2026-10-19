namespace Aria.Core.Tests;

using System.Collections.Immutable;
using Aria.Core.Model;

public sealed class MarkerValidationTests
{
    private static readonly TimeSpan Duration = TimeSpan.FromMinutes(3);

    private static ImmutableArray<Marker> One(string name = "метка", TimeSpan? position = null) =>
        [new Marker(name, position ?? TimeSpan.FromSeconds(30), MarkerAction.Pause)];

    [Fact]
    public void EmptyMarkers_AreValid() => Assert.Null(MarkerValidation.Validate([], Duration));

    [Fact]
    public void ValidMarkers_AreAccepted() =>
        Assert.Null(MarkerValidation.Validate(One(), Duration));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyName_IsRejected(string name) =>
        Assert.Equal("marker-name-empty", MarkerValidation.Validate(One(name), Duration));

    [Fact]
    public void LongName_IsRejected() =>
        Assert.Equal("marker-name-too-long", MarkerValidation.Validate(One(new string('я', 49)), Duration));

    [Fact]
    public void NegativePosition_IsRejected() =>
        Assert.Equal("marker-position-out-of-range", MarkerValidation.Validate(One(position: TimeSpan.FromTicks(-1)), Duration));

    [Fact]
    public void PositionPastDuration_IsRejected() =>
        Assert.Equal("marker-position-out-of-range", MarkerValidation.Validate(One(position: Duration + TimeSpan.FromTicks(1)), Duration));

    [Fact]
    public void PositionAtDuration_IsAccepted() =>
        Assert.Null(MarkerValidation.Validate(One(position: Duration), Duration));

    [Fact]
    public void DuplicatePosition_IsRejected()
    {
        ImmutableArray<Marker> markers =
        [
            new Marker("первая", TimeSpan.FromSeconds(30), MarkerAction.Pause),
            new Marker("вторая", TimeSpan.FromSeconds(30), MarkerAction.Pause),
        ];

        Assert.Equal("marker-position-duplicate", MarkerValidation.Validate(markers, Duration));
    }

    [Fact]
    public void TooManyMarkers_IsRejected()
    {
        var markers = ImmutableArray.CreateBuilder<Marker>();
        for (var index = 0; index < 65; index++)
        {
            markers.Add(new Marker($"метка {index}", TimeSpan.FromSeconds(index), MarkerAction.Pause));
        }

        Assert.Equal("marker-too-many", MarkerValidation.Validate(markers.ToImmutable(), Duration));
    }

    [Fact]
    public void SixtyFourMarkers_AreAccepted()
    {
        var markers = ImmutableArray.CreateBuilder<Marker>();
        for (var index = 0; index < 64; index++)
        {
            markers.Add(new Marker($"метка {index}", TimeSpan.FromSeconds(index), MarkerAction.Pause));
        }

        Assert.Null(MarkerValidation.Validate(markers.ToImmutable(), Duration));
    }
}
