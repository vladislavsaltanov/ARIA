namespace Aria.Persistence.Tests;

using System.Collections.Immutable;
using Aria.Core.Model;
using Microsoft.Data.Sqlite;

public sealed class WaveformStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"aria-wave-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var file = _path + suffix;
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public void RoundTrip_PreservesPeaks()
    {
        var trackId = TrackId.New();
        var points = ImmutableArray.CreateRange(
            Enumerable.Range(0, 250).Select(i => new PeakPoint(-0.5f - i * 0.001f, 0.5f + i * 0.001f)));

        using (var store = new SqliteWaveformStore(_path))
        {
            store.Save(new WaveformPeaks(trackId, 50, 8000, points));
        }

        using (var store = new SqliteWaveformStore(_path))
        {
            var loaded = store.Load(trackId);

            Assert.NotNull(loaded);
            Assert.Equal(trackId, loaded.TrackId);
            Assert.Equal(50, loaded.PointsPerSecond);
            Assert.Equal(8000, loaded.SampleRate);
            Assert.Equal(points.Length, loaded.Points.Length);
            AssertClose(points[0].Min, loaded.Points[0].Min);
            AssertClose(points[0].Max, loaded.Points[0].Max);
            AssertClose(points[^1].Min, loaded.Points[^1].Min);
            AssertClose(points[^1].Max, loaded.Points[^1].Max);
        }
    }

    [Fact]
    public void Load_Missing_ReturnsNull()
    {
        using var store = new SqliteWaveformStore(_path);

        Assert.Null(store.Load(TrackId.New()));
    }

    [Fact]
    public void Save_Twice_Replaces()
    {
        var trackId = TrackId.New();
        var first = new WaveformPeaks(trackId, 50, 8000, [new PeakPoint(-0.1f, 0.1f), new PeakPoint(-0.1f, 0.1f), new PeakPoint(-0.1f, 0.1f)]);
        var second = new WaveformPeaks(trackId, 25, 44100, [new PeakPoint(-0.9f, 0.9f), new PeakPoint(-0.8f, 0.8f)]);

        using (var store = new SqliteWaveformStore(_path))
        {
            store.Save(first);
            store.Save(second);
        }

        using (var store = new SqliteWaveformStore(_path))
        {
            var loaded = store.Load(trackId);

            Assert.NotNull(loaded);
            Assert.Equal(25, loaded.PointsPerSecond);
            Assert.Equal(44100, loaded.SampleRate);
            Assert.Equal(2, loaded.Points.Length);
            AssertClose(0.8f, loaded.Points[1].Max);
        }
    }

    [Fact]
    public void CorruptBlob_LoadReturnsNull()
    {
        var trackId = TrackId.New();
        using (var store = new SqliteWaveformStore(_path))
        {
            store.Save(new WaveformPeaks(trackId, 50, 8000, [new PeakPoint(-0.1f, 0.1f), new PeakPoint(-0.2f, 0.2f)]));
        }

        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE waveform SET points = $blob WHERE track_id = $id";
            command.Parameters.AddWithValue("$blob", new byte[] { 1, 2, 3, 4, 5 });
            command.Parameters.AddWithValue("$id", trackId.Value);
            command.ExecuteNonQuery();
        }

        using (var store = new SqliteWaveformStore(_path))
        {
            Assert.Null(store.Load(trackId));
        }
    }

    private static void AssertClose(float expected, float actual)
    {
        Assert.InRange(actual, expected - 1e-6f, expected + 1e-6f);
    }
}