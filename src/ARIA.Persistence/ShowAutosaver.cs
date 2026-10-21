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
    private bool _disposed;
    private ImmutableArray<Track> _upsertedTracks;
    private ImmutableArray<Project> _upsertedProjects;

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
            if (_disposed)
            {
                return;
            }
            TrySave();
        }
    }

    public void Dispose()
    {
        _subscription.Dispose();
        lock (_gate)
        {
            _disposed = true;
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

    private void BackgroundFlush() => FlushNow();

    private void TrySave()
    {
        try
        {
            Save();
        }
        catch (Exception e)
        {
            _log.Error("snapshot.save_failed", new Dictionary<string, string> { ["error"] = e.Message });
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
        if (_libraryStore is not null && !tracks.IsEmpty && !LibraryUnchanged(tracks, snapshot.Show.Projects))
        {
            try
            {
                _libraryStore.Upsert(tracks, snapshot.Show.Projects);
                _libraryFailed = false;
                _upsertedTracks = tracks;
                _upsertedProjects = snapshot.Show.Projects;
            }
            catch (Exception e)
            {
                _upsertedTracks = default;
                _log.Error("library.upsert_failed", new Dictionary<string, string> { ["error"] = e.Message });
                if (!_libraryFailed)
                {
                    _libraryFailed = true;
                    _libraryError?.Invoke(e);
                }
            }
        }
    }

    private bool LibraryUnchanged(ImmutableArray<Track> tracks, ImmutableArray<Project> projects) =>
        !_upsertedTracks.IsDefault && _upsertedTracks == tracks && _upsertedProjects == projects;
}
