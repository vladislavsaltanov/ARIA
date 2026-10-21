namespace Aria.App.Tests;

using Aria.App;
using Aria.Audio;

public sealed class ShutdownDeadlockTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aria-shutdown-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Dispose_BlockedOnUiLikeContext_CompletesInsteadOfDeadlocking()
    {
        var completed = false;
        var thread = new Thread(() =>
        {
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new NeverPumpedContext());
            try
            {
                var host = new AppHost(_directory, null, () => new NullSink(8000, 2), () => new NullSourceFactory());
                Task.Run(() => host.StartAsync()).GetAwaiter().GetResult();
                completed = host.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        });
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(15));

        Assert.True(completed, "DisposeAsync waited for a UI context that the caller was blocking");
    }

    private sealed class NeverPumpedContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
        }
    }

    private sealed class NullSourceFactory : ISourceFactory
    {
        public ISampleSource? Open(string filePath, TimeSpan cueIn, TimeSpan? cueOut) => null;
    }
}
