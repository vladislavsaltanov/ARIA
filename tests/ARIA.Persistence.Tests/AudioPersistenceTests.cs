namespace Aria.Persistence.Tests;

using System.Collections.Immutable;
using Aria.Core.Model;
using Microsoft.Data.Sqlite;

public sealed class AudioPersistenceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"aria-lib-audio-{Guid.NewGuid():N}.db");
    private readonly string _snapPath = Path.Combine(Path.GetTempPath(), $"aria-snap-audio-{Guid.NewGuid():N}.json");

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

    private static TrackAudioSettings NonFlatAudio() => new(
        GainDb: -4.5,
        Pan: 0.25,
        Eq: new AudioEq([.. AudioEq.DefaultFrequencies.Select((f, i) => new EqBand(f, i - 3, 0.7f + i * 0.1f))]));

    private static GlobalAudioSettings NonFlatGlobal() => new(
        Pan: -0.5,
        Mono: true,
        HpfHz: 80,
        Eq: new AudioEq([.. AudioEq.DefaultFrequencies.Select((f, i) => new EqBand(f, 2 - i, 1.2f))]),
        Limiter: new LimiterSettings(true, -3.0, 250.0),
        NormalizeTargetLufs: -23.5,
        MeterZones: new LufsMeterZones(-18.0, -12.0, -4.0));

    private static void AssertAudioEqual(TrackAudioSettings expected, TrackAudioSettings? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.GainDb, actual.GainDb);
        Assert.Equal(expected.Pan, actual.Pan);
        Assert.Equal(expected.Eq.Bands.Length, actual.Eq.Bands.Length);
        for (var i = 0; i < expected.Eq.Bands.Length; i++)
        {
            Assert.Equal(expected.Eq.Bands[i].FrequencyHz, actual.Eq.Bands[i].FrequencyHz);
            Assert.Equal(expected.Eq.Bands[i].GainDb, actual.Eq.Bands[i].GainDb);
            Assert.Equal(expected.Eq.Bands[i].Q, actual.Eq.Bands[i].Q);
        }
    }

    [Fact]
    public void Library_TrackAudio_RoundTripsNonFlatEq()
    {
        var audio = NonFlatAudio();
        var track = TestFactory.Track("eq") with { Defaults = TestFactory.Track("eq").Defaults with { Audio = audio } };
        using (var store = new SqliteLibraryStore(_dbPath))
        {
            store.Upsert([track], []);
        }

        using (var store = new SqliteLibraryStore(_dbPath))
        {
            var loaded = store.Load();

            var reloaded = Assert.Single(loaded.Tracks);
            AssertAudioEqual(audio, reloaded.Defaults.Audio);
        }
    }

    [Fact]
    public void Library_TrackAudio_Null_RoundTripsAsNull()
    {
        var track = TestFactory.Track("flat");
        Assert.Null(track.Defaults.Audio);
        using (var store = new SqliteLibraryStore(_dbPath))
        {
            store.Upsert([track], []);
        }

        using (var store = new SqliteLibraryStore(_dbPath))
        {
            var loaded = store.Load();

            Assert.Null(Assert.Single(loaded.Tracks).Defaults.Audio);
        }
    }

    [Fact]
    public void Library_LegacyTrackRowWithoutAudioColumn_LoadsNullAudio()
    {
        var id = Guid.NewGuid();
        using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
        {
            connection.Open();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    CREATE TABLE tracks(
                        id TEXT PRIMARY KEY, file_path TEXT NOT NULL, default_name TEXT NOT NULL,
                        duration_ticks INTEGER NOT NULL, gain_db REAL NOT NULL, end_action INTEGER NOT NULL,
                        fade_in_ticks INTEGER, fade_in_curve INTEGER, fade_out_ticks INTEGER,
                        fade_out_curve INTEGER, markers_json TEXT)
                    """;
                command.ExecuteNonQuery();
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "INSERT INTO tracks(id, file_path, default_name, duration_ticks, gain_db, end_action) " +
                    $"VALUES('{id}', '/audio/old.flac', 'old', 10000000, 0, 3)";
                command.ExecuteNonQuery();
            }
        }

        using (var store = new SqliteLibraryStore(_dbPath))
        {
            var loaded = store.Load();

            var track = Assert.Single(loaded.Tracks);
            Assert.Equal(id, track.Id.Value);
            Assert.Null(track.Defaults.Audio);
        }
    }

    [Fact]
    public void Library_EntryOverrideAudio_RoundTrips()
    {
        var audio = NonFlatAudio();
        var track = TestFactory.Track("base");
        var entry = TestFactory.Entry(track, new PlaylistOverrides(Name: "override", Audio: audio));
        var playlist = TestFactory.Playlist("Main", entry);
        using (var store = new SqliteLibraryStore(_dbPath))
        {
            store.Upsert([track], [playlist]);
        }

        using (var store = new SqliteLibraryStore(_dbPath))
        {
            var loaded = store.Load();

            var reloaded = Assert.Single(loaded.Playlists).Entries[0];
            Assert.NotNull(reloaded.Overrides);
            AssertAudioEqual(audio, reloaded.Overrides.Audio);
        }
    }

    [Fact]
    public void Library_EntryOverrideAudio_Null_RoundTripsAsNull()
    {
        var track = TestFactory.Track("base");
        var entry = TestFactory.Entry(track, new PlaylistOverrides(Name: "plain"));
        var playlist = TestFactory.Playlist("Main", entry);
        using (var store = new SqliteLibraryStore(_dbPath))
        {
            store.Upsert([track], [playlist]);
        }

        using (var store = new SqliteLibraryStore(_dbPath))
        {
            var loaded = store.Load();

            var reloaded = Assert.Single(loaded.Playlists).Entries[0];
            Assert.NotNull(reloaded.Overrides);
            Assert.Null(reloaded.Overrides.Audio);
        }
    }

    [Fact]
    public void Library_LegacyOverridesJsonWithoutAudio_LoadsNullAudio()
    {
        var track = TestFactory.Track("base");
        var entryId = Guid.NewGuid();
        var playlistId = Guid.NewGuid();
        using (var store = new SqliteLibraryStore(_dbPath))
        {
            store.Upsert([track], []);
        }
        using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
        {
            connection.Open();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"INSERT INTO playlists(id, name, position) VALUES('{playlistId}', 'Old', 0)";
                command.ExecuteNonQuery();
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "INSERT INTO playlist_entries(id, playlist_id, track_id, position, overrides_json) " +
                    $"VALUES('{entryId}', '{playlistId}', '{track.Id.Value}', 0, '{{\"Name\":\"legacy\"}}')";
                command.ExecuteNonQuery();
            }
        }

        using (var store = new SqliteLibraryStore(_dbPath))
        {
            var loaded = store.Load();

            var reloaded = Assert.Single(loaded.Playlists).Entries[0];
            Assert.NotNull(reloaded.Overrides);
            Assert.Equal("legacy", reloaded.Overrides.Name);
            Assert.Null(reloaded.Overrides.Audio);
        }
    }

    [Fact]
    public void Snapshot_Global_RoundTripsAllFields()
    {
        var global = NonFlatGlobal();
        var document = new ShowDocument([], [], null, [], 0, TimeSpan.FromMilliseconds(100), TimeSpan.Zero, false, [], DateTimeOffset.UtcNow, global);
        using (var store = new JsonSnapshotStore(_snapPath))
        {
            store.Save(document);
        }

        using (var store = new JsonSnapshotStore(_snapPath))
        {
            var loaded = store.LoadLatest();

            Assert.NotNull(loaded);
            Assert.NotNull(loaded.Global);
            Assert.Equal(global.Pan, loaded.Global.Pan);
            Assert.Equal(global.Mono, loaded.Global.Mono);
            Assert.Equal(global.HpfHz, loaded.Global.HpfHz);
            Assert.Equal(global.Limiter, loaded.Global.Limiter);
            Assert.Equal(-23.5, loaded.Global.NormalizeTargetLufs);
            Assert.Equal(new LufsMeterZones(-18.0, -12.0, -4.0), loaded.Global.EffectiveZones);
            Assert.Equal(7, loaded.Global.Eq.Bands.Length);
            for (var i = 0; i < 7; i++)
            {
                Assert.Equal(global.Eq.Bands[i], loaded.Global.Eq.Bands[i]);
            }
        }
    }

    [Fact]
    public void Snapshot_MissingGlobalNode_LoadsDefault()
    {
        File.WriteAllText(_snapPath, """
            {"Tracks":[],"Playlists":[],"Queue":[],"MasterGainDb":0,"PanicFadeTicks":1000000,"ClockElapsedTicks":0,"ClockRunning":false,"Scripts":[],"SavedAt":"2026-09-11T00:00:00Z"}
            """);
        using var store = new JsonSnapshotStore(_snapPath);

        var loaded = store.LoadLatest();

        Assert.NotNull(loaded);
        Assert.Equal(GlobalAudioSettings.Default, loaded.EffectiveGlobal);
    }

    [Fact]
    public void Snapshot_TrackAndEntryAudio_RoundTrip()
    {
        var audio = NonFlatAudio();
        var track = TestFactory.Track("snap") with { Defaults = TestFactory.Track("snap").Defaults with { Audio = audio } };
        var entry = TestFactory.Entry(track, new PlaylistOverrides(Audio: audio));
        var playlist = TestFactory.Playlist("Main", entry);
        var document = new ShowDocument([track], [playlist], playlist.Id, [], 0, TimeSpan.FromMilliseconds(100), TimeSpan.Zero, false, [], DateTimeOffset.UtcNow);
        using (var store = new JsonSnapshotStore(_snapPath))
        {
            store.Save(document);
        }

        using (var store = new JsonSnapshotStore(_snapPath))
        {
            var loaded = store.LoadLatest();

            Assert.NotNull(loaded);
            AssertAudioEqual(audio, Assert.Single(loaded.Tracks).Defaults.Audio);
            AssertAudioEqual(audio, Assert.Single(loaded.Playlists).Entries[0].Overrides!.Audio);
        }
    }

    [Fact]
    public void Snapshot_LegacyTrackWithoutAudio_LoadsNullAudio()
    {
        File.WriteAllText(_snapPath, """
            {"Tracks":[{"Id":"11111111-1111-1111-1111-111111111111","FilePath":"/audio/old.flac","DefaultName":"old","DurationTicks":10000000,"GainDb":0,"EndAction":3}],"Playlists":[],"Queue":[],"MasterGainDb":0,"PanicFadeTicks":1000000,"ClockElapsedTicks":0,"ClockRunning":false,"Scripts":[],"SavedAt":"2026-09-11T00:00:00Z"}
            """);
        using var store = new JsonSnapshotStore(_snapPath);

        var loaded = store.LoadLatest();

        Assert.NotNull(loaded);
        Assert.Null(Assert.Single(loaded.Tracks).Defaults.Audio);
        Assert.Equal(GlobalAudioSettings.Default, loaded.EffectiveGlobal);
    }
}
