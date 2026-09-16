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
    ImmutableArray<Marker> Markers,
    TrackAudioSettings Audio);

public static class EffectiveSettings
{
    public static PlaybackSettings Resolve(ProjectEntry entry, Track track, EndAction defaultEndAction = EndAction.Advance)
    {
        var o = entry.Overrides;
        var d = track.Defaults;
        return new PlaybackSettings(
            DisplayName: o?.Name ?? track.DefaultName,
            Color: o?.Color,
            GainDb: o?.GainDb ?? d.GainDb,
            EndAction: o?.EndAction ?? (d.EndAction != EndAction.Advance ? d.EndAction : defaultEndAction),
            In: o?.In ?? d.In ?? Fade.None,
            Out: o?.Out ?? d.Out ?? Fade.None,
            CueIn: o?.CueIn ?? TimeSpan.Zero,
            CueOut: o?.CueOut,
            Markers: d.Markers ?? ImmutableArray<Marker>.Empty,
            Audio: o?.Audio ?? d.Audio ?? TrackAudioSettings.Default);
    }

    public static PlaybackSettings ForTrack(Track track, EndAction defaultEndAction = EndAction.Advance)
    {
        var d = track.Defaults;
        return new PlaybackSettings(
            DisplayName: track.DefaultName,
            Color: null,
            GainDb: d.GainDb,
            EndAction: d.EndAction != EndAction.Advance ? d.EndAction : defaultEndAction,
            In: d.In ?? Fade.None,
            Out: d.Out ?? Fade.None,
            CueIn: TimeSpan.Zero,
            CueOut: null,
            Markers: d.Markers ?? ImmutableArray<Marker>.Empty,
            Audio: d.Audio ?? TrackAudioSettings.Default);
    }
}
