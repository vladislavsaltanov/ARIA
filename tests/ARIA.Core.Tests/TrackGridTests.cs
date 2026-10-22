namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;
using Aria.Core.State;
using System.Linq;

public sealed class TrackGridTests : IDisposable
{
    private static readonly ClientId TestClient = new("bpm");

    private readonly FakeEngine _engine = new();
    private readonly CommandBus _bus;
    private readonly ShowController _controller;
    private readonly Track _track;
    private readonly System.Collections.Concurrent.ConcurrentQueue<StateEvent> _events = new();
    private readonly IDisposable _subscription;
    private long _seq;

    public TrackGridTests()
    {
        _controller = new ShowController(_engine);
        _bus = new CommandBus(_controller, BusMode.Pumped);
        _subscription = _bus.Subscribe(e => _events.Enqueue(e));
        _track = TestShow.Track("bpm");
        var project = TestShow.Project("Main", TestShow.Entry(_track));
        Submit(new LoadShow([_track], [project], project.Id));
    }

    public void Dispose()
    {
        _subscription.Dispose();
        _bus.Dispose();
    }

    private void Submit(Command command) => _bus.Submit(TestClient, Interlocked.Increment(ref _seq), command);

    private static async Task Poll(Func<bool> condition, string message)
    {
        var deadline = Environment.TickCount64 + 10000;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                Assert.Fail(message);
            }
            await Task.Delay(25);
        }
    }

    private async Task WaitRejected(long seq, string reason)
    {
        await Poll(() =>
        {
            foreach (var e in _events)
            {
                if (e is Rejected r && r.Seq == seq && r.Reason == reason)
                {
                    return true;
                }
            }
            return false;
        }, "no rejected " + reason);
    }

    [Fact]
    public async Task SetTrackGrid_StoresAndClears()
    {
        var grid = new BeatGrid(128.5, 3, TimeSpan.FromMilliseconds(250));
        Submit(new SetTrackGrid(_track.Id, grid));
        await Poll(() => _controller.TrackGrid(_track.Id) == grid, "grid not stored");
        Submit(new SetTrackGrid(_track.Id, null));
        await Poll(() => _controller.TrackGrid(_track.Id) is null, "grid not cleared");
    }

    [Theory]
    [InlineData(10.0, 4, 0L, "bpm-out-of-range")]
    [InlineData(120.0, 0, 0L, "beats-out-of-range")]
    [InlineData(120.0, 4, 180000L, "first-beat-out-of-range")]
    public async Task SetTrackGrid_OutOfRange_Rejected(double bpm, int beats, long firstBeatMs, string reason)
    {
        var seq = Interlocked.Increment(ref _seq);
        _bus.Submit(TestClient, seq, new SetTrackGrid(_track.Id, new BeatGrid(bpm, beats, TimeSpan.FromMilliseconds(firstBeatMs))));
        await WaitRejected(seq, reason);
        Assert.True(_controller.TrackGrid(_track.Id) is null, "bad grid stored");
    }

    [Fact]
    public async Task SetTrackGrid_UnknownTrack_Rejected()
    {
        var seq = Interlocked.Increment(ref _seq);
        _bus.Submit(TestClient, seq, new SetTrackGrid(new TrackId(Guid.NewGuid()), new BeatGrid(120, 4, TimeSpan.Zero)));
        await WaitRejected(seq, "unknown-track");
    }

    [Fact]
    public async Task SetTrackGrid_UpdatesDigest()
    {
        var grid = new BeatGrid(140, 4, TimeSpan.Zero);
        Submit(new SetTrackGrid(_track.Id, grid));
        await Poll(() => DigestGrid() == grid, "digest missing grid");
        Submit(new SetTrackGrid(_track.Id, null));
        await Poll(() => DigestGrid() is null, "digest grid not cleared");
    }

    [Fact]
    public async Task SetTrackGrid_CurrentTrack_EmitsTransportGrid()
    {
        Submit(new Play());
        await Poll(() => _bus.Snapshot().Transport.Status == TransportStatus.Playing, "not playing");

        var grid = new BeatGrid(130, 7, TimeSpan.FromSeconds(1.5));
        Submit(new SetTrackGrid(_track.Id, grid));
        await Poll(() => _bus.Snapshot().Transport.Current?.Grid == grid, "current deck missing grid");
        Assert.Equal(grid, _controller.Tracks.First(t => t.Id == _track.Id).Defaults.Grid);
    }

    private BeatGrid? DigestGrid() => _bus.Snapshot().Show.TrackDigest.Entries
        .FirstOrDefault(e => e.Track == _track.Id)?.Grid;
}
