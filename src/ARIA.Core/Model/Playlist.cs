namespace Aria.Core.Model;

using System.Collections.Immutable;

public sealed record PlaylistEntry(
    EntryId Id,
    TrackId TrackId,
    PlaylistOverrides? Overrides = null);

public sealed record PlaylistOverrides(
    string? Name = null,
    string? Color = null,
    string? Note = null,
    double? GainDb = null,
    EndAction? EndAction = null,
    Fade? In = null,
    Fade? Out = null,
    TimeSpan? CueIn = null,
    TimeSpan? CueOut = null);

public sealed record Playlist(PlaylistId Id, string Name, ImmutableArray<PlaylistEntry> Entries);
