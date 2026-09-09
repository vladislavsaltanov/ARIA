namespace Aria.Audio.Tests;

using Aria.Core.Model;

public sealed class WaveformScannerTests : IDisposable
{
    private const int SampleRate = 8000;

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "aria-waveform-scanner-tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Sine_PeakMatchesAmplitude()
    {
        var path = TestWav.WriteSine(_directory, "sine-8000.wav", SampleRate, 1, 2.0, 440.0, 0.5);
        var scanner = new WaveformScanner(new MiniaudioSourceFactory(SampleRate, 1));

        var peaks = scanner.Scan(path, TrackId.New(), pointsPerSecond: 50);

        Assert.Equal(50, peaks.PointsPerSecond);
        Assert.Equal(SampleRate, peaks.SampleRate);
        Assert.InRange(peaks.Points.Length, 98, 102);
        foreach (var point in peaks.Points)
        {
            Assert.InRange(point.Max, 0.49f, 0.51f);
            Assert.InRange(point.Min, -0.51f, -0.49f);
        }
    }

    [Fact]
    public void Stereo_MixedToMono()
    {
        var path = TestWav.WriteSine(_directory, "stereo-8000.wav", SampleRate, 2, 1.0, 440.0, 0.4);
        var scanner = new WaveformScanner(new MiniaudioSourceFactory(SampleRate, 2));

        var peaks = scanner.Scan(path, TrackId.New(), pointsPerSecond: 50);

        Assert.True(peaks.Points.Length > 0, "stereo file produced no points");
        foreach (var point in peaks.Points)
        {
            Assert.InRange(point.Max, 0.39f, 0.41f);
        }
    }

    [Fact]
    public void Resampled_File_ScanWorks()
    {
        var path = TestWav.WriteSine(_directory, "resampled-44100.wav", 44100, 1, 1.0, 440.0, 0.5);
        var scanner = new WaveformScanner(new MiniaudioSourceFactory(SampleRate, 1));

        var peaks = scanner.Scan(path, TrackId.New(), pointsPerSecond: 50);

        Assert.Equal(SampleRate, peaks.SampleRate);
        Assert.InRange(peaks.Points.Length, 48, 52);
    }

    [Fact]
    public void EmptyPoints_OnTinyFile()
    {
        var path = TestWav.WriteSine(_directory, "tiny-8000.wav", SampleRate, 1, 0.01, 440.0, 0.5);
        var scanner = new WaveformScanner(new MiniaudioSourceFactory(SampleRate, 1));

        var peaks = scanner.Scan(path, TrackId.New(), pointsPerSecond: 50);

        var point = Assert.Single(peaks.Points);
        Assert.False(float.IsNaN(point.Min));
        Assert.False(float.IsNaN(point.Max));
        Assert.InRange(point.Max, 0.0f, 0.51f);
        Assert.InRange(point.Min, -0.51f, 0.0f);
    }

    [Fact]
    public void Missing_File_Throws()
    {
        var scanner = new WaveformScanner(new MiniaudioSourceFactory(SampleRate, 1));
        var missing = Path.Combine(_directory, "does-not-exist.wav");

        Assert.Throws<FileNotFoundException>(() => scanner.Scan(missing, TrackId.New(), 50));
    }
}