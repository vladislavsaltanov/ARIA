namespace Aria.Core.Model;

using System.Collections.Immutable;

public enum EndAction
{
    Pause,
    Stop,
    Replay,
    Advance,
}

public enum FadeCurve
{
    Linear,
    Logarithmic,
    Exponential,
    SCurve,
}

public sealed record Fade(TimeSpan Duration, FadeCurve Curve)
{
    public static Fade None { get; } = new(TimeSpan.Zero, FadeCurve.Linear);
}

public sealed record Smoothing(bool Enabled, TimeSpan ManualCrossfade, TimeSpan AutoCrossfade, TimeSpan StartFade, TimeSpan StopFade, TimeSpan SeekFade, TimeSpan MarkerFade)
{
    public static Smoothing Default { get; } = new(
        false,
        TimeSpan.FromMilliseconds(350),
        TimeSpan.FromMilliseconds(800),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(350),
        TimeSpan.FromMilliseconds(250));
}

public enum MarkerAction
{
    Pause,
    None,
    RepeatFromPrevious,
    Restart,
}

public sealed record Marker(string Name, TimeSpan Position, MarkerAction Action);

public static class MarkerValidation
{
    private const int NameMaxLength = 48;
    private const int MarkerMaxCount = 64;

    public static string? Validate(ImmutableArray<Marker> markers, TimeSpan duration)
    {
        if (markers.IsDefaultOrEmpty)
        {
            return null;
        }
        if (markers.Length > MarkerMaxCount)
        {
            return "marker-too-many";
        }
        var seen = new HashSet<long>();
        foreach (var marker in markers)
        {
            if (string.IsNullOrWhiteSpace(marker.Name))
            {
                return "marker-name-empty";
            }
            if (marker.Name.Length > NameMaxLength)
            {
                return "marker-name-too-long";
            }
            if (marker.Position < TimeSpan.Zero || marker.Position > duration)
            {
                return "marker-position-out-of-range";
            }
            if (!seen.Add(marker.Position.Ticks))
            {
                return "marker-position-duplicate";
            }
        }
        return null;
    }
}
