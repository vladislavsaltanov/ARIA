namespace Aria.Core.Model;

using System.Collections.Immutable;

public sealed record ProjectEntry(
    EntryId Id,
    TrackId TrackId,
    ProjectOverrides? Overrides = null);

public sealed record ProjectOverrides(
    string? Name = null,
    string? Color = null,
    string? Note = null,
    double? GainDb = null,
    EndAction? EndAction = null,
    Fade? In = null,
    Fade? Out = null,
    TimeSpan? CueIn = null,
    TimeSpan? CueOut = null,
    TrackAudioSettings? Audio = null);

public sealed record Project(ProjectId Id, string Name, ImmutableArray<ProjectEntry> Entries);
