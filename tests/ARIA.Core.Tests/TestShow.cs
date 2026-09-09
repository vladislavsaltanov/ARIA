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

    public static PlaylistEntry Entry(Track track, PlaylistOverrides? overrides = null) =>
        new(EntryId.New(), track.Id, overrides);

    public static Playlist Playlist(string name, params PlaylistEntry[] entries) =>
        new(PlaylistId.New(), name, [.. entries]);
}
