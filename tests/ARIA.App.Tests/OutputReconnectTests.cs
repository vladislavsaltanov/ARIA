namespace Aria.App.Tests;

using Aria.App;
using Aria.Audio;

public sealed class OutputReconnectTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aria-reconnect-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task StalledMainOutput_IsReplacedAutomatically()
    {
        var replacement = new CapturingSink(AppHost.SampleRate, AppHost.Channels);
        var calls = 0;
        await using var host = new AppHost(
            _directory,
            null,
            () => Interlocked.Increment(ref calls) == 1 ? new DeadSink() : replacement,
            () => new NullSourceFactory());

        await host.StartAsync();

        var deadline = Environment.TickCount64 + 10_000;
        while (replacement.TotalFrames == 0)
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException("stalled output was never replaced");
            }
            await Task.Delay(20);
        }
        Assert.True(Volatile.Read(ref calls) >= 2);
    }

    private sealed class NullSourceFactory : ISourceFactory
    {
        public ISampleSource? Open(string filePath, TimeSpan cueIn, TimeSpan? cueOut) => null;
    }

    private sealed class DeadSink : IAudioSink
    {
        public int SampleRate => AppHost.SampleRate;

        public int Channels => AppHost.Channels;

        public int Write(ReadOnlySpan<float> samples)
        {
            Thread.Sleep(5);
            return 0;
        }

        public void Flush()
        {
        }
    }
}
