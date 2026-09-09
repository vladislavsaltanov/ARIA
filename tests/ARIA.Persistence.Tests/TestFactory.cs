namespace Aria.Persistence.Tests;

using Aria.Core.Model;

public static class TestFactory
{
    public static Track Track(string name, EndAction end = EndAction.Advance) => new(
        TrackId.New(),
        $"/audio/{name}.flac",
        name,
        TimeSpan.FromMinutes(3),
        new TrackDefaults(EndAction: end));

    public static PlaylistEntry Entry(Track track, PlaylistOverrides? overrides = null) =>
        new(EntryId.New(), track.Id, overrides);

    public static Playlist Playlist(string name, params PlaylistEntry[] entries) =>
        new(PlaylistId.New(), name, [.. entries]);
}
