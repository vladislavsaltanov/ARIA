namespace Aria.App.Tests;

using Aria.Audio;

public sealed class WaveformImportChainTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aria-chain-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task FullChain_Import_Save_Reload_KeepsPeaks()
    {
        Directory.CreateDirectory(_directory);
        var wav = TestWav.Write(_directory, "chain.wav");
        await using (var host = new AppHost(
            Path.Combine(_directory, "data"),
            sinkFactory: () => new NullSink(AppHost.SampleRate, AppHost.Channels),
            sourceFactory: () => new MiniaudioSourceFactory(AppHost.SampleRate, AppHost.Channels)))
        {
            await host.StartAsync();
            var report = await host.ImportTracksAsync([wav]);
            Assert.Equal(1, report.Added);
            var track = Assert.Single(host.Library!.Load().Tracks);
            var peaks = host.Waveforms!.Load(track.Id);
            Assert.NotNull(peaks);
            Assert.NotEmpty(peaks.Points);
        }

        await using (var host2 = new AppHost(
            Path.Combine(_directory, "data"),
            sinkFactory: () => new NullSink(AppHost.SampleRate, AppHost.Channels),
            sourceFactory: () => new MiniaudioSourceFactory(AppHost.SampleRate, AppHost.Channels)))
        {
            await host2.StartAsync();
            var track = Assert.Single(host2.Library!.Load().Tracks);
            var peaks = host2.Waveforms!.Load(track.Id);
            Assert.NotNull(peaks);
        }
    }
}
