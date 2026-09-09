namespace Aria.Core.Model;

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

public enum MarkerAction
{
    Stop,
}

public sealed record Marker(string Name, TimeSpan Position, MarkerAction Action);
