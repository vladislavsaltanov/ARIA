namespace Aria.Core.Model;

using System.Collections.Immutable;

public sealed record EqBand(float FrequencyHz, float GainDb, float Q);

public sealed record AudioEq
{
    public AudioEq(ImmutableArray<EqBand> bands)
    {
        if (bands.IsDefault || bands.Length != 7)
        {
            throw new ArgumentException("Equalizer requires exactly 7 bands.", nameof(bands));
        }
        Bands = bands;
    }

    public ImmutableArray<EqBand> Bands { get; init; }

    public static readonly float[] DefaultFrequencies = [63, 160, 400, 1000, 2500, 6300, 12000];

    public static AudioEq Flat { get; } = new([.. DefaultFrequencies.Select(f => new EqBand(f, 0, 1))]);
}

public sealed record LimiterSettings(bool Enabled, double ThresholdDb, double ReleaseMs)
{
    public static LimiterSettings Default { get; } = new(true, -1.0, 100.0);
}

public sealed record LufsMeterZones(double GreenDb = -15.0, double YellowDb = -9.0, double RedDb = -5.0)
{
    public static LufsMeterZones Default { get; } = new();
}

public sealed record GlobalAudioSettings(
    double Pan,
    bool Mono,
    double HpfHz,
    AudioEq Eq,
    LimiterSettings Limiter,
    double NormalizeTargetLufs = -16.0,
    LufsMeterZones? MeterZones = null,
    bool NormalizeEnabled = false)
{
    public static GlobalAudioSettings Default { get; } = new(0, false, 0, AudioEq.Flat, LimiterSettings.Default);

    public LufsMeterZones EffectiveZones => MeterZones ?? LufsMeterZones.Default;
}

public sealed record TrackAudioSettings(
    double GainDb,
    double Pan,
    AudioEq Eq,
    bool NormalizeEnabled = false,
    double? MeasuredLufs = null)
{
    public static TrackAudioSettings Default { get; } = new(0, 0, AudioEq.Flat);
}

public static class LufsNormalize
{
    private const double GainMinDb = -60.0;
    private const double GainMaxDb = 12.0;

    public static double AdjustGain(double baseGainDb, double measuredLufs, double targetLufs)
        => Math.Clamp(baseGainDb + targetLufs - measuredLufs, GainMinDb, GainMaxDb);
}

public static class AudioValidation
{
    private const float EqGainMinDb = -15.0f;
    private const float EqGainMaxDb = 15.0f;
    private const float EqFreqMinHz = 20.0f;
    private const float EqFreqMaxHz = 20000.0f;
    private const float EqQMin = 0.1f;
    private const float EqQMax = 10.0f;
    private const double LimiterThresholdMinDb = -24.0;
    private const double LimiterThresholdMaxDb = 0.0;
    private const double LimiterReleaseMinMs = 10.0;
    private const double LimiterReleaseMaxMs = 1000.0;
    private const double PanMin = -1.0;
    private const double PanMax = 1.0;
    private const double HpfMaxHz = 400.0;
    private const double LufsTargetMinLufs = -36.0;
    private const double LufsTargetMaxLufs = -12.0;
    private const double MeterZoneMinDb = -60.0;
    private const double MeterZoneMaxDb = 0.0;
    private const double TrackGainMinDb = -60.0;
    private const double TrackGainMaxDb = 12.0;

    public static string? ValidateGlobal(GlobalAudioSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Pan is < PanMin or > PanMax)
        {
            return "pan-out-of-range";
        }
        if (value.HpfHz is < 0 or > HpfMaxHz)
        {
            return "hpf-out-of-range";
        }
        if (value.Limiter.ThresholdDb is < LimiterThresholdMinDb or > LimiterThresholdMaxDb
            || value.Limiter.ReleaseMs is < LimiterReleaseMinMs or > LimiterReleaseMaxMs)
        {
            return "limiter-out-of-range";
        }
        if (value.NormalizeTargetLufs is < LufsTargetMinLufs or > LufsTargetMaxLufs)
        {
            return "lufs-out-of-range";
        }
        var zones = value.EffectiveZones;
        if (zones.GreenDb is < MeterZoneMinDb or > MeterZoneMaxDb
            || zones.YellowDb is < MeterZoneMinDb or > MeterZoneMaxDb
            || zones.RedDb is < MeterZoneMinDb or > MeterZoneMaxDb
            || !(zones.GreenDb < zones.YellowDb && zones.YellowDb < zones.RedDb))
        {
            return "meter-out-of-range";
        }
        return ValidateEq(value.Eq);
    }

    public static string? ValidateTrack(TrackAudioSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.GainDb is < TrackGainMinDb or > TrackGainMaxDb)
        {
            return "gain-out-of-range";
        }
        if (value.Pan is < PanMin or > PanMax)
        {
            return "pan-out-of-range";
        }
        return ValidateEq(value.Eq);
    }

    private static string? ValidateEq(AudioEq eq)
    {
        ArgumentNullException.ThrowIfNull(eq);
        foreach (var band in eq.Bands)
        {
            if (band.FrequencyHz is < EqFreqMinHz or > EqFreqMaxHz
                || band.GainDb is < EqGainMinDb or > EqGainMaxDb
                || band.Q is < EqQMin or > EqQMax)
            {
                return "eq-out-of-range";
            }
        }
        return null;
    }
}
