namespace Aria.Core.Playback;

public sealed record ClickSettings
{
    public ClickSettings(double bpm, int beatsPerBar, double gainDb, double offsetMs)
    {
        if (bpm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bpm));
        }
        if (beatsPerBar < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(beatsPerBar));
        }
        Bpm = bpm;
        BeatsPerBar = beatsPerBar;
        GainDb = gainDb;
        OffsetMs = offsetMs;
    }

    public double Bpm { get; init; }

    public int BeatsPerBar { get; init; }

    public double GainDb { get; init; }

    public double OffsetMs { get; init; }
}
