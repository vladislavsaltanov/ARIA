namespace Aria.App.Tests;

using System.Collections.Immutable;
using Aria.App;
using Aria.Audio;
using Aria.Core.Model;
using Aria.Core.State;
using Aria.Persistence;

public sealed class AppHostRecoveryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aria-recovery-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task Start_InconsistentSnapshot_RestoresTheRestInsteadOfLosingTheShow()
    {
        Directory.CreateDirectory(_directory);
        var track = new Track(TrackId.New(), "/audio/x.flac", "x", TimeSpan.FromMinutes(1), new TrackDefaults());
        var project = new Project(ProjectId.New(), "Main", [new ProjectEntry(EntryId.New(), track.Id)]);
        var ghost = new QueueItem(EntryId.New(), TrackId.New(), "ghost", null);
        using (var store = new JsonSnapshotStore(Path.Combine(_directory, "show.json")))
        {
            store.Save(new ShowDocument([track], [project], project.Id, ImmutableArray.Create(ghost), -3, TimeSpan.FromMilliseconds(90), TimeSpan.Zero, false, [], DateTimeOffset.UtcNow));
        }

        await using var host = new AppHost(_directory, null, () => new NullSink(8000, 2), () => new NullSourceFactory());
        await host.StartAsync();

        var deadline = Environment.TickCount64 + 5_000;
        while (host.Bus.Snapshot().Show.Projects.IsEmpty)
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException("show was rejected and lost");
            }
            await Task.Delay(20);
        }
        Assert.Equal(project.Id, host.Bus.Snapshot().Show.ActiveId);
        Assert.Empty(host.Bus.Snapshot().Queue.Items);
    }

    private sealed class NullSourceFactory : ISourceFactory
    {
        public ISampleSource? Open(string filePath, TimeSpan cueIn, TimeSpan? cueOut) => null;
    }
}
