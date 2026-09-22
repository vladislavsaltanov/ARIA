namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.Runtime;
using Aria.Core.State;

public sealed class SessionTrackTests : IDisposable
{
    private static readonly ClientId TestClient = new("session-track");

    private readonly FakeEngine _engine = new();
    private readonly CommandBus _bus;
    private readonly ShowController _controller;
    private readonly System.Collections.Concurrent.ConcurrentQueue<StateEvent> _events = new();
    private readonly IDisposable _subscription;
    private long _seq;

    public SessionTrackTests()
    {
        _controller = new ShowController(_engine);
        _bus = new CommandBus(_controller, BusMode.Pumped);
        _subscription = _bus.Subscribe(e => _events.Enqueue(e));
    }

    public void Dispose()
    {
        _subscription.Dispose();
        _bus.Dispose();
    }

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
    public async Task StartSessionTrack_UnknownTrack_Rejected()
    {
        var seq = Interlocked.Increment(ref _seq);
        _bus.Submit(TestClient, seq, new StartSessionTrack(new PreviewSessionHandle(1), new TrackId(Guid.NewGuid())));
        await WaitRejected(seq, "unknown-track");
    }

    [Fact]
    public async Task StartSessionTrack_KnownTrack_OpensVoice()
    {
        var track = TestShow.Track("audition");
        var project = TestShow.Project("Main", TestShow.Entry(track));
        _bus.Submit(TestClient, Interlocked.Increment(ref _seq), new LoadShow([track], [project], project.Id));
        _bus.Submit(TestClient, Interlocked.Increment(ref _seq), new StartSessionTrack(new PreviewSessionHandle(7), track.Id));
        await Poll(() => _engine.SessionTracks.Count > 0, "session voice never started");
        Assert.Equal(7, _engine.SessionTracks[0].Session.Value);
        Assert.Equal("/audio/audition.flac", _engine.SessionTracks[0].Source.FilePath);
    }
}
