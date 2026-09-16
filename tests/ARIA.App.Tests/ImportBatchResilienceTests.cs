namespace Aria.App.Tests;

using Aria.App;
using Aria.App.Services;
using Aria.Audio;

public sealed class ImportBatchResilienceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aria-batch-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task AppHost_ImportTracks_ThrowingFile_DoesNotAbortBatch()
    {
        await using var host = new AppHost(_directory, null, () => new NullSink(8000, 2), () => new NullSourceFactory());
        await host.StartAsync();
        var good1 = TestWav.Write(_directory, "good1.wav");
        var boom = TestWav.Write(_directory, "boom.wav");
        var good2 = TestWav.Write(_directory, "good2.wav");
        var factory = new MiniaudioSourceFactory(AppHost.SampleRate, AppHost.Channels);
        var real = new TrackImporter(factory, new WaveformScanner(factory));
        host.ImportOverride = path => path == boom
            ? throw new InvalidDataException("boom")
            : real.Import(path);

        var report = await host.ImportTracksAsync([good1, boom, good2]);

        Assert.Equal(2, report.Added);
        Assert.Equal(0, report.Skipped);
        Assert.Equal(boom, Assert.Single(report.Failed));
        Assert.Equal(2, host.Library!.Load().Tracks.Length);
    }

    private sealed class NullSourceFactory : ISourceFactory
    {
        public ISampleSource? Open(string filePath, TimeSpan cueIn, TimeSpan? cueOut) => null;
    }
}
