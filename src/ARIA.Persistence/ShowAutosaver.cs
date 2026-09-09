namespace Aria.Persistence;

using System.Collections.Immutable;
using Aria.Core.Model;
using Aria.Core.Runtime;
using Aria.Core.State;

public sealed class ShowAutosaver : IDisposable
{
    private readonly ICommandBus _bus;
    private readonly ISnapshotStore _store;
    private readonly TimeSpan _debounce;
    private readonly Func<ImmutableArray<Track>>? _tracksSource;
    private readonly IDisposable _subscription;
    private readonly object _gate = new();
    private System.Threading.Timer? _timer;

    public ShowAutosaver(ICommandBus bus, ISnapshotStore store, TimeSpan debounce, Func<ImmutableArray<Track>>? tracksSource = null)
    {
        _bus = bus;
        _store = store;
        _debounce = debounce;
        _tracksSource = tracksSource;
        _subscription = bus.Subscribe(OnEvent);
    }

    public void FlushNow()
    {
        lock (_gate)
        {
            Save();
        }
    }

    public void Dispose()
    {
        _subscription.Dispose();
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void OnEvent(StateEvent e)
    {
        if (e is not (ShowDelta or QueueDelta or MixerDelta))
        {
            return;
        }
        lock (_gate)
        {
            if (_timer is { } existing)
            {
                existing.Change(_debounce, Timeout.InfiniteTimeSpan);
            }
            else
            {
                _timer = new System.Threading.Timer(_ => FlushNow(), null, _debounce, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void Save()
    {
        var snapshot = _bus.Snapshot();
        var document = new ShowDocument(
            _tracksSource?.Invoke() ?? [],
            snapshot.Show.Playlists,
            snapshot.Show.ActiveId,
            snapshot.Queue.Items,
            snapshot.Mixer.MasterGainDb,
            snapshot.Mixer.PanicFade,
            DateTimeOffset.UtcNow);
        _store.Save(document);
    }
}
