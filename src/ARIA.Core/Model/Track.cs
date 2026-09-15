namespace Aria.Core.Model;

using System.Collections.Immutable;

public sealed record TrackDefaults(
    double GainDb = 0.0,
    EndAction EndAction = EndAction.Advance,
    Fade? In = null,
    Fade? Out = null,
    ImmutableArray<Marker>? Markers = null,
    TrackAudioSettings? Audio = null);

public sealed record Track(
    TrackId Id,
    string FilePath,
    string DefaultName,
    TimeSpan Duration,
    TrackDefaults Defaults);
