namespace Aria.App.Tests;

using Aria.App;
using Aria.Audio;
using Aria.Core.Commands;
using Aria.Persistence;

public sealed class AppHostFatalTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aria-fatal-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task Fatal_FlushesShowImmediately_EvenWithLoggingDisabled()
    {
        await using var host = new AppHost(_directory, null, () => new NullSink(8000, 2), () => new NullSourceFactory());
        await host.StartAsync();
        host.Submit(new CreateProject("Before crash"));
        var deadline = Environment.TickCount64 + 5_000;
        while (host.Bus.Snapshot().Show.Projects.IsEmpty)
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException("project never applied");
            }
            await Task.Delay(10);
        }

        host.OnFatal(new InvalidOperationException("render thread died"));

        using var store = new JsonSnapshotStore(Path.Combine(_directory, "show.json"));
        var saved = store.LoadLatest();
        Assert.NotNull(saved);
        Assert.Equal("Before crash", Assert.Single(saved.Projects).Name);
    }

    [Fact]
    public async Task ReportUiFault_WithLoggingDisabled_DoesNotThrow()
    {
        await using var host = new AppHost(_directory, null, () => new NullSink(8000, 2), () => new NullSourceFactory());
        await host.StartAsync();

        host.ReportUiFault(new InvalidOperationException("binding blew up"));
    }

    private sealed class NullSourceFactory : ISourceFactory
    {
        public ISampleSource? Open(string filePath, TimeSpan cueIn, TimeSpan? cueOut) => null;
    }
}
