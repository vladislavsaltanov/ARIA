namespace Aria.Core.Tests;

using System.Collections.Immutable;
using Aria.Core.Model;

public static class TestShow
{
    public static Track Track(
        string name = "Track",
        EndAction end = EndAction.Advance,
        ImmutableArray<Marker>? markers = null,
        double gainDb = 0,
        Fade? fadeIn = null,
        Fade? fadeOut = null) => new(
            TrackId.New(),
            $"/audio/{name}.flac",
            name,
            TimeSpan.FromMinutes(3),
            new TrackDefaults(
                GainDb: gainDb,
                EndAction: end,
                In: fadeIn,
                Out: fadeOut,
                Markers: markers));

    public static ProjectEntry Entry(Track track, ProjectOverrides? overrides = null) =>
        new(EntryId.New(), new TrackContent(track.Id, overrides));

    public static ProjectEntry Note(string text) =>
        new(EntryId.New(), new NoteContent(text));

    public static Project Project(string name, params ProjectEntry[] entries) =>
        new(ProjectId.New(), name, [.. entries]);
}
