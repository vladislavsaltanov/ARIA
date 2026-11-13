namespace Aria.Persistence.Tests;

using Aria.Core.Model;
using Microsoft.Data.Sqlite;

public sealed class NoteEntryPersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aria-note-{Guid.NewGuid():N}");
    private readonly string _dbPath;
    private readonly string _snapshotPath;

    public NoteEntryPersistenceTests()
    {
        Directory.CreateDirectory(_directory);
        _dbPath = Path.Combine(_directory, "library.db");
        _snapshotPath = Path.Combine(_directory, "show.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Sqlite_RoundTripsNoteBetweenTracks()
    {
        var track = TestFactory.Track("one");
        var note = TestFactory.Note("после фанфар — свет");
        var project = TestFactory.Project("Main", TestFactory.Entry(track), note);

        using (var store = new SqliteLibraryStore(_dbPath))
        {
            store.Upsert([track], [project]);
        }

        using var reloaded = new SqliteLibraryStore(_dbPath);
        var (_, projects) = reloaded.Load();
        var entries = Assert.Single(projects).Entries;

        Assert.Equal(2, entries.Length);
        Assert.IsType<TrackContent>(entries[0].Content);
        var saved = Assert.IsType<NoteContent>(entries[1].Content);
        Assert.Equal("после фанфар — свет", saved.Text);
        Assert.Equal(note.Id, entries[1].Id);
    }

    [Fact]
    public void Sqlite_MigratesLegacyEntriesTable_ThatForbidsNullTrackId()
    {
        var track = TestFactory.Track("one");
        CreateLegacyDatabase(track);

        using (var store = new SqliteLibraryStore(_dbPath))
        {
            var (tracks, projects) = store.Load();
            Assert.Single(tracks);
            Assert.IsType<TrackContent>(Assert.Single(Assert.Single(projects).Entries).Content);
        }

        var note = TestFactory.Note("заметка после миграции");
        using (var store = new SqliteLibraryStore(_dbPath))
        {
            var (tracks, projects) = store.Load();
            store.Upsert(tracks, [projects[0] with { Entries = [.. projects[0].Entries, note] }]);
        }

        using var final = new SqliteLibraryStore(_dbPath);
        var (_, finalProjects) = final.Load();
        Assert.Equal(2, Assert.Single(finalProjects).Entries.Length);
        Assert.Equal("заметка после миграции", Assert.IsType<NoteContent>(Assert.Single(finalProjects).Entries[1].Content).Text);
    }

    [Fact]
    public void Snapshot_RoundTripsNote()
    {
        var track = TestFactory.Track("one");
        var note = TestFactory.Note("кусок сценария");
        var project = TestFactory.Project("Main", TestFactory.Entry(track), note);

        using (var store = new JsonSnapshotStore(_snapshotPath))
        {
            store.Save(Document(track, project));
        }

        using var reloaded = new JsonSnapshotStore(_snapshotPath);
        var loaded = reloaded.LoadLatest();
        Assert.NotNull(loaded);
        var entries = loaded!.Projects[0].Entries;

        Assert.Equal(2, entries.Length);
        Assert.Equal("кусок сценария", Assert.IsType<NoteContent>(entries[1].Content).Text);
    }

    [Fact]
    public void Snapshot_BlankNote_IsDropped()
    {
        var track = TestFactory.Track("one");
        var project = TestFactory.Project("Main", TestFactory.Entry(track));
        using (var store = new JsonSnapshotStore(_snapshotPath))
        {
            store.Save(Document(track, project));
        }
        var json = File.ReadAllText(_snapshotPath);
        var pos = json.LastIndexOf(']');
        json = json[..pos] + ",{\"Id\":\"" + Guid.NewGuid() + "\",\"TrackId\":null,\"Text\":\"   \"}" + json[pos..];
        File.WriteAllText(_snapshotPath, json);

        using var reloaded = new JsonSnapshotStore(_snapshotPath);
        var entries = reloaded.LoadLatest()!.Projects[0].Entries;

        Assert.Single(entries);
        Assert.IsType<TrackContent>(entries[0].Content);
    }

    private static ShowDocument Document(Track track, Project project) => new(
        [track],
        [project],
        project.Id,
        [],
        0,
        TimeSpan.FromSeconds(1),
        TimeSpan.Zero,
        false,
        [],
        DateTimeOffset.UtcNow);

    private void CreateLegacyDatabase(Track track)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath};Pooling=false");
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
            CREATE TABLE projects(
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                position INTEGER NOT NULL)
            """);
        Execute(connection, """
            CREATE TABLE project_entries(
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
                track_id TEXT NOT NULL,
                position INTEGER NOT NULL,
                overrides_json TEXT)
            """);
        Execute(connection, $"INSERT INTO tracks(id, file_path, default_name, duration_ticks, gain_db, end_action) VALUES('{track.Id.Value}', '{track.FilePath}', '{track.DefaultName}', {track.Duration.Ticks}, 0, 0)");
        Execute(connection, $"INSERT INTO projects(id, name, position) VALUES('{Guid.NewGuid()}', 'Old', 0)");
        Execute(connection, $"INSERT INTO project_entries(id, project_id, track_id, position, overrides_json) SELECT '{Guid.NewGuid()}', id, '{track.Id.Value}', 0, NULL FROM projects");
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
