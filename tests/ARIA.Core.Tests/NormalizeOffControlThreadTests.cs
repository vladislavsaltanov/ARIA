namespace Aria.Core.Tests;

using System.Runtime.CompilerServices;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;
using Aria.Core.State;

public sealed class NormalizeOffControlThreadTests : IDisposable
{
    private static readonly ClientId Client = new("normalize-async");

    private readonly ManualResetEventSlim _gate = new(false);
    private readonly FakeEngine _engine = new();
    private readonly StrongBox<CommandBus> _busHolder = new();
    private readonly CommandBus _bus;
    private readonly ShowController _controller;
    private readonly List<StateEvent> _events = [];
    private readonly Track _first;
    private readonly Track _second;
    private readonly Project _project;
    private long _seq;

    public NormalizeOffControlThreadTests()
    {
        _engine.ScanLufs = _ =>
        {
            _gate.Wait(TimeSpan.FromSeconds(30));
            return -10.0;
        };
        _controller = new ShowController(_engine, null, work => _busHolder.Value!.Post(work));
        _bus = new CommandBus(_controller, BusMode.Pumped);
        _busHolder.Value = _bus;
        _bus.Subscribe(e =>
        {
            lock (_events)
            {
                _events.Add(e);
            }
        });
        _first = TestShow.Track("one");
        _second = TestShow.Track("two");
        _project = TestShow.Project("Main", TestShow.Entry(_first), TestShow.Entry(_second));
        Submit(new LoadShow([_first, _second], [_project], _project.Id));
        Submit(new SetGlobalAudio(GlobalAudioSettings.Default with { NormalizeEnabled = true }));
        var deadline = Environment.TickCount64 + 10_000;
        while (!_bus.Snapshot().Mixer.EffectiveGlobal.NormalizeEnabled && Environment.TickCount64 < deadline)
        {
            Thread.Sleep(5);
        }
    }

    [Fact]
    public async Task NormalizeTrack_SlowScan_KeepsControlThreadResponsive()
    {
        Submit(new NormalizeTrack(_first.Id));
        Submit(new SetMasterGain(-6));

        await Poll(() => _engine.MasterGains.Contains(-6), "control thread blocked behind LUFS scan");

        _gate.Set();

        await Poll(() => MeasuredLufs(_first.Id) == -10.0, "measurement never applied after scan");
    }

    [Fact]
    public async Task NormalizeProject_SlowScan_KeepsPanicResponsive()
    {
        Submit(new NormalizeProject(_project.Id));
        Submit(new Panic());

        await Poll(() => _engine.Panics.Count == 1, "panic blocked behind project LUFS scan");

        _gate.Set();

        await Poll(() => MeasuredLufs(_first.Id) == -10.0 && MeasuredLufs(_second.Id) == -10.0, "project measurement never applied");
    }

    [Fact]
    public async Task NormalizeTrack_TrackRemovedDuringScan_RejectsInsteadOfResurrecting()
    {
        var seq = Submit(new NormalizeTrack(_first.Id));
        Submit(new LoadShow([_second], [TestShow.Project("Other", TestShow.Entry(_second))], null));

        _gate.Set();

        await Poll(() => RejectionOf(seq) is not null, "stale scan never resolved");
        Assert.Equal("unknown-track", RejectionOf(seq)!.Reason);
        Assert.DoesNotContain(_bus.Snapshot().Show.Projects.SelectMany(p => p.Entries), e => e.TrackId == _first.Id);
    }

    public void Dispose()
    {
        _gate.Set();
        _bus.Dispose();
        _gate.Dispose();
    }

    private double? MeasuredLufs(TrackId id) => _controller.TrackAudio(id)?.MeasuredLufs;

    private Rejected? RejectionOf(long seq)
    {
        lock (_events)
        {
            return _events.OfType<Rejected>().LastOrDefault(r => r.Seq == seq);
        }
    }

    private long Submit(Command command)
    {
        var seq = ++_seq;
        _bus.Submit(Client, seq, command);
        return seq;
    }

    private static async Task Poll(Func<bool> condition, string message)
    {
        var deadline = Environment.TickCount64 + 10_000;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException(message);
            }
            await Task.Delay(10);
        }
    }
}
