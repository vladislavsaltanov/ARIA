namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;
using Aria.Core.State;
using System.Linq;

public sealed class TrackBpmTests : IDisposable
{
    private static readonly ClientId TestClient = new("bpm");

    private readonly FakeEngine _engine = new();
    private readonly CommandBus _bus;
    private readonly ShowController _controller;
    private readonly Track _track;
    private readonly System.Collections.Concurrent.ConcurrentQueue<StateEvent> _events = new();
    private readonly IDisposable _subscription;
    private long _seq;

    public TrackBpmTests()
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
    public async Task SetTrackBpm_StoresAndClears()
    {
        Submit(new SetTrackBpm(_track.Id, 128.5));
        await Poll(() => _controller.TrackBpm(_track.Id) == 128.5, "bpm not stored");
        Submit(new SetTrackBpm(_track.Id, null));
        await Poll(() => _controller.TrackBpm(_track.Id) is null, "bpm not cleared");
    }

    [Fact]
    public async Task SetTrackBpm_OutOfRange_Rejected()
    {
        var seq = Interlocked.Increment(ref _seq);
        _bus.Submit(TestClient, seq, new SetTrackBpm(_track.Id, 10));
        await WaitRejected(seq, "bpm-out-of-range");
        Assert.True(_controller.TrackBpm(_track.Id) is null, "bad bpm stored");
    }

    [Fact]
    public async Task SetTrackBpm_UnknownTrack_Rejected()
    {
        var seq = Interlocked.Increment(ref _seq);
        _bus.Submit(TestClient, seq, new SetTrackBpm(new TrackId(Guid.NewGuid()), 120));
        await WaitRejected(seq, "unknown-track");
    }

    [Fact]
    public async Task SetTrackBpm_UpdatesDigest()
    {
        Submit(new SetTrackBpm(_track.Id, 140));
        await Poll(() => DigestBpm() == 140, "digest missing bpm");
        Submit(new SetTrackBpm(_track.Id, null));
        await Poll(() => DigestBpm() is null, "digest bpm not cleared");
    }

    [Fact]
    public async Task SetTrackBpm_UpdatesTransportCurrentBpm()
    {
        Submit(new Play());
        await Poll(() => _bus.Snapshot().Transport.Status == TransportStatus.Playing, "not playing");

        Submit(new SetTrackBpm(_track.Id, 130));
        await Poll(() => _bus.Snapshot().Transport.Current?.Bpm == 130, "current deck missing bpm");
        Assert.Equal(130, _controller.Tracks.First(t => t.Id == _track.Id).Defaults.Bpm);
    }

    private double? DigestBpm() => _bus.Snapshot().Show.TrackDigest.Entries
        .FirstOrDefault(e => e.Track == _track.Id)?.Bpm;
}
