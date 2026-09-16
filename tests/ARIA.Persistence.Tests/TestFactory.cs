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

    public static ProjectEntry Entry(Track track, ProjectOverrides? overrides = null) =>
        new(EntryId.New(), track.Id, overrides);

    public static Project Project(string name, params ProjectEntry[] entries) =>
        new(ProjectId.New(), name, [.. entries]);
}
