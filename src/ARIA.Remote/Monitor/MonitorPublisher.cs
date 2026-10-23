namespace Aria.Remote.Monitor;

using Aria.Audio;
using Aria.Core.Model;
using Aria.Core.State;

internal sealed class MonitorPublisher : IDisposable
{
    private const int SampleRate = 48000;
    private const long OutputTimeoutUs = 2_000_000;

    private readonly MonitorFeed _feed;
    private readonly MonitorHub _hub;
    private readonly Action<Exception>? _onFault;
    private readonly OutputClock _clock = new(SampleRate);
    private readonly MixBlockReport _scratch = new();
    private readonly CancellationTokenSource _stop = new();
    private Thread? _thread;
    private TransportState? _pendingTransport;
    private long _nextSequence;
    private long _firstTickUs = long.MinValue;
    private long _lastStampUs = long.MinValue;
    private bool? _outputOk;

    public MonitorPublisher(MonitorFeed feed, MonitorHub hub, Func<TrackId, bool> available, Action<Exception>? onFault = null)
    {
        _feed = feed;
        _hub = hub;
        _onFault = onFault;
        Builder = new MonitorTimelineBuilder(handle => feed.Bindings.TryGetDeck(handle, out var deck) ? deck : null, feed.Sources, available);
    }

    public MonitorTimelineBuilder Builder { get; }

    public void SetTransport(TransportState transport) => Volatile.Write(ref _pendingTransport, transport);

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "aria-monitor" };
        _thread.Start();
    }

    public void Dispose()
    {
        _stop.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(1));
        _stop.Dispose();
    }

    internal void Tick(long nowUs)
    {
        try
        {
            Step(nowUs);
        }
        catch (Exception e)
        {
            _onFault?.Invoke(e);
        }
    }

    private void Run()
    {
        while (!_stop.IsCancellationRequested)
        {
            Tick(HostClock.NowUs());
            Thread.Sleep(2);
        }
    }

    private void Step(long nowUs)
    {
        if (_firstTickUs == long.MinValue)
        {
            _firstTickUs = nowUs;
        }
        if (_feed.Stamps.TryReadOutputStamp(out var stamp))
        {
            _clock.Observe(stamp);
            _lastStampUs = nowUs;
            SetOutput(true);
        }
        else if (nowUs - Math.Max(_lastStampUs, _firstTickUs) > OutputTimeoutUs)
        {
            SetOutput(false);
        }
        _clock.DeviceBufferFrames = _feed.Stamps.DeviceBufferFrames;
        _clock.HallTrimMs = _feed.HallTrimMs();
        if (Interlocked.Exchange(ref _pendingTransport, null) is { } transport)
        {
            Builder.SetTransport(transport);
        }
        var force = DrainReports();
        if (Builder.Build(_clock, nowUs, force) is { } message)
        {
            _hub.BroadcastTimeline(message);
        }
    }

    private bool DrainReports()
    {
        var latest = _feed.Reports.LatestSequence;
        var force = false;
        if (_nextSequence == 0)
        {
            _nextSequence = Math.Max(1, latest);
        }
        if (latest - _nextSequence >= MixReportRing.Capacity)
        {
            _nextSequence = latest;
            force = true;
        }
        while (_nextSequence <= latest)
        {
            if (_feed.Reports.TryRead(_nextSequence, _scratch))
            {
                Builder.Apply(_scratch);
            }
            else
            {
                force = true;
            }
            _nextSequence++;
        }
        return force;
    }

    private void SetOutput(bool ok)
    {
        if (_outputOk == ok)
        {
            return;
        }
        _outputOk = ok;
        _hub.BroadcastStatus(ok);
    }
}
