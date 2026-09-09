namespace Aria.Persistence;

using System.Collections.Immutable;
using Aria.Core.Model;
using Microsoft.Data.Sqlite;

public interface ILibraryStore : IDisposable
{
    void Upsert(ImmutableArray<Track> tracks, ImmutableArray<Playlist> playlists);

    (ImmutableArray<Track> Tracks, ImmutableArray<Playlist> Playlists) Load();
}

public sealed class SqliteLibraryStore : ILibraryStore
{
    private readonly string _connectionString;

    public SqliteLibraryStore(string path)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        using var connection = Open();
        EnsureSchema(connection);
    }

    public void Upsert(ImmutableArray<Track> tracks, ImmutableArray<Playlist> playlists)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        Execute(connection, transaction, "DELETE FROM playlist_entries");
        Execute(connection, transaction, "DELETE FROM playlists");
        Execute(connection, transaction, "DELETE FROM tracks");

        foreach (var track in tracks)
        {
            var dto = TrackMapper.ToDto(track);
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO tracks(id, file_path, default_name, duration_ticks, gain_db, end_action,
                    fade_in_ticks, fade_in_curve, fade_out_ticks, fade_out_curve, markers_json)
                VALUES($id, $file_path, $default_name, $duration_ticks, $gain_db, $end_action,
                    $fade_in_ticks, $fade_in_curve, $fade_out_ticks, $fade_out_curve, $markers_json)
                """;
            command.Parameters.AddWithValue("$id", dto.Id);
            command.Parameters.AddWithValue("$file_path", dto.FilePath);
            command.Parameters.AddWithValue("$default_name", dto.DefaultName);
            command.Parameters.AddWithValue("$duration_ticks", dto.DurationTicks);
            command.Parameters.AddWithValue("$gain_db", dto.GainDb);
            command.Parameters.AddWithValue("$end_action", dto.EndAction);
            command.Parameters.AddWithValue("$fade_in_ticks", (object?)dto.FadeInTicks ?? DBNull.Value);
            command.Parameters.AddWithValue("$fade_in_curve", (object?)dto.FadeInCurve ?? DBNull.Value);
            command.Parameters.AddWithValue("$fade_out_ticks", (object?)dto.FadeOutTicks ?? DBNull.Value);
            command.Parameters.AddWithValue("$fade_out_curve", (object?)dto.FadeOutCurve ?? DBNull.Value);
            command.Parameters.AddWithValue("$markers_json", dto.Markers is null ? DBNull.Value : DtoJson.Serialize(dto.Markers));
            command.ExecuteNonQuery();
        }

        for (var i = 0; i < playlists.Length; i++)
        {
            var playlistDto = PlaylistMapper.ToDto(playlists[i]);
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO playlists(id, name, position) VALUES($id, $name, $position)";
            command.Parameters.AddWithValue("$id", playlistDto.Id);
            command.Parameters.AddWithValue("$name", playlistDto.Name);
            command.Parameters.AddWithValue("$position", i);
            command.ExecuteNonQuery();

            for (var j = 0; j < playlistDto.Entries.Count; j++)
            {
                var entryDto = playlistDto.Entries[j];
                var entryCommand = connection.CreateCommand();
                entryCommand.Transaction = transaction;
                entryCommand.CommandText = """
                    INSERT INTO playlist_entries(id, playlist_id, track_id, position, overrides_json)
                    VALUES($id, $playlist_id, $track_id, $position, $overrides_json)
                    """;
                entryCommand.Parameters.AddWithValue("$id", entryDto.Id);
                entryCommand.Parameters.AddWithValue("$playlist_id", playlistDto.Id);
                entryCommand.Parameters.AddWithValue("$track_id", entryDto.TrackId);
                entryCommand.Parameters.AddWithValue("$position", j);
                entryCommand.Parameters.AddWithValue("$overrides_json", entryDto.Overrides is null ? DBNull.Value : DtoJson.Serialize(entryDto.Overrides));
                entryCommand.ExecuteNonQuery();
            }
        }
        transaction.Commit();
    }

    public (ImmutableArray<Track> Tracks, ImmutableArray<Playlist> Playlists) Load()
    {
        using var connection = Open();

        var tracks = new List<Track>();
        var trackCommand = connection.CreateCommand();
        trackCommand.CommandText = "SELECT id, file_path, default_name, duration_ticks, gain_db, end_action, fade_in_ticks, fade_in_curve, fade_out_ticks, fade_out_curve, markers_json FROM tracks";
        using (var reader = trackCommand.ExecuteReader())
        {
            while (reader.Read())
            {
                var dto = new TrackDto
                {
                    Id = reader.GetGuid(0),
                    FilePath = reader.GetString(1),
                    DefaultName = reader.GetString(2),
                    DurationTicks = reader.GetInt64(3),
                    GainDb = reader.GetDouble(4),
                    EndAction = reader.GetInt32(5),
                    FadeInTicks = reader.IsDBNull(6) ? null : reader.GetInt64(6),
                    FadeInCurve = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    FadeOutTicks = reader.IsDBNull(8) ? null : reader.GetInt64(8),
                    FadeOutCurve = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                    Markers = reader.IsDBNull(10) ? null : DtoJson.Deserialize<List<MarkerDto>>(reader.GetString(10)),
                };
                tracks.Add(TrackMapper.ToDomain(dto));
            }
        }

        var playlistRows = new List<(Guid Id, string Name)>();
        var playlistCommand = connection.CreateCommand();
        playlistCommand.CommandText = "SELECT id, name FROM playlists ORDER BY position";
        using (var reader = playlistCommand.ExecuteReader())
        {
            while (reader.Read())
            {
                playlistRows.Add((reader.GetGuid(0), reader.GetString(1)));
            }
        }

        var entriesByPlaylist = new Dictionary<Guid, List<PlaylistEntry>>();
        var entryCommand = connection.CreateCommand();
        entryCommand.CommandText = "SELECT id, playlist_id, track_id, overrides_json FROM playlist_entries ORDER BY playlist_id, position";
        using (var reader = entryCommand.ExecuteReader())
        {
            while (reader.Read())
            {
                var dto = new EntryDto
                {
                    Id = reader.GetGuid(0),
                    TrackId = reader.GetGuid(2),
                    Overrides = reader.IsDBNull(3) ? null : DtoJson.Deserialize<OverridesDto>(reader.GetString(3)),
                };
                var playlistId = reader.GetGuid(1);
                if (!entriesByPlaylist.TryGetValue(playlistId, out var list))
                {
                    list = [];
                    entriesByPlaylist[playlistId] = list;
                }
                list.Add(PlaylistMapper.ToDomain(dto));
            }
        }

        var playlists = new List<Playlist>();
        foreach (var (id, name) in playlistRows)
        {
            var entries = entriesByPlaylist.TryGetValue(id, out var list)
                ? list.ToImmutableArray()
                : ImmutableArray<PlaylistEntry>.Empty;
            playlists.Add(new Playlist(new PlaylistId(id), name, entries));
        }

        return ([.. tracks], [.. playlists]);
    }

    public void Dispose()
    {
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        Execute(connection, null, "PRAGMA journal_mode=WAL");
        Execute(connection, null, "PRAGMA foreign_keys=ON");
        return connection;
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        Execute(connection, null, """
            CREATE TABLE IF NOT EXISTS tracks(
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
                markers_json TEXT)
            """);
        Execute(connection, null, """
            CREATE TABLE IF NOT EXISTS playlists(
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                position INTEGER NOT NULL)
            """);
        Execute(connection, null, """
            CREATE TABLE IF NOT EXISTS playlist_entries(
                id TEXT PRIMARY KEY,
                playlist_id TEXT NOT NULL REFERENCES playlists(id) ON DELETE CASCADE,
                track_id TEXT NOT NULL,
                position INTEGER NOT NULL,
                overrides_json TEXT)
            """);
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
