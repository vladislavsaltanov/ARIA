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

public sealed record Smoothing(bool Enabled, TimeSpan ManualCrossfade, TimeSpan AutoCrossfade, TimeSpan StartFade, TimeSpan StopFade, TimeSpan SeekFade)
{
    public static Smoothing Default { get; } = new(
        false,
        TimeSpan.FromMilliseconds(350),
        TimeSpan.FromMilliseconds(800),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(350));
}

public enum MarkerAction
{
    Stop,
}

public sealed record Marker(string Name, TimeSpan Position, MarkerAction Action);
