namespace Aria.Core.Tests;

using System.Collections.Concurrent;
using Aria.Core.Commands;
using Aria.Core.Runtime;
using Aria.Core.State;

public sealed class BusResilienceTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task PumpedMode_HandlerThrows_KeepsProcessingLaterCommands()
    {
        var handler = new ScriptedHandler { ThrowOn = typeof(Stop) };
        var log = new RecordingLog();
        using var bus = new CommandBus(handler, BusMode.Pumped, log);
        var rejected = new TaskCompletionSource<Rejected>(TaskCreationOptions.RunContinuationsAsynchronously);
        bus.Subscribe(e =>
        {
            if (e is Rejected r)
            {
                rejected.TrySetResult(r);
            }
        });

        bus.Submit(Harness.Client, 1, new Stop());
        bus.Submit(Harness.Client, 2, new Play());

        await handler.WaitForAsync(typeof(Play), Wait);
        var rejection = await rejected.Task.WaitAsync(Wait);
        Assert.Equal(1, rejection.Seq);
        Assert.Equal("internal-error", rejection.Reason);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Error && e.Message == "bus.handler_failed");
    }

    [Fact]
    public async Task PumpedMode_PostedWorkThrows_KeepsProcessingLaterCommands()
    {
        var handler = new ScriptedHandler();
        var log = new RecordingLog();
        using var bus = new CommandBus(handler, BusMode.Pumped, log);

        bus.Post(() => throw new InvalidOperationException("boom"));
        bus.Submit(Harness.Client, 1, new Play());

        await handler.WaitForAsync(typeof(Play), Wait);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Error && e.Message == "bus.work_failed");
    }

    [Fact]
    public void ThrowingSubscriber_DoesNotStarveOtherSubscribersOrHandler()
    {
        var handler = new ScriptedHandler { EmitOnHandle = true };
        var log = new RecordingLog();
        using var bus = new CommandBus(handler, BusMode.Inline, log);
        var received = new List<StateEvent>();
        bus.Subscribe(_ => throw new InvalidOperationException("ui bug"));
        bus.Subscribe(received.Add);

        bus.Submit(Harness.Client, 1, new Play());
        bus.Submit(Harness.Client, 2, new Pause());

        Assert.Equal(2, received.Count);
        Assert.Equal(2, handler.Handled.Count);
        Assert.Contains(log.Entries, e => e.Message == "bus.subscriber_failed");
    }

    [Fact]
    public async Task Panic_OvertakesQueuedBacklog()
    {
        var handler = new ScriptedHandler { BlockOn = typeof(SetMasterGain) };
        using var bus = new CommandBus(handler, BusMode.Pumped);

        bus.Submit(Harness.Client, 1, new SetMasterGain(-6));
        await handler.Blocked.Task.WaitAsync(Wait);
        for (var seq = 2; seq < 50; seq++)
        {
            bus.Submit(Harness.Client, seq, new CreateProject($"p{seq}"));
        }
        bus.Submit(Harness.Client, 50, new Panic());
        handler.Release.Set();

        await handler.WaitForAsync(typeof(Panic), Wait);
        await handler.WaitForCountAsync(50, Wait);
        var order = handler.Handled.Select(c => c.GetType()).ToList();
        Assert.Equal(typeof(SetMasterGain), order[0]);
        Assert.Equal(typeof(Panic), order[1]);
    }

    [Fact]
    public async Task Panic_OvertakingQueuedPlay_SupersedesIt_ButKeepsEdits()
    {
        var handler = new ScriptedHandler { BlockOn = typeof(SetMasterGain) };
        using var bus = new CommandBus(handler, BusMode.Pumped);
        var rejections = new ConcurrentQueue<Rejected>();
        bus.Subscribe(e =>
        {
            if (e is Rejected r)
            {
                rejections.Enqueue(r);
            }
        });

        bus.Submit(Harness.Client, 1, new SetMasterGain(-6));
        await handler.Blocked.Task.WaitAsync(Wait);
        bus.Submit(Harness.Client, 2, new Play());
        bus.Submit(Harness.Client, 3, new CreateProject("Edit"));
        bus.Submit(Harness.Client, 4, new Panic());
        bus.Submit(Harness.Client, 5, new Play());
        handler.Release.Set();

        await handler.WaitForCountAsync(4, Wait);
        var order = handler.Handled.Select(c => c.GetType()).ToList();
        Assert.Equal([typeof(SetMasterGain), typeof(Panic), typeof(CreateProject), typeof(Play)], order);
        Assert.Equal(2, Assert.Single(rejections).Seq);
        Assert.Equal("superseded-by-panic", rejections.Single().Reason);
    }

    [Fact]
    public async Task Snapshot_FromForeignThread_NeverRacesTheControlThread()
    {
        var handler = new ScriptedHandler { BlockOn = typeof(Play) };
        using var bus = new CommandBus(handler, BusMode.Pumped);

        bus.Submit(Harness.Client, 1, new Play());
        await handler.Blocked.Task.WaitAsync(Wait);
        var snapshot = await Task.Run(bus.Snapshot).WaitAsync(Wait);
        handler.Release.Set();
        await handler.WaitForCountAsync(1, Wait);

        Assert.NotNull(snapshot);
        Assert.Equal(0, handler.ConcurrentSnapshots);
    }

    [Fact]
    public async Task Snapshot_ReadAfterObservedDelta_ReflectsThatDelta()
    {
        var handler = new ScriptedHandler { EmitOnHandle = true };
        using var bus = new CommandBus(handler, BusMode.Pumped);
        var observed = new TaskCompletionSource<(int Delta, int Snapshot)>(TaskCreationOptions.RunContinuationsAsynchronously);
        bus.Subscribe(e =>
        {
            if (e is ShowDelta delta)
            {
                Task.Run(() => observed.TrySetResult((delta.Version, bus.Snapshot().ShowVersion)));
            }
        });

        bus.Submit(Harness.Client, 1, new Play());

        var (deltaVersion, snapshotVersion) = await observed.Task.WaitAsync(Wait);
        Assert.True(snapshotVersion >= deltaVersion);
    }

    private sealed class ScriptedHandler : IShowHandler
    {
        private readonly object _gate = new();
        private int _inHandle;
        private int _version;
        private int _concurrentSnapshots;

        public Type? ThrowOn { get; init; }
        public Type? BlockOn { get; init; }
        public bool EmitOnHandle { get; init; }
        public TaskCompletionSource Blocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new();
        public ConcurrentQueue<Command> HandledQueue { get; } = new();
        public List<Command> Handled => [.. HandledQueue];
        public int ConcurrentSnapshots => Volatile.Read(ref _concurrentSnapshots);

        public Action<StateEvent>? Emitted { get; set; }

        public void Handle(ClientId client, long seq, Command command)
        {
            Interlocked.Exchange(ref _inHandle, 1);
            try
            {
                if (BlockOn == command.GetType() && !Blocked.Task.IsCompleted)
                {
                    Blocked.TrySetResult();
                    Release.Wait(Wait);
                }
                if (ThrowOn == command.GetType())
                {
                    throw new InvalidOperationException("handler bug");
                }
                lock (_gate)
                {
                    _version++;
                }
                HandledQueue.Enqueue(command);
                if (EmitOnHandle)
                {
                    Emitted?.Invoke(new ShowDelta(_version, Snapshot().Show));
                }
            }
            finally
            {
                Interlocked.Exchange(ref _inHandle, 0);
            }
        }

        public ShowSnapshot Snapshot()
        {
            if (Volatile.Read(ref _inHandle) == 1 && !EmitOnHandle)
            {
                Interlocked.Increment(ref _concurrentSnapshots);
            }
            int version;
            lock (_gate)
            {
                version = _version;
            }
            var empty = new ShowController(new FakeEngine()).Snapshot();
            return empty with { ShowVersion = version };
        }

        public async Task WaitForAsync(Type type, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (!HandledQueue.Any(c => c.GetType() == type))
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException(type.Name);
                }
                await Task.Delay(5);
            }
        }

        public async Task WaitForCountAsync(int count, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (HandledQueue.Count < count)
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException(count.ToString());
                }
                await Task.Delay(5);
            }
        }
    }

    private sealed class RecordingLog : IAppLog
    {
        private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();

        public IReadOnlyList<(LogLevel Level, string Message)> Entries => [.. _entries];

        public void Write(LogLevel level, string message, IReadOnlyDictionary<string, string>? data = null) =>
            _entries.Enqueue((level, message));
    }
}
