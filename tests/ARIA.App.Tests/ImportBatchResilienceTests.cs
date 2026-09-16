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

    [Fact]
    public async Task AppHost_ImportTracks_InaccessibleSubfolder_DoesNotAbortBatch()
    {
        await using var host = new AppHost(_directory, null, () => new NullSink(8000, 2), () => new NullSourceFactory());
        await host.StartAsync();
        var root = Path.Combine(_directory, "root");
        var locked = Path.Combine(root, "locked");
        Directory.CreateDirectory(locked);
        var good = TestWav.Write(root, "good.wav");
        TestWav.Write(locked, "secret.wav");
        var blocked = false;
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(locked, UnixFileMode.None);
            blocked = IsListingBlocked(locked);
        }

        ImportReport report;
        try
        {
            report = await host.ImportTracksAsync([root]);
        }
        finally
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        Assert.Contains(good, host.Library!.Load().Tracks.Select(t => t.FilePath));
        Assert.Equal(blocked ? 1 : 2, report.Added);
    }

    [Fact]
    public async Task AppHost_ImportTracks_PathologicalDepth_SkipsBeyondCap()
    {
        await using var host = new AppHost(_directory, null, () => new NullSink(8000, 2), () => new NullSourceFactory());
        await host.StartAsync();
        var root = Path.Combine(_directory, "deep");
        var leaf = root;
        for (var i = 0; i < 70; i++)
        {
            leaf = Path.Combine(leaf, "d");
            Directory.CreateDirectory(leaf);
        }
        var top = TestWav.Write(root, "top.wav");
        var bottom = TestWav.Write(leaf, "bottom.wav");

        await host.ImportTracksAsync([root]);

        var stored = host.Library!.Load().Tracks.Select(t => t.FilePath);
        Assert.Contains(top, stored);
        Assert.DoesNotContain(bottom, stored);
    }

    private static bool IsListingBlocked(string directory)
    {
        try
        {
            _ = Directory.GetFileSystemEntries(directory);
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
