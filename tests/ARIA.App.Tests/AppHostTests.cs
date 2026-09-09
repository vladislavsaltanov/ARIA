namespace Aria.App.Tests;

using System.Collections.Immutable;
using Aria.App;
using Aria.Audio;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.State;
using Aria.Persistence;

public sealed class AppHostTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aria-host-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task Start_CreatesStores_AndAcceptsCommands()
    {
        await using var host = new AppHost(_directory, null, () => new NullSink(8000, 2), () => new NullSourceFactory());

        await host.StartAsync();
        host.Submit(new CreatePlaylist("Main"));

        await PollAsync(() => host.Bus.Snapshot().Show.Playlists.Length == 1);
        Assert.True(File.Exists(Path.Combine(_directory, "library.db")));
    }

    [Fact]
    public async Task Start_RestoresSessionFromSnapshot()
    {
        Directory.CreateDirectory(_directory);
        var track = new Track(TrackId.New(), "/audio/x.flac", "x", TimeSpan.FromMinutes(1), new TrackDefaults());
        var playlist = new Playlist(PlaylistId.New(), "Main", [new PlaylistEntry(EntryId.New(), track.Id)]);
        var queue = ImmutableArray.Create(new QueueItem(null, track.Id, "x", null));
        var snapshotPath = Path.Combine(_directory, "show.json");
        using (var store = new JsonSnapshotStore(snapshotPath))
        {
            store.Save(new ShowDocument([track], [playlist], playlist.Id, queue, -3, TimeSpan.FromMilliseconds(90), DateTimeOffset.UtcNow));
        }

        await using var host = new AppHost(_directory, null, () => new NullSink(8000, 2), () => new NullSourceFactory());
        await host.StartAsync();

        await PollAsync(() => host.Bus.Snapshot().Show.ActiveId is not null);
        var snapshot = host.Bus.Snapshot();
        Assert.Equal(playlist.Id, snapshot.Show.ActiveId);
        Assert.Single(snapshot.Queue.Items);
        Assert.Equal(-3, snapshot.Mixer.MasterGainDb);
        Assert.Equal(TimeSpan.FromMilliseconds(90), snapshot.Mixer.PanicFade);
    }

    [Fact]
    public async Task Dispose_FlushesAutosave()
    {
        var host = new AppHost(_directory, null, () => new NullSink(8000, 2), () => new NullSourceFactory());
        await host.StartAsync();
        host.Submit(new CreatePlaylist("Main"));
        await Task.Delay(100);

        await host.DisposeAsync();

        Assert.True(File.Exists(Path.Combine(_directory, "show.json")));
    }

    private static async Task PollAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }
            await Task.Delay(25);
        }
        throw new TimeoutException("condition not met");
    }

    private sealed class NullSourceFactory : ISourceFactory
    {
        public ISampleSource? Open(string filePath, TimeSpan cueIn, TimeSpan? cueOut) => null;
    }
}
