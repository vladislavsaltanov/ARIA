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
    private static readonly object Wake = new();

    private readonly Channel<object> _channel = Channel.CreateUnbounded<object>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentQueue<Envelope> _urgent = new();
    private readonly IAppLog _log;
    private readonly Dictionary<ClientId, RingSet> _seen = [];
    private readonly object _gate = new();
    private List<Action<StateEvent>> _subscribers = [];
    private CancellationTokenSource? _cts;
    private Thread? _pump;
    private ShowSnapshot _published;
    private long _panicsSubmitted;
    private long _panicsHandled;

    public CommandBus(IShowHandler handler, BusMode mode = BusMode.Pumped, IAppLog? log = null)
    {
        _handler = handler;
        _mode = mode;
        _log = log ?? NullAppLog.Instance;
        handler.Emitted = Publish;
        _published = handler.Snapshot();
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
        if (command is Panic)
        {
            _urgent.Enqueue(new Envelope { Client = client, Seq = seq, Command = command, PanicEpoch = Interlocked.Increment(ref _panicsSubmitted) });
            _channel.Writer.TryWrite(Wake);
            return;
        }
        _channel.Writer.TryWrite(new Envelope { Client = client, Seq = seq, Command = command, PanicEpoch = Volatile.Read(ref _panicsSubmitted) });
    }

    public void Post(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (_mode == BusMode.Inline)
        {
            work();
            return;
        }
        _channel.Writer.TryWrite(work);
    }

    public IDisposable Subscribe(Action<StateEvent> observer)
    {
        lock (_gate)
        {
            _subscribers.Add(observer);
        }
        return new Subscription(this, observer);
    }

    public ShowSnapshot Snapshot() => _mode == BusMode.Inline ? _handler.Snapshot() : Volatile.Read(ref _published);

    public void Dispose()
    {
        if (_mode == BusMode.Pumped)
        {
            _cts?.Cancel();
            _pump?.Join(TimeSpan.FromSeconds(2));
        }
    }

    private void Dispatch(ClientId client, long seq, Command command)
    {
        if (IsDuplicate(client, seq))
        {
            return;
        }
        try
        {
            _handler.Handle(client, seq, command);
        }
        catch (Exception e)
        {
            _log.Error("bus.handler_failed", Describe(e, command.GetType().Name));
            Publish(new Rejected(client, seq, "internal-error"));
        }
    }

    private void Run(Action work)
    {
        try
        {
            work();
        }
        catch (Exception e)
        {
            _log.Error("bus.work_failed", Describe(e, "work"));
        }
    }

    private static Dictionary<string, string> Describe(Exception e, string source) => new()
    {
        ["source"] = source,
        ["type"] = e.GetType().FullName ?? "unknown",
        ["message"] = e.Message,
    };

    private void RefreshPublished()
    {
        try
        {
            Volatile.Write(ref _published, _handler.Snapshot());
        }
        catch (Exception e)
        {
            _log.Error("bus.snapshot_failed", Describe(e, "snapshot"));
        }
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
        if (_mode == BusMode.Pumped)
        {
            RefreshPublished();
        }
        Action<StateEvent>[] targets;
        lock (_gate)
        {
            targets = [.. _subscribers];
        }
        foreach (var target in targets)
        {
            try
            {
                target(e);
            }
            catch (Exception ex)
            {
                _log.Error("bus.subscriber_failed", Describe(ex, e.GetType().Name));
            }
        }
    }

    private void StartPump()
    {
        _cts = new CancellationTokenSource();
        _pump = new Thread(() => PumpLoop(_cts.Token)) { IsBackground = true, Name = "aria-control" };
        _pump.Start();
    }

    private void PumpLoop(CancellationToken ct)
    {
        try
        {
            while (_channel.Reader.WaitToReadAsync(ct).AsTask().GetAwaiter().GetResult())
            {
                while (_channel.Reader.TryRead(out var item))
                {
                    DrainUrgent();
                    if (item is Envelope envelope)
                    {
                        if (envelope.PanicEpoch < _panicsHandled && StartsSound(envelope.Command))
                        {
                            Publish(new Rejected(envelope.Client, envelope.Seq, "superseded-by-panic"));
                            continue;
                        }
                        Dispatch(envelope.Client, envelope.Seq, envelope.Command);
                        RefreshPublished();
                    }
                    else if (item is Action work)
                    {
                        Run(work);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void DrainUrgent()
    {
        while (_urgent.TryDequeue(out var urgent))
        {
            _panicsHandled = urgent.PanicEpoch;
            Dispatch(urgent.Client, urgent.Seq, urgent.Command);
            RefreshPublished();
        }
    }

    private static bool StartsSound(Command command) =>
        command is Play or Next or Replay or SeekTo or JumpTo or PlayTrack or StartPreviewTrack or StartSessionTrack;

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
        public long PanicEpoch { get; init; }
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
