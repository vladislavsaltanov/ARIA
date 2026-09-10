namespace Aria.App.Tests;

using Aria.App.Services;
using Aria.Audio;

public sealed class TrackImporterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aria-import-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void TrackImporter_ImportsWav()
    {
        Directory.CreateDirectory(_directory);
        var path = TestWav.Write(_directory, "demo-track.wav");
        var importer = CreateImporter();

        var imported = importer.Import(path);

        Assert.NotNull(imported);
        Assert.Equal("demo-track", imported.Track.DefaultName);
        Assert.Equal(path, imported.Track.FilePath);
        Assert.InRange(imported.Track.Duration.TotalSeconds, 0.95, 1.05);
        Assert.NotNull(imported.Peaks);
        Assert.Equal(imported.Track.Id, imported.Peaks.TrackId);
        Assert.InRange(imported.Peaks.Points.Length, 20, 30);
    }

    [Fact]
    public void Importer_NullForMissing()
    {
        var importer = CreateImporter();

        var imported = importer.Import(Path.Combine(_directory, "missing.wav"));

        Assert.Null(imported);
    }

    [Fact]
    public void TrackImporter_ImportsWav_AtProductionRate()
    {
        Directory.CreateDirectory(_directory);
        var path = TestWav.Write(_directory, "production-rate.wav");
        var factory = new MiniaudioSourceFactory(AppHost.SampleRate, AppHost.Channels);
        var importer = new TrackImporter(factory, new WaveformScanner(factory));

        var imported = importer.Import(path);

        Assert.NotNull(imported);
        Assert.InRange(imported.Track.Duration.TotalSeconds, 0.95, 1.05);
        Assert.NotNull(imported.Peaks);
        Assert.InRange(imported.Peaks.Points.Length, 20, 30);
    }

    private static TrackImporter CreateImporter()
    {
        var factory = new MiniaudioSourceFactory(8000, 1);
        return new TrackImporter(factory, new WaveformScanner(factory));
    }
}
