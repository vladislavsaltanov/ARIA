namespace Aria.Persistence;

using System.Collections.Immutable;
using Aria.Core.Model;
using Microsoft.Data.Sqlite;

public interface ILibraryStore : IDisposable
{
    void Upsert(ImmutableArray<Track> tracks, ImmutableArray<Project> projects);

    (ImmutableArray<Track> Tracks, ImmutableArray<Project> Projects) Load();
}

public sealed class SqliteLibraryStore : ILibraryStore
{
    private readonly string _connectionString;

    public SqliteLibraryStore(string path)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        using var connection = Open();
        MigrateLegacySchema(connection);
        EnsureSchema(connection);
        EnsureAudioColumn(connection);
    }

    public void Upsert(ImmutableArray<Track> tracks, ImmutableArray<Project> projects)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        Execute(connection, transaction, "DELETE FROM project_entries");
        Execute(connection, transaction, "DELETE FROM projects");
        Execute(connection, transaction, "DELETE FROM tracks");

        foreach (var track in tracks)
        {
            var dto = TrackMapper.ToDto(track);
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO tracks(id, file_path, default_name, duration_ticks, gain_db, end_action,
                    fade_in_ticks, fade_in_curve, fade_out_ticks, fade_out_curve, markers_json, audio_json)
                VALUES($id, $file_path, $default_name, $duration_ticks, $gain_db, $end_action,
                    $fade_in_ticks, $fade_in_curve, $fade_out_ticks, $fade_out_curve, $markers_json, $audio_json)
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
            command.Parameters.AddWithValue("$audio_json", dto.Audio is null ? DBNull.Value : DtoJson.Serialize(dto.Audio));
            command.ExecuteNonQuery();
        }

        for (var i = 0; i < projects.Length; i++)
        {
            var projectDto = ProjectMapper.ToDto(projects[i]);
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO projects(id, name, position) VALUES($id, $name, $position)";
            command.Parameters.AddWithValue("$id", projectDto.Id);
            command.Parameters.AddWithValue("$name", projectDto.Name);
            command.Parameters.AddWithValue("$position", i);
            command.ExecuteNonQuery();

            for (var j = 0; j < projectDto.Entries.Count; j++)
            {
                var entryDto = projectDto.Entries[j];
                var entryCommand = connection.CreateCommand();
                entryCommand.Transaction = transaction;
                entryCommand.CommandText = """
                    INSERT INTO project_entries(id, project_id, track_id, position, overrides_json)
                    VALUES($id, $project_id, $track_id, $position, $overrides_json)
                    """;
                entryCommand.Parameters.AddWithValue("$id", entryDto.Id);
                entryCommand.Parameters.AddWithValue("$project_id", projectDto.Id);
                entryCommand.Parameters.AddWithValue("$track_id", entryDto.TrackId);
                entryCommand.Parameters.AddWithValue("$position", j);
                entryCommand.Parameters.AddWithValue("$overrides_json", entryDto.Overrides is null ? DBNull.Value : DtoJson.Serialize(entryDto.Overrides));
                entryCommand.ExecuteNonQuery();
            }
        }
        transaction.Commit();
    }

    public (ImmutableArray<Track> Tracks, ImmutableArray<Project> Projects) Load()
    {
        using var connection = Open();

        var tracks = new List<Track>();
        var trackCommand = connection.CreateCommand();
        trackCommand.CommandText = "SELECT id, file_path, default_name, duration_ticks, gain_db, end_action, fade_in_ticks, fade_in_curve, fade_out_ticks, fade_out_curve, markers_json, audio_json FROM tracks";
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
                    Audio = reader.IsDBNull(11) ? null : DtoJson.Deserialize<TrackAudioDto>(reader.GetString(11)),
                };
                tracks.Add(TrackMapper.ToDomain(dto));
            }
        }

        var projectRows = new List<(Guid Id, string Name)>();
        var projectCommand = connection.CreateCommand();
        projectCommand.CommandText = "SELECT id, name FROM projects ORDER BY position";
        using (var reader = projectCommand.ExecuteReader())
        {
            while (reader.Read())
            {
                projectRows.Add((reader.GetGuid(0), reader.GetString(1)));
            }
        }

        var entriesByProject = new Dictionary<Guid, List<ProjectEntry>>();
        var entryCommand = connection.CreateCommand();
        entryCommand.CommandText = "SELECT id, project_id, track_id, overrides_json FROM project_entries ORDER BY project_id, position";
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
                var projectId = reader.GetGuid(1);
                if (!entriesByProject.TryGetValue(projectId, out var list))
                {
                    list = [];
                    entriesByProject[projectId] = list;
                }
                list.Add(ProjectMapper.ToDomain(dto));
            }
        }

        var projects = new List<Project>();
        foreach (var (id, name) in projectRows)
        {
            var entries = entriesByProject.TryGetValue(id, out var list)
                ? list.ToImmutableArray()
                : ImmutableArray<ProjectEntry>.Empty;
            projects.Add(new Project(new ProjectId(id), name, entries));
        }

        return ([.. tracks], [.. projects]);
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
                markers_json TEXT,
                audio_json TEXT)
            """);
        Execute(connection, null, """
            CREATE TABLE IF NOT EXISTS projects(
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                position INTEGER NOT NULL)
            """);
        Execute(connection, null, """
            CREATE TABLE IF NOT EXISTS project_entries(
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
                track_id TEXT NOT NULL,
                position INTEGER NOT NULL,
                overrides_json TEXT)
            """);
    }

    private static void MigrateLegacySchema(SqliteConnection connection)
    {
        if (TableExists(connection, "playlists") && !TableExists(connection, "projects"))
        {
            Execute(connection, null, "ALTER TABLE playlists RENAME TO projects");
        }
        if (TableExists(connection, "playlist_entries") && !TableExists(connection, "project_entries"))
        {
            Execute(connection, null, "ALTER TABLE playlist_entries RENAME TO project_entries");
            Execute(connection, null, "ALTER TABLE project_entries RENAME COLUMN playlist_id TO project_id");
        }
    }

    private static bool TableExists(SqliteConnection connection, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name";
        command.Parameters.AddWithValue("$name", name);
        using var reader = command.ExecuteReader();
        return reader.Read();
    }

    private static void EnsureAudioColumn(SqliteConnection connection)
    {
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA table_info(tracks)";
        using var reader = pragma.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetString(1) == "audio_json")
            {
                return;
            }
        }
        Execute(connection, null, "ALTER TABLE tracks ADD COLUMN audio_json TEXT");
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
