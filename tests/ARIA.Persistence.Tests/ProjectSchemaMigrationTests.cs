namespace Aria.Persistence.Tests;

using Aria.Core.Model;
using Microsoft.Data.Sqlite;

public sealed class ProjectSchemaMigrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"aria-lib-mig-{Guid.NewGuid():N}.db");
    private readonly string _snapPath = Path.Combine(Path.GetTempPath(), $"aria-snap-mig-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        foreach (var file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm", _snapPath, _snapPath + ".tmp" })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public void Migrate_LegacyProjectTables_PreservesData()
    {
        var track = TestFactory.Track("base");
        var entryId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        CreateLegacyDatabase(track, entryId, projectId);

        using (var store = new SqliteLibraryStore(_dbPath))
        {
            var (tracks, projects) = store.Load();

            Assert.Equal(track.Id, Assert.Single(tracks).Id);
            var project = Assert.Single(projects);
            Assert.Equal(projectId, project.Id.Value);
            Assert.Equal("Old", project.Name);
            var entry = Assert.Single(project.Entries);
            Assert.Equal(entryId, entry.Id.Value);
            Assert.Equal(track.Id, entry.TrackId);
            Assert.Equal("legacy", entry.Overrides!.Name);
        }
    }

    [Fact]
    public void Migrate_LegacyTables_RenamedAndIdempotent()
    {
        var track = TestFactory.Track("base");
        CreateLegacyDatabase(track, Guid.NewGuid(), Guid.NewGuid());

        using (var store = new SqliteLibraryStore(_dbPath))
        {
            store.Load();
        }
        Assert.Equal(["project_entries", "projects", "tracks"], TableNames().Order().ToArray());

        using (var store = new SqliteLibraryStore(_dbPath))
        {
            var (tracks, projects) = store.Load();

            Assert.Equal(track.Id, Assert.Single(tracks).Id);
            Assert.Single(projects);
        }
        Assert.Equal(["project_entries", "projects", "tracks"], TableNames().Order().ToArray());
    }

    [Fact]
    public void Save_WritesProjectsPropertyName()
    {
        var track = TestFactory.Track("base");
        var project = TestFactory.Project("Main", TestFactory.Entry(track));
        var document = new ShowDocument([track], [project], project.Id, [], 0, TimeSpan.FromMilliseconds(100), TimeSpan.Zero, false, [], DateTimeOffset.UtcNow);
        using (var store = new JsonSnapshotStore(_snapPath))
        {
            store.Save(document);
        }

        var json = File.ReadAllText(_snapPath);
        Assert.Contains("\"Projects\"", json);
        Assert.DoesNotContain("\"Playlists\"", json);
    }

    [Fact]
    public void Load_LegacyPlaylistsProperty_ReadsProjects()
    {
        var projectId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        File.WriteAllText(_snapPath, """
            {"Tracks":[],"Playlists":[{"Id":"PROJECT","Name":"Old","Entries":[{"Id":"ENTRY","TrackId":"TRACK","Overrides":null}]}],"Queue":[],"MasterGainDb":0,"PanicFadeTicks":1000000,"ClockElapsedTicks":0,"ClockRunning":false,"Scripts":[],"SavedAt":"2026-09-11T00:00:00Z"}
            """.Replace("PROJECT", projectId.ToString()).Replace("ENTRY", Guid.NewGuid().ToString()).Replace("TRACK", trackId.ToString()));
        using var store = new JsonSnapshotStore(_snapPath);

        var loaded = store.LoadLatest();

        Assert.NotNull(loaded);
        var project = Assert.Single(loaded.Projects);
        Assert.Equal(projectId, project.Id.Value);
        Assert.Equal("Old", project.Name);
        Assert.Equal(trackId, Assert.Single(project.Entries).TrackId.Value);
    }

    private void CreateLegacyDatabase(Track track, Guid entryId, Guid projectId)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        Execute(connection, """
            CREATE TABLE tracks(
                id TEXT PRIMARY KEY,
                file_path TEXT NOT NULL,
                default_name TEXT NOT NULL,
                duration_ticks INTEGER NOT NULL,
                gain_db REAL NOT NULL,
                end_action INTEGER NOT NULL,
                fade_in_ticks INTEGER,
                fade_in_curve INTEGER,
                fade_out_ticks INTEGER,
                fade_out_curve INTEGER,
                markers_json TEXT,
                audio_json TEXT)
            """);
        Execute(connection, """
            CREATE TABLE playlists(
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                position INTEGER NOT NULL)
            """);
        Execute(connection, """
            CREATE TABLE playlist_entries(
                id TEXT PRIMARY KEY,
                playlist_id TEXT NOT NULL REFERENCES playlists(id) ON DELETE CASCADE,
                track_id TEXT NOT NULL,
                position INTEGER NOT NULL,
                overrides_json TEXT)
            """);
        Execute(connection, "INSERT INTO tracks(id, file_path, default_name, duration_ticks, gain_db, end_action) " +
            $"VALUES('{track.Id.Value}', '{track.FilePath}', '{track.DefaultName}', {track.Duration.Ticks}, 0, 0)");
        Execute(connection, $"INSERT INTO playlists(id, name, position) VALUES('{projectId}', 'Old', 0)");
        Execute(connection, "INSERT INTO playlist_entries(id, playlist_id, track_id, position, overrides_json) " +
            $"VALUES('{entryId}', '{projectId}', '{track.Id.Value}', 0, '{{\"Name\":\"legacy\"}}')");
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private List<string> TableNames()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }
        return names;
    }
}
