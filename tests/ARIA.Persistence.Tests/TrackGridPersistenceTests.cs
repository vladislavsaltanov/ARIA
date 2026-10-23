namespace Aria.Persistence.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.Runtime;
using Aria.Core.State;
using Microsoft.Data.Sqlite;

public sealed class TrackGridPersistenceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"aria-lib-grid-{Guid.NewGuid():N}.db");
    private readonly string _snapPath = Path.Combine(Path.GetTempPath(), $"aria-snap-grid-{Guid.NewGuid():N}.json");

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
    public void Library_TrackGrid_RoundTrips()
    {
        var plain = TestFactory.Track("bpm");
        var track = plain with { Defaults = plain.Defaults with { Grid = new BeatGrid(128.5, 3, TimeSpan.FromMilliseconds(250)) } };
        using (var store = new SqliteLibraryStore(_dbPath))
        {
            store.Upsert([track], []);
        }

        using (var store = new SqliteLibraryStore(_dbPath))
        {
            var reloaded = Assert.Single(store.Load().Tracks);
            Assert.Equal(new BeatGrid(128.5, 3, TimeSpan.FromMilliseconds(250)), reloaded.Defaults.Grid);
        }
    }

    [Fact]
    public void Library_TrackGrid_MissingByDefault()
    {
        var track = TestFactory.Track("plain");
        using (var store = new SqliteLibraryStore(_dbPath))
        {
            store.Upsert([track], []);
        }

        using (var store = new SqliteLibraryStore(_dbPath))
        {
            var reloaded = Assert.Single(store.Load().Tracks);
            Assert.Null(reloaded.Defaults.Grid);
        }
    }

    [Fact]
    public void Migrate_LegacyTracksWithoutBpm_LoadsNull()
    {
        var track = TestFactory.Track("legacy");
        CreateLegacyDatabase(track);

        using (var store = new SqliteLibraryStore(_dbPath))
        {
            var loaded = store.Load();
            var reloaded = Assert.Single(loaded.Tracks);
            Assert.Equal(track.Id, reloaded.Id);
            Assert.Null(reloaded.Defaults.Grid);
        }

        using (var store = new SqliteLibraryStore(_dbPath))
        {
            Assert.Null(Assert.Single(store.Load().Tracks).Defaults.Grid);
        }
    }

    [Fact]
    public void Snapshot_TrackGrid_RoundTrips()
    {
        var plain = TestFactory.Track("bpm");
        var track = plain with { Defaults = plain.Defaults with { Grid = new BeatGrid(100, 7, TimeSpan.FromSeconds(1.5)) } };
        var project = TestFactory.Project("Main", TestFactory.Entry(track));
        var document = new ShowDocument([track], [project], project.Id, [], 0, TimeSpan.FromMilliseconds(100), TimeSpan.Zero, false, [], DateTimeOffset.UtcNow);
        using (var store = new JsonSnapshotStore(_snapPath))
        {
            store.Save(document);
        }

        using (var store = new JsonSnapshotStore(_snapPath))
        {
            var loaded = store.LoadLatest();
            Assert.NotNull(loaded);
            Assert.Equal(new BeatGrid(100, 7, TimeSpan.FromSeconds(1.5)), Assert.Single(loaded.Tracks).Defaults.Grid);
        }
    }

    [Fact]
    public void ShowAutosaver_PersistsTrackGrid_ToSnapshotAndLibrary()
    {
        var plain = TestFactory.Track("bpm");
        var track = plain with { Defaults = plain.Defaults with { Grid = new BeatGrid(133.0, 5, TimeSpan.FromSeconds(2)) } };
        var project = TestFactory.Project("Main", TestFactory.Entry(track));
        using var libStore = new SqliteLibraryStore(_dbPath);
        libStore.Upsert([track], [project]);

        using var snapStore = new JsonSnapshotStore(_snapPath);
        using var bus = new CommandBus(new ShowController(new StubEngine()));
        using var autosaver = new ShowAutosaver(bus, snapStore, TimeSpan.FromMilliseconds(50), () => [track], libStore);

        bus.Submit(new ClientId("test"), 1, new CreateProject("NewProject"));
        autosaver.FlushNow();

        var reloadedSnap = snapStore.LoadLatest();
        Assert.NotNull(reloadedSnap);
        Assert.Equal(new BeatGrid(133.0, 5, TimeSpan.FromSeconds(2)), Assert.Single(reloadedSnap.Tracks).Defaults.Grid);

        var reloadedLib = libStore.Load();
        Assert.Equal(new BeatGrid(133.0, 5, TimeSpan.FromSeconds(2)), Assert.Single(reloadedLib.Tracks).Defaults.Grid);
    }

    [Fact]
    public void Migrate_LegacyBpmOnlyColumns_LoadsGridWithDefaults()
    {
        var track = TestFactory.Track("legacy-bpm");
        CreateLegacyDatabase(track);
        using (var connection = new SqliteConnection($"Data Source={_dbPath};Pooling=false"))
        {
            connection.Open();
            Execute(connection, "ALTER TABLE tracks ADD COLUMN bpm REAL");
            Execute(connection, $"UPDATE tracks SET bpm = 100 WHERE id = '{track.Id.Value}'");
        }

        using (var store = new SqliteLibraryStore(_dbPath))
        {
            Assert.Equal(new BeatGrid(100, 4, TimeSpan.Zero), Assert.Single(store.Load().Tracks).Defaults.Grid);
        }
    }

    [Fact]
    public void Snapshot_LegacyBpmJson_LoadsGridWithDefaults()
    {
        var plain = TestFactory.Track("legacy-json");
        var track = plain with { Defaults = plain.Defaults with { Grid = new BeatGrid(100, 3, TimeSpan.FromSeconds(1)) } };
        var project = TestFactory.Project("Main", TestFactory.Entry(track));
        var document = new ShowDocument([track], [project], project.Id, [], 0, TimeSpan.FromMilliseconds(100), TimeSpan.Zero, false, [], DateTimeOffset.UtcNow);
        using (var store = new JsonSnapshotStore(_snapPath))
        {
            store.Save(document);
        }
        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(_snapPath))!.AsObject();
        foreach (var node in root.First(p => string.Equals(p.Key, "tracks", StringComparison.OrdinalIgnoreCase)).Value!.AsArray())
        {
            var dto = node!.AsObject();
            foreach (var key in dto.Select(p => p.Key).Where(k => k.Contains("beat", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                dto.Remove(key);
            }
        }
        File.WriteAllText(_snapPath, root.ToJsonString());

        using (var store = new JsonSnapshotStore(_snapPath))
        {
            var loaded = store.LoadLatest();
            Assert.NotNull(loaded);
            Assert.Equal(new BeatGrid(100, 4, TimeSpan.Zero), Assert.Single(loaded.Tracks).Defaults.Grid);
        }
    }

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
        Execute(connection, "INSERT INTO tracks(id, file_path, default_name, duration_ticks, gain_db, end_action) " +
            $"VALUES('{track.Id.Value}', '{track.FilePath}', '{track.DefaultName}', {track.Duration.Ticks}, 0, 0)");
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private sealed class StubEngine : IAudioEngine
    {
        public event Action<StreamEvent>? Events
        {
            add { }
            remove { }
        }

        public StreamHandle StartStream(TrackSource source, StreamOptions options) => new(0);

        public void Transport(StreamHandle handle, TransportCommand command)
        {
        }

        public void SetMix(StreamHandle handle, MixParameters mix)
        {
        }

        public void Seek(StreamHandle handle, TimeSpan position)
        {
        }

        public void SetMasterGain(double gainDb)
        {
        }

        public void SetSmoothing(Smoothing smoothing)
        {
        }

        public void Panic(PanicSpec spec)
        {
        }

        public void DisposeStream(StreamHandle handle)
        {
        }
    }
}
