namespace Aria.Core.Runtime;

using System.Collections.Concurrent;
using System.Threading.Channels;
using Aria.Core.Commands;
using Aria.Core.State;

public enum BusMode
{
    Inline,
    Pumped,
}

public sealed class CommandBus : ICommandBus, IDisposable
{
    private const int DedupeWindow = 256;

    private readonly IShowHandler _handler;
    private readonly BusMode _mode;
    private readonly Channel<Envelope> _channel = Channel.CreateUnbounded<Envelope>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Dictionary<ClientId, RingSet> _seen = [];
    private readonly object _gate = new();
    private List<Action<StateEvent>> _subscribers = [];
    private CancellationTokenSource? _cts;
    private Task? _pump;

    public CommandBus(IShowHandler handler, BusMode mode = BusMode.Pumped)
    {
        _handler = handler;
        _mode = mode;
        handler.Emitted = Publish;
        if (mode == BusMode.Pumped)
        {
            StartPump();
        }
    }

    public void Submit(ClientId client, long seq, Command command)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seq);
        if (_mode == BusMode.Inline)
        {
            Dispatch(client, seq, command);
            return;
        }
        _channel.Writer.TryWrite(new Envelope { Client = client, Seq = seq, Command = command });
    }

    public IDisposable Subscribe(Action<StateEvent> observer)
    {
        lock (_gate)
        {
            _subscribers.Add(observer);
        }
        return new Subscription(this, observer);
    }

    public ShowSnapshot Snapshot() => _handler.Snapshot();

    public void Dispose()
    {
        if (_mode == BusMode.Pumped)
        {
            _cts?.Cancel();
            _pump?.Wait(TimeSpan.FromSeconds(2));
        }
    }

    private void Dispatch(ClientId client, long seq, Command command)
    {
        if (IsDuplicate(client, seq))
        {
            return;
        }
        _handler.Handle(client, seq, command);
    }

    private bool IsDuplicate(ClientId client, long seq)
    {
        lock (_gate)
        {
            if (!_seen.TryGetValue(client, out var ring))
            {
                ring = new RingSet(DedupeWindow);
                _seen[client] = ring;
            }
            return !ring.Add(seq);
        }
    }

    private void Publish(StateEvent e)
    {
        Action<StateEvent>[] targets;
        lock (_gate)
        {
            targets = [.. _subscribers];
        }
        foreach (var target in targets)
        {
            target(e);
        }
    }

    private void StartPump()
    {
        _cts = new CancellationTokenSource();
        _pump = Task.Factory.StartNew(
            () => PumpLoopAsync(_cts.Token),
            _cts.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
    }

    private async Task PumpLoopAsync(CancellationToken ct)
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (_channel.Reader.TryRead(out var envelope))
                {
                    Dispatch(envelope.Client, envelope.Seq, envelope.Command);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Unsubscribe(Action<StateEvent> observer)
    {
        lock (_gate)
        {
            _subscribers.Remove(observer);
        }
    }

    private sealed class Envelope
    {
        public required ClientId Client { get; init; }
        public required long Seq { get; init; }
        public required Command Command { get; init; }
    }

    private sealed class Subscription(CommandBus bus, Action<StateEvent> observer) : IDisposable
    {
        public void Dispose() => bus.Unsubscribe(observer);
    }

    private sealed class RingSet(int capacity)
    {
        private readonly Queue<long> _order = [];
        private readonly HashSet<long> _set = [];

        public bool Add(long value)
        {
            if (!_set.Add(value))
            {
                return false;
            }
            _order.Enqueue(value);
            if (_order.Count > capacity)
            {
                _set.Remove(_order.Dequeue());
            }
            return true;
        }
    }
}
