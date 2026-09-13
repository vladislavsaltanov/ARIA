namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.Playback;
using Aria.Core.Runtime;
using Aria.Core.State;

public sealed class Harness : IDisposable
{
    public static readonly ClientId Client = new("test");

    public FakeEngine Engine { get; } = new();
    public CommandBus Bus { get; }
    public List<StateEvent> Events { get; } = [];
    public PlaybackMonitor? Monitor { get; }

    private long _seq;

    public Harness(PlaybackMonitor? monitor = null)
    {
        Monitor = monitor;
        Bus = new CommandBus(new ShowController(Engine, Monitor), BusMode.Inline);
        Bus.Subscribe(Events.Add);
    }

    public long Submit(Command command)
    {
        var seq = ++_seq;
        Bus.Submit(Client, seq, command);
        return seq;
    }

    public ShowSnapshot Snapshot => Bus.Snapshot();

    public TransportState Transport => Snapshot.Transport;

    public Rejected? RejectionOf(long seq) => Events.OfType<Rejected>().LastOrDefault(r => r.Seq == seq);

    public void Dispose() => Bus.Dispose();
}
