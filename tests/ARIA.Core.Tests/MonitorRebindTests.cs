namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.Runtime;
using Aria.Core.State;

public sealed class MonitorRebindTests : IDisposable
{
    private static readonly ClientId TestClient = new("rebind");

    private readonly FakeEngine _engine = new();
    private readonly PlaybackMonitor _monitor = new();
    private readonly CommandBus _bus;
    private readonly Track _song = TestShow.Track("song");
    private readonly Track _guide = TestShow.Track("guide");
    private readonly ProjectEntry _entry;
    private long _seq;

    public MonitorRebindTests()
    {
        _bus = new CommandBus(new ShowController(_engine, _monitor), BusMode.Pumped);
        _entry = TestShow.Entry(_song);
        var project = TestShow.Project("Main", _entry);
        Submit(new LoadShow([_song, _guide], [project], project.Id));
    }

    public void Dispose() => _bus.Dispose();

    [Fact]
    public async Task SetTrackGrid_WhilePlaying_RebindsMonitorDeck()
    {
        var handle = await PlayAsync();
        var grid = new BeatGrid(90, 3, TimeSpan.Zero);

        Submit(new SetTrackGrid(_song.Id, grid));

        await Poll(() => _monitor.TryGetDeck(handle, out var deck) && deck.Grid == grid, "monitor binding kept the old grid");
    }

    [Fact]
    public async Task SetEntryBacking_WhilePlaying_RebindsMonitorDeck()
    {
        var handle = await PlayAsync();

        Submit(new SetEntryOverrides(_entry.Id, new ProjectOverrides(Backing: _guide.Id)));

        await Poll(() => _monitor.TryGetDeck(handle, out var deck) && deck.Backing == _guide.Id, "monitor binding kept the old backing");
    }

    [Fact]
    public async Task SetTrackGrid_WhilePlaying_KeepsStartPosition()
    {
        Submit(new SetEntryOverrides(_entry.Id, new ProjectOverrides(CueIn: TimeSpan.FromSeconds(10))));
        var handle = await PlayAsync();
        Assert.True(_monitor.TryGetDeck(handle, out var before));
        Assert.Equal(TimeSpan.FromSeconds(10), before.CueIn);

        Submit(new SetTrackGrid(_song.Id, new BeatGrid(90, 3, TimeSpan.Zero)));

        await Poll(() => _monitor.TryGetDeck(handle, out var deck) && deck.Grid is not null, "grid not rebound");
        Assert.True(_monitor.TryGetDeck(handle, out var after));
        Assert.Equal(TimeSpan.FromSeconds(10), after.CueIn);
        Assert.Equal(TimeSpan.FromSeconds(10), _bus.Snapshot().Transport.Current!.CueIn);
    }

    private async Task<StreamHandle> PlayAsync()
    {
        Submit(new Play());
        await Poll(() => _bus.Snapshot().Transport.Status == TransportStatus.Playing && _engine.Created.Count > 0, "not playing");
        return _engine.Created[^1].Handle;
    }

    private void Submit(Command command) => _bus.Submit(TestClient, Interlocked.Increment(ref _seq), command);

    private static async Task Poll(Func<bool> condition, string message)
    {
        var deadline = Environment.TickCount64 + 5000;
        while (!condition())
        {
            Assert.True(Environment.TickCount64 < deadline, message);
            await Task.Delay(20);
        }
    }
}
