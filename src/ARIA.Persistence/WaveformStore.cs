namespace Aria.Persistence;

using System.Collections.Immutable;
using Aria.Core.Model;
using Microsoft.Data.Sqlite;

public interface IWaveformStore : IDisposable
{
    void Save(WaveformPeaks peaks);

    WaveformPeaks? Load(TrackId trackId);
}

public sealed class SqliteWaveformStore : IWaveformStore
{
    private const int PointBytes = 8;

    private readonly string _connectionString;

    public SqliteWaveformStore(string path)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        using var connection = Open();
        EnsureSchema(connection);
    }

    public void Save(WaveformPeaks peaks)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO waveform(track_id, points_per_second, sample_rate, points)
            VALUES($id, $points_per_second, $sample_rate, $points)
            ON CONFLICT(track_id) DO UPDATE SET
                points_per_second = excluded.points_per_second,
                sample_rate = excluded.sample_rate,
                points = excluded.points
            """;
        command.Parameters.AddWithValue("$id", peaks.TrackId.Value);
        command.Parameters.AddWithValue("$points_per_second", peaks.PointsPerSecond);
        command.Parameters.AddWithValue("$sample_rate", peaks.SampleRate);
        command.Parameters.AddWithValue("$points", Encode(peaks.Points));
        command.ExecuteNonQuery();
    }

    public WaveformPeaks? Load(TrackId trackId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT points_per_second, sample_rate, points FROM waveform WHERE track_id = $id";
        command.Parameters.AddWithValue("$id", trackId.Value);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }
        var blob = (byte[])reader.GetValue(2);
        if (blob.Length % PointBytes != 0)
        {
            return null;
        }
        var count = blob.Length / PointBytes;
        var points = ImmutableArray.CreateBuilder<PeakPoint>(count);
        for (var i = 0; i < count; i++)
        {
            var offset = i * PointBytes;
            points.Add(new PeakPoint(ReadFloat(blob, offset), ReadFloat(blob, offset + 4)));
        }
        return new WaveformPeaks(trackId, reader.GetInt32(0), reader.GetInt32(1), points.MoveToImmutable());
    }

    public void Dispose()
    {
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        Execute(connection, "PRAGMA journal_mode=WAL");
        return connection;
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS waveform(
                track_id TEXT PRIMARY KEY,
                points_per_second INTEGER NOT NULL,
                sample_rate INTEGER NOT NULL,
                points BLOB NOT NULL)
            """);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static byte[] Encode(ImmutableArray<PeakPoint> points)
    {
        var blob = new byte[points.Length * PointBytes];
        for (var i = 0; i < points.Length; i++)
        {
            WriteFloat(blob, i * PointBytes, points[i].Min);
            WriteFloat(blob, i * PointBytes + 4, points[i].Max);
        }
        return blob;
    }

    private static void WriteFloat(byte[] blob, int offset, float value)
    {
        var bits = BitConverter.SingleToUInt32Bits(value);
        blob[offset] = (byte)bits;
        blob[offset + 1] = (byte)(bits >> 8);
        blob[offset + 2] = (byte)(bits >> 16);
        blob[offset + 3] = (byte)(bits >> 24);
    }

    private static float ReadFloat(byte[] blob, int offset)
    {
        var bits = (uint)(blob[offset] | (blob[offset + 1] << 8) | (blob[offset + 2] << 16) | (blob[offset + 3] << 24));
        return BitConverter.UInt32BitsToSingle(bits);
    }
}