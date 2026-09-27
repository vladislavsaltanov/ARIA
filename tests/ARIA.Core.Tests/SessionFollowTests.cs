namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.Runtime;
using Aria.Core.State;

public sealed class SessionFollowTests : IDisposable
{
    private static readonly ClientId TestClient = new("session-follow");

    private readonly FakeEngine _engine = new();
    private readonly CommandBus _bus;
    private readonly ShowController _controller;
    private long _seq;

    public SessionFollowTests()
    {
        _controller = new ShowController(_engine);
        _bus = new CommandBus(_controller, BusMode.Pumped);
    }

    public void Dispose() => _bus.Dispose();

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

    private async Task PlayLoadedShow()
    {
        var track = TestShow.Track("follow");
        var project = TestShow.Project("Main", TestShow.Entry(track));
        _bus.Submit(TestClient, Interlocked.Increment(ref _seq), new LoadShow([track], [project], project.Id));
        _bus.Submit(TestClient, Interlocked.Increment(ref _seq), new Play());
        await Poll(() => _engine.SessionFollows.Count > 0 && _engine.SessionFollows[^1], "follow never enabled on play");
    }

    [Fact]
    public async Task Play_EnablesSessionFollow()
    {
        await PlayLoadedShow();
        Assert.Equal([true], _engine.SessionFollows);
    }

    [Fact]
    public async Task Pause_DisablesSessionFollow()
    {
        await PlayLoadedShow();
        _bus.Submit(TestClient, Interlocked.Increment(ref _seq), new Pause());
        await Poll(() => _engine.SessionFollows.Count == 2, "follow never disabled on pause");
        Assert.Equal([true, false], _engine.SessionFollows);
    }

    [Fact]
    public async Task Stop_DisablesSessionFollow()
    {
        await PlayLoadedShow();
        _bus.Submit(TestClient, Interlocked.Increment(ref _seq), new Stop());
        await Poll(() => _engine.SessionFollows.Count == 2, "follow never disabled on stop");
        Assert.Equal([true, false], _engine.SessionFollows);
    }

    [Fact]
    public async Task Panic_DisablesSessionFollow()
    {
        await PlayLoadedShow();
        _bus.Submit(TestClient, Interlocked.Increment(ref _seq), new Panic());
        await Poll(() => _engine.SessionFollows.Count == 2, "follow never disabled on panic");
        Assert.Equal([true, false], _engine.SessionFollows);
    }
}
