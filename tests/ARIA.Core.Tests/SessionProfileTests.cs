namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.Runtime;
using Aria.Core.State;

public sealed class SessionProfileTests : IDisposable
{
    private static readonly ClientId TestClient = new("session-profile");

    private readonly FakeEngine _engine = new();
    private readonly CommandBus _bus;
    private readonly ShowController _controller;
    private readonly System.Collections.Concurrent.ConcurrentQueue<StateEvent> _events = new();
    private readonly IDisposable _subscription;
    private long _seq;

    public SessionProfileTests()
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
    public async Task RenameSession_StoresName()
    {
        var session = new PreviewSessionHandle(11);
        _bus.Submit(TestClient, Interlocked.Increment(ref _seq), new RenameSession(session, "Barabanshik"));
        await Poll(() => _engine.SessionNames.Count > 0, "rename never reached engine");
        Assert.Equal((session, "Barabanshik"), _engine.SessionNames[0]);
    }

    [Fact]
    public async Task RenameSession_Empty_Rejected()
    {
        var seq = Interlocked.Increment(ref _seq);
        _bus.Submit(TestClient, seq, new RenameSession(new PreviewSessionHandle(12), "  "));
        await WaitRejected(seq, "name-empty");
    }

    [Fact]
    public async Task RenameSession_TooLong_Rejected()
    {
        var seq = Interlocked.Increment(ref _seq);
        _bus.Submit(TestClient, seq, new RenameSession(new PreviewSessionHandle(13), new string('x', 65)));
        await WaitRejected(seq, "name-too-long");
    }

    [Fact]
    public async Task SetSessionBackingGain_RecordsEngineCall()
    {
        var session = new PreviewSessionHandle(14);
        _bus.Submit(TestClient, Interlocked.Increment(ref _seq), new SetSessionBackingGain(session, -6.0));
        await Poll(() => _engine.SessionGains.Count > 0, "gain never reached engine");
        Assert.Equal((session, -6.0), _engine.SessionGains[0]);
    }

    [Fact]
    public async Task SetSessionBackingGain_OutOfRange_Rejected()
    {
        var seq = Interlocked.Increment(ref _seq);
        _bus.Submit(TestClient, seq, new SetSessionBackingGain(new PreviewSessionHandle(15), 20.0));
        await WaitRejected(seq, "gain-out-of-range");
    }

    [Fact]
    public async Task CloseSession_ClosesEngineSession()
    {
        var session = new PreviewSessionHandle(16);
        _bus.Submit(TestClient, Interlocked.Increment(ref _seq), new CloseSession(session));
        await Poll(() => _engine.ClosedSessions.Count > 0, "close never reached engine");
        Assert.Equal(session, _engine.ClosedSessions[0]);
    }

    [Fact]
    public async Task ListSessions_ReflectsRenameAndGain()
    {
        var session = new PreviewSessionHandle(17);
        _bus.Submit(TestClient, Interlocked.Increment(ref _seq), new RenameSession(session, "Monitor"));
        _bus.Submit(TestClient, Interlocked.Increment(ref _seq), new SetSessionBackingGain(session, -3.0));
        await Poll(() => _engine.ListSessions() is [{ } listed] && listed.Name == "Monitor" && listed.BackingGainDb == -3.0, "session never listed");
        var profile = Assert.Single(_engine.ListSessions());
        Assert.Equal(session, profile.Session);
        Assert.Equal("Monitor", profile.Name);
        Assert.Equal(-3.0, profile.BackingGainDb);
    }
}
