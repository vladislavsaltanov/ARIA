namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;
using Aria.Core.State;

public sealed class BusTests
{
    [Fact]
    public async Task PumpedMode_ProcessesCommandsOnControlThread_AndDeliversDeltas()
    {
        var engine = new FakeEngine();
        using var bus = new CommandBus(new ShowController(engine), BusMode.Pumped);
        var events = new List<StateEvent>();
        var playing = new TaskCompletionSource();
        var gate = new object();
        bus.Subscribe(e =>
        {
            lock (gate)
            {
                events.Add(e);
                if (e is TransportDelta { State.Status: TransportStatus.Playing })
                {
                    playing.TrySetResult();
                }
            }
        });

        var t1 = TestShow.Track("one");
        var p = TestShow.Project("Main", TestShow.Entry(t1));
        bus.Submit(Harness.Client, 1, new LoadShow([t1], [p], p.Id));
        bus.Submit(Harness.Client, 2, new Play());
        bus.Submit(Harness.Client, 2, new LoadShow([t1], [p], p.Id));

        await playing.Task.WaitAsync(TimeSpan.FromSeconds(5));

        lock (gate)
        {
            Assert.Contains(events, e => e is ShowDelta);
            Assert.DoesNotContain(events, e => e is Rejected);
        }
        Assert.Single(engine.Created);
        Assert.Equal(TransportStatus.Playing, bus.Snapshot().Transport.Status);
    }

    [Fact]
    public void InlineMode_IsDeterministic()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Project("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));

        h.Submit(new Play());

        Assert.Single(h.Engine.Created);
        Assert.True(h.Events.Count > 0);
    }
}
