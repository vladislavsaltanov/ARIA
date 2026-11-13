namespace Aria.Core.Model;

using System.Collections.Immutable;
using System.Text.Json.Serialization;

public sealed record ProjectEntry(EntryId Id, EntryContent Content);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TrackContent), "track")]
[JsonDerivedType(typeof(NoteContent), "note")]
public abstract record EntryContent;

public sealed record TrackContent(TrackId TrackId, ProjectOverrides? Overrides = null) : EntryContent;

public sealed record NoteContent(string Text) : EntryContent
{
    public const int MaxLength = 2000;

    public static bool TryCreate(string? raw, out NoteContent? note)
    {
        var text = raw?.Trim() ?? string.Empty;
        if (text.Length is 0 or > MaxLength)
        {
            note = null;
            return false;
        }
        note = new NoteContent(text);
        return true;
    }
}

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
    TrackAudioSettings? Audio = null,
    ImmutableArray<Marker>? Markers = null,
    TrackId? Backing = null);

public sealed record Project(ProjectId Id, string Name, ImmutableArray<ProjectEntry> Entries);
