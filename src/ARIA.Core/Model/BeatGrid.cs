namespace Aria.Core.Model;

public sealed record BeatGrid(double Bpm, int BeatsPerBar, TimeSpan FirstBeat);

public static class BeatGridValidation
{
    public const double BpmMin = 20.0;
    public const double BpmMax = 300.0;
    public const int BeatsMin = 1;
    public const int BeatsMax = 16;

    public static string? Validate(BeatGrid grid, TimeSpan duration)
    {
        if (!(grid.Bpm >= BpmMin && grid.Bpm <= BpmMax))
        {
            return "bpm-out-of-range";
        }
        if (grid.BeatsPerBar is < BeatsMin or > BeatsMax)
        {
            return "beats-out-of-range";
        }
        if (grid.FirstBeat < TimeSpan.Zero || (duration > TimeSpan.Zero && grid.FirstBeat >= duration))
        {
            return "first-beat-out-of-range";
        }
        return null;
    }
}
