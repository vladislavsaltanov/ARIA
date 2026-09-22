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
    private readonly ILibraryStore? _libraryStore;
    private readonly IAppLog _log;
    private readonly Action<Exception>? _libraryError;
    private bool _libraryFailed;
    private readonly IDisposable _subscription;
    private readonly object _gate = new();
    private System.Threading.Timer? _timer;

    public ShowAutosaver(ICommandBus bus, ISnapshotStore store, TimeSpan debounce, Func<ImmutableArray<Track>>? tracksSource = null, ILibraryStore? libraryStore = null, IAppLog? log = null, Action<Exception>? libraryError = null)
    {
        _bus = bus;
        _store = store;
        _debounce = debounce;
        _tracksSource = tracksSource;
        _libraryStore = libraryStore;
        _log = log ?? NullAppLog.Instance;
        _libraryError = libraryError;
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
                _timer = new System.Threading.Timer(_ => BackgroundFlush(), null, _debounce, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void BackgroundFlush()
    {
        try
        {
            FlushNow();
        }
        catch (IOException)
        {
        }
    }

    private void Save()
    {
        var snapshot = _bus.Snapshot();
        var tracks = _tracksSource?.Invoke() ?? [];
        var document = new ShowDocument(
            tracks,
            snapshot.Show.Projects,
            snapshot.Show.ActiveId,
            snapshot.Queue.Items,
            snapshot.Mixer.MasterGainDb,
            snapshot.Mixer.PanicFade,
            snapshot.Show.Clock.Elapsed,
            snapshot.Show.Clock.Running,
            snapshot.Show.Scripts,
            DateTimeOffset.UtcNow,
            snapshot.Mixer.EffectiveGlobal);
        _store.Save(document);
        if (_libraryStore is not null && !tracks.IsEmpty)
        {
            try
            {
                _libraryStore.Upsert(tracks, snapshot.Show.Projects);
                _libraryFailed = false;
            }
            catch (Exception e)
            {
                _log.Error("library.upsert_failed", new Dictionary<string, string> { ["error"] = e.Message });
                if (!_libraryFailed)
                {
                    _libraryFailed = true;
                    _libraryError?.Invoke(e);
                }
            }
        }
    }
}
