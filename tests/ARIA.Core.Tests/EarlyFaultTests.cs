namespace Aria.Core.Tests;

using System.Runtime.CompilerServices;
using Aria.Core.Commands;
using Aria.Core.Runtime;
using Aria.Core.State;

public sealed class EarlyFaultTests
{
    [Fact]
    public async Task FaultEmittedBeforeOpenCompletes_StillFaultsTheDeck()
    {
        var engine = new FakeEngine { FaultBeforeOpenReturns = true };
        var holder = new StrongBox<CommandBus>();
        var controller = new ShowController(engine, null, work => holder.Value!.Post(work));
        using var bus = new CommandBus(controller, BusMode.Pumped);
        holder.Value = bus;
        var track = TestShow.Track("broken");
        var project = TestShow.Project("Main", TestShow.Entry(track));
        bus.Submit(Harness.Client, 1, new LoadShow([track], [project], project.Id));

        bus.Submit(Harness.Client, 2, new Play());

        var deadline = Environment.TickCount64 + 5_000;
        while (!bus.Snapshot().Transport.Faulted.Contains(track.Id))
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException("early fault dropped: deck stuck in " + bus.Snapshot().Transport.Status);
            }
            await Task.Delay(10);
        }
        Assert.Equal(TransportStatus.Stopped, bus.Snapshot().Transport.Status);
        Assert.Null(bus.Snapshot().Transport.Current);
    }
}
