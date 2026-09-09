namespace Aria.Core.Model;

using System.Collections.Immutable;

public sealed record PlaybackSettings(
    string DisplayName,
    string? Color,
    double GainDb,
    EndAction EndAction,
    Fade In,
    Fade Out,
    TimeSpan CueIn,
    TimeSpan? CueOut,
    ImmutableArray<Marker> Markers);

public static class EffectiveSettings
{
    public static PlaybackSettings Resolve(PlaylistEntry entry, Track track)
    {
        var o = entry.Overrides;
        var d = track.Defaults;
        return new PlaybackSettings(
            DisplayName: o?.Name ?? track.DefaultName,
            Color: o?.Color,
            GainDb: o?.GainDb ?? d.GainDb,
            EndAction: o?.EndAction ?? d.EndAction,
            In: o?.In ?? d.In ?? Fade.None,
            Out: o?.Out ?? d.Out ?? Fade.None,
            CueIn: o?.CueIn ?? TimeSpan.Zero,
            CueOut: o?.CueOut,
            Markers: d.Markers ?? ImmutableArray<Marker>.Empty);
    }

    public static PlaybackSettings ForTrack(Track track)
    {
        var d = track.Defaults;
        return new PlaybackSettings(
            DisplayName: track.DefaultName,
            Color: null,
            GainDb: d.GainDb,
            EndAction: d.EndAction,
            In: d.In ?? Fade.None,
            Out: d.Out ?? Fade.None,
            CueIn: TimeSpan.Zero,
            CueOut: null,
            Markers: d.Markers ?? ImmutableArray<Marker>.Empty);
    }
}
