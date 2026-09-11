namespace Aria.Core.Runtime;

using System.Collections.Immutable;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.State;

public sealed class ShowController : IShowHandler
{
    private const double MasterGainMinDb = -80.0;
    private const double MasterGainMaxDb = 12.0;
    private const double SilenceDb = -80.0;
    private static readonly TimeSpan PanicFadeMax = TimeSpan.FromMilliseconds(2000);
    private static readonly TimeSpan ClockTick = TimeSpan.FromSeconds(1);

    private readonly IAudioEngine _engine;
    private readonly PlaybackMonitor? _monitor;

    private ImmutableArray<Track> _tracks = [];
    private ImmutableArray<Playlist> _playlists = [];
    private Dictionary<TrackId, Track> _trackMap = [];
    private Dictionary<EntryId, (Playlist Playlist, int Index)> _entryMap = [];
    private PlaylistId? _activePlaylistId;
    private int _cursor;

    private readonly List<QueueItem> _queue = [];

    private TransportStatus _status = TransportStatus.Stopped;
    private DeckInstance? _current;
    private readonly HashSet<StreamHandle> _retired = [];
    private readonly HashSet<TrackId> _faulted = [];
    private bool _atEndBoundary;
    private bool _panicked;

    private double _masterGainDb;
    private bool _muted;
    private bool _locked;
    private TimeSpan _clockElapsed;
    private bool _clockRunning;
    private TimeSpan _panicFade = TimeSpan.FromMilliseconds(100);

    private int _showVersion;
    private int _transportVersion;
    private int _queueVersion;
    private int _mixerVersion;

    public Action<StateEvent>? Emitted { get; set; }

    public ShowController(IAudioEngine engine, PlaybackMonitor? monitor = null, Action<Action>? marshalEngineEvents = null)
    {
        _engine = engine;
        _monitor = monitor;
        if (marshalEngineEvents is { } marshal)
        {
            _engine.Events += e => marshal(() => OnStreamEvent(e));
        }
        else
        {
            _engine.Events += OnStreamEvent;
        }
    }

    public void Handle(ClientId client, long seq, Command command)
    {
        if (_locked && command is not (Panic or SetLocked or TickShowClock))
        {
            Reject(client, seq, "locked");
            return;
        }
        switch (command)
        {
            case LoadShow load:
                OnLoadShow(client, seq, load);
                break;
            case RestoreShow restore:
                OnRestoreShow(client, seq, restore);
                break;
            case MergeTracks merge:
                OnMergeTracks(client, seq, merge);
                break;
            case Play:
                OnPlay(client, seq);
                break;
            case Pause:
                OnPause(client, seq);
                break;
            case Stop:
                OnStop(client, seq);
                break;
            case Next:
                OnNext(client, seq);
                break;
            case Replay:
                OnReplay(client, seq);
                break;
            case Panic:
                OnPanic();
                break;
            case JumpTo jump:
                OnJumpTo(client, seq, jump);
                break;
            case EnqueueEntry enqueueEntry:
                OnEnqueueEntry(client, seq, enqueueEntry);
                break;
            case EnqueueTrack enqueueTrack:
                OnEnqueueTrack(client, seq, enqueueTrack);
                break;
            case RemoveFromQueue removeFromQueue:
                OnRemoveFromQueue(client, seq, removeFromQueue);
                break;
            case ClearQueue:
                OnClearQueue();
                break;
            case CreatePlaylist createPlaylist:
                OnCreatePlaylist(client, seq, createPlaylist);
                break;
            case RenamePlaylist renamePlaylist:
                OnRenamePlaylist(client, seq, renamePlaylist);
                break;
            case DeletePlaylist deletePlaylist:
                OnDeletePlaylist(client, seq, deletePlaylist);
                break;
            case SetActivePlaylist setActivePlaylist:
                OnSetActivePlaylist(client, seq, setActivePlaylist);
                break;
            case AddEntry addEntry:
                OnAddEntry(client, seq, addEntry);
                break;
            case RemoveEntry removeEntry:
                OnRemoveEntry(client, seq, removeEntry);
                break;
            case MoveEntry moveEntry:
                OnMoveEntry(client, seq, moveEntry);
                break;
            case SetEntryOverrides setEntryOverrides:
                OnSetEntryOverrides(client, seq, setEntryOverrides);
                break;
            case MoveQueueItem moveQueueItem:
                OnMoveQueueItem(client, seq, moveQueueItem);
                break;
            case SetMasterGain setMasterGain:
                OnSetMasterGain(client, seq, setMasterGain);
                break;
            case SetMuted setMuted:
                OnSetMuted(setMuted);
                break;
            case SetLocked setLocked:
                OnSetLocked(setLocked);
                break;
            case TickShowClock:
                OnTickShowClock();
                break;
            case ResetShowClock:
                OnResetShowClock();
                break;
            case SetPanicFade setPanicFade:
                OnSetPanicFade(client, seq, setPanicFade);
                break;
            default:
                Reject(client, seq, "unknown-command");
                break;
        }
    }

    public ShowSnapshot Snapshot() => new(
        _showVersion,
        new ShowState(_playlists, _activePlaylistId, _locked, new ShowClockState(_clockElapsed, _clockRunning)),
        _transportVersion,
        BuildTransport(),
        _queueVersion,
        new QueueState([.. _queue]),
        _mixerVersion,
        new MixerState(_masterGainDb, _muted, _panicFade));

    private void OnLoadShow(ClientId client, long seq, LoadShow load)
    {
        if (_status != TransportStatus.Stopped)
        {
            Reject(client, seq, "show-load-requires-stopped");
            return;
        }
        if (load.Tracks.IsDefault || load.Playlists.IsDefault)
        {
            Reject(client, seq, "show-data-required");
            return;
        }
        if (!ValidateShow(load.Tracks, load.Playlists, []))
        {
            Reject(client, seq, "invalid-show");
            return;
        }
        if (load.Active is { } active && !_playlists.Any(p => p.Id == active) && !load.Playlists.Any(p => p.Id == active))
        {
            Reject(client, seq, "unknown-active-playlist");
            return;
        }

        if (_current is { Handle: { } handle })
        {
            _engine.DisposeStream(handle);
            _monitor?.Unbind(handle);
        }

        _tracks = load.Tracks;
        _playlists = load.Playlists;
        _trackMap = load.Tracks.ToDictionary(t => t.Id);
        RebuildEntryMap();
        _activePlaylistId = load.Active ?? (_playlists.Length > 0 ? _playlists[0].Id : null);
        _cursor = 0;
        _queue.Clear();
        _current = null;
        _atEndBoundary = false;
        _panicked = false;
        _clockElapsed = TimeSpan.Zero;
        _clockRunning = false;

        EmitShow();
        EmitQueue();
        EmitTransport();
    }

    private void OnRestoreShow(ClientId client, long seq, RestoreShow restore)
    {
        if (_status != TransportStatus.Stopped)
        {
            Reject(client, seq, "show-load-requires-stopped");
            return;
        }
        if (restore.Tracks.IsDefault || restore.Playlists.IsDefault || restore.Queue.IsDefault)
        {
            Reject(client, seq, "show-data-required");
            return;
        }
        if (!ValidateShow(restore.Tracks, restore.Playlists, restore.Queue))
        {
            Reject(client, seq, "invalid-show");
            return;
        }
        if (restore.Active is { } active && !restore.Playlists.Any(p => p.Id == active))
        {
            Reject(client, seq, "unknown-active-playlist");
            return;
        }
        if (restore.MasterGainDb is < MasterGainMinDb or > MasterGainMaxDb)
        {
            Reject(client, seq, "gain-out-of-range");
            return;
        }
        if (restore.PanicFade < TimeSpan.Zero || restore.PanicFade > PanicFadeMax)
        {
            Reject(client, seq, "panic-fade-out-of-range");
            return;
        }
        if (restore.ClockElapsed < TimeSpan.Zero)
        {
            Reject(client, seq, "bad-clock");
            return;
        }

        if (_current is { Handle: { } handle })
        {
            _engine.DisposeStream(handle);
            _monitor?.Unbind(handle);
        }

        _tracks = restore.Tracks;
        _playlists = restore.Playlists;
        _trackMap = restore.Tracks.ToDictionary(t => t.Id);
        RebuildEntryMap();
        _activePlaylistId = restore.Active ?? (_playlists.Length > 0 ? _playlists[0].Id : null);
        _cursor = 0;
        _queue.Clear();
        _queue.AddRange(restore.Queue);
        _masterGainDb = restore.MasterGainDb;
        _muted = false;
        _panicFade = restore.PanicFade;
        _clockElapsed = restore.ClockElapsed;
        _clockRunning = restore.ClockRunning;
        _engine.SetMasterGain(restore.MasterGainDb);
        _current = null;
        _atEndBoundary = false;
        _panicked = false;

        EmitShow();
        EmitQueue();
        EmitMixer();
        EmitTransport();
    }

    private void OnMergeTracks(ClientId client, long seq, MergeTracks merge)
    {
        if (merge.Tracks.IsDefault || merge.Tracks.Length == 0)
        {
            Reject(client, seq, "show-data-required");
            return;
        }
        var incoming = new HashSet<TrackId>();
        foreach (var track in merge.Tracks)
        {
            if (!incoming.Add(track.Id))
            {
                Reject(client, seq, "duplicate-track");
                return;
            }
        }
        var added = false;
        foreach (var track in merge.Tracks)
        {
            if (_trackMap.ContainsKey(track.Id))
            {
                continue;
            }
            _tracks = _tracks.Add(track);
            _trackMap.Add(track.Id, track);
            added = true;
        }
        if (!added)
        {
            return;
        }
        EmitShow();
    }

    private static bool ValidateShow(ImmutableArray<Track> tracks, ImmutableArray<Playlist> playlists, ImmutableArray<QueueItem> queue)
    {
        var trackIds = new HashSet<TrackId>();
        foreach (var track in tracks)
        {
            if (!trackIds.Add(track.Id))
            {
                return false;
            }
        }
        var entryIds = new HashSet<EntryId>();
        foreach (var playlist in playlists)
        {
            foreach (var entry in playlist.Entries)
            {
                if (!entryIds.Add(entry.Id) || !trackIds.Contains(entry.TrackId))
                {
                    return false;
                }
            }
        }
        foreach (var item in queue)
        {
            if (!trackIds.Contains(item.TrackId) || (item.EntryId is { } entry && !entryIds.Contains(entry)))
            {
                return false;
            }
        }
        return true;
    }

    private void RebuildEntryMap()
    {
        _entryMap = [];
        foreach (var playlist in _playlists)
        {
            for (var i = 0; i < playlist.Entries.Length; i++)
            {
                _entryMap[playlist.Entries[i].Id] = (playlist, i);
            }
        }
    }

    private int IndexOfPlaylist(PlaylistId id)
    {
        for (var i = 0; i < _playlists.Length; i++)
        {
            if (_playlists[i].Id == id)
            {
                return i;
            }
        }
        return -1;
    }

    private void OnPlay(ClientId client, long seq)
    {
        if (_panicked)
        {
            _panicked = false;
            if (_current is null)
            {
                if (!StartFromOrder())
                {
                    Reject(client, seq, "nothing-to-play");
                    return;
                }
            }
            else
            {
                RestartCurrent();
            }
            StartClockIfNeeded();
            return;
        }

        switch (_status)
        {
            case TransportStatus.Playing:
                break;
            case TransportStatus.Paused when _atEndBoundary:
                AdvanceFromBoundary();
                break;
            case TransportStatus.Paused:
                if (_current?.Handle is not { } handle)
                {
                    Reject(client, seq, "no-active-stream");
                    return;
                }
                _engine.Transport(handle, TransportCommand.Play);
                _status = TransportStatus.Playing;
                EmitTransport();
                break;
            case TransportStatus.Stopped:
                if (_current is null)
                {
                    if (!StartFromOrder())
                    {
                        Reject(client, seq, "nothing-to-play");
                    }
                }
                else
                {
                    RestartCurrent();
                }
                break;
        }
        StartClockIfNeeded();
    }

    private void OnPause(ClientId client, long seq)
    {
        if (_status != TransportStatus.Playing || _current?.Handle is not { } handle)
        {
            Reject(client, seq, "not-playing");
            return;
        }
        _engine.Transport(handle, TransportCommand.Pause);
        _status = TransportStatus.Paused;
        _atEndBoundary = false;
        EmitTransport();
    }

    private void OnStop(ClientId client, long seq)
    {
        if (_status == TransportStatus.Stopped)
        {
            return;
        }
        if (_status == TransportStatus.Panicked)
        {
            _panicked = false;
        }
        if (_current is { Handle: { } handle })
        {
            _engine.Transport(handle, TransportCommand.Stop);
            _engine.DisposeStream(handle);
            _retired.Remove(handle);
            _monitor?.Unbind(handle);
            _current.Handle = null;
        }
        _status = TransportStatus.Stopped;
        _atEndBoundary = false;
        EmitTransport();
    }

    private void OnNext(ClientId client, long seq)
    {
        if (_panicked)
        {
            Reject(client, seq, "panicked");
            return;
        }
        var wasPlaying = _status == TransportStatus.Playing;
        var old = _current;
        if (!StartFromOrder())
        {
            Reject(client, seq, "nothing-to-play");
            return;
        }
        ReleaseOld(old, wasPlaying);
    }

    private void OnReplay(ClientId client, long seq)
    {
        if (_panicked)
        {
            Reject(client, seq, "panicked");
            return;
        }
        if (_current is null)
        {
            Reject(client, seq, "nothing-to-replay");
            return;
        }
        RestartCurrent();
    }

    private void OnPanic()
    {
        if (_status == TransportStatus.Panicked)
        {
            return;
        }
        _engine.Panic(new PanicSpec(_panicFade));
        if (_current is { Handle: { } handle })
        {
            _retired.Add(handle);
            _monitor?.Unbind(handle);
            _current.Handle = null;
        }
        _status = TransportStatus.Panicked;
        _panicked = true;
        _atEndBoundary = false;
        EmitTransport();
    }

    private void OnJumpTo(ClientId client, long seq, JumpTo jump)
    {
        if (_panicked)
        {
            Reject(client, seq, "panicked");
            return;
        }
        if (!_entryMap.TryGetValue(jump.Entry, out var location))
        {
            Reject(client, seq, "unknown-entry");
            return;
        }
        var wasPlaying = _status == TransportStatus.Playing;
        var old = _current;
        StartPlaylistEntry(location.Playlist, location.Index);
        ReleaseOld(old, wasPlaying);
    }

    private void OnEnqueueEntry(ClientId client, long seq, EnqueueEntry command)
    {
        if (!_entryMap.TryGetValue(command.Entry, out var location))
        {
            Reject(client, seq, "unknown-entry");
            return;
        }
        var entry = location.Playlist.Entries[location.Index];
        if (!_trackMap.TryGetValue(entry.TrackId, out var track))
        {
            Reject(client, seq, "unknown-track");
            return;
        }
        var settings = EffectiveSettings.Resolve(entry, track);
        _queue.Add(new QueueItem(entry.Id, track.Id, settings.DisplayName, settings.Color));
        EmitQueue();
        EmitTransport();
    }

    private void OnEnqueueTrack(ClientId client, long seq, EnqueueTrack command)
    {
        if (!_trackMap.TryGetValue(command.Track, out var track))
        {
            Reject(client, seq, "unknown-track");
            return;
        }
        var settings = EffectiveSettings.ForTrack(track);
        _queue.Add(new QueueItem(null, track.Id, settings.DisplayName, settings.Color));
        EmitQueue();
        EmitTransport();
    }

    private void OnRemoveFromQueue(ClientId client, long seq, RemoveFromQueue command)
    {
        if (command.Index < 0 || command.Index >= _queue.Count)
        {
            Reject(client, seq, "bad-index");
            return;
        }
        _queue.RemoveAt(command.Index);
        EmitQueue();
        EmitTransport();
    }

    private void OnClearQueue()
    {
        if (_queue.Count == 0)
        {
            return;
        }
        _queue.Clear();
        EmitQueue();
        EmitTransport();
    }

    private void OnCreatePlaylist(ClientId client, long seq, CreatePlaylist command)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            Reject(client, seq, "bad-name");
            return;
        }
        _playlists = _playlists.Add(new Playlist(PlaylistId.New(), command.Name, []));
        RebuildEntryMap();
        EmitShow();
        EmitTransport();
    }

    private void OnRenamePlaylist(ClientId client, long seq, RenamePlaylist command)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            Reject(client, seq, "bad-name");
            return;
        }
        var index = IndexOfPlaylist(command.Id);
        if (index < 0)
        {
            Reject(client, seq, "unknown-playlist");
            return;
        }
        _playlists = _playlists.SetItem(index, _playlists[index] with { Name = command.Name });
        RebuildEntryMap();
        EmitShow();
    }

    private void OnDeletePlaylist(ClientId client, long seq, DeletePlaylist command)
    {
        var index = IndexOfPlaylist(command.Id);
        if (index < 0)
        {
            Reject(client, seq, "unknown-playlist");
            return;
        }
        _playlists = _playlists.RemoveAt(index);
        if (_activePlaylistId == command.Id)
        {
            _activePlaylistId = null;
            _cursor = 0;
        }
        RebuildEntryMap();
        EmitShow();
        EmitTransport();
    }

    private void OnSetActivePlaylist(ClientId client, long seq, SetActivePlaylist command)
    {
        if (_panicked)
        {
            Reject(client, seq, "panicked");
            return;
        }
        var index = IndexOfPlaylist(command.Id);
        if (index < 0)
        {
            Reject(client, seq, "unknown-playlist");
            return;
        }
        _activePlaylistId = _playlists[index].Id;
        _cursor = 0;
        EmitShow();
        EmitTransport();
    }

    private void OnAddEntry(ClientId client, long seq, AddEntry command)
    {
        var index = IndexOfPlaylist(command.Playlist);
        if (index < 0)
        {
            Reject(client, seq, "unknown-playlist");
            return;
        }
        if (!_trackMap.ContainsKey(command.Track))
        {
            Reject(client, seq, "unknown-track");
            return;
        }
        var playlist = _playlists[index];
        if (command.Index is { } position && (position < 0 || position > playlist.Entries.Length))
        {
            Reject(client, seq, "bad-index");
            return;
        }
        var entry = new PlaylistEntry(EntryId.New(), command.Track);
        var entries = command.Index is { } at ? playlist.Entries.Insert(at, entry) : playlist.Entries.Add(entry);
        _playlists = _playlists.SetItem(index, playlist with { Entries = entries });
        RebuildEntryMap();
        EmitShow();
        EmitTransport();
    }

    private void OnRemoveEntry(ClientId client, long seq, RemoveEntry command)
    {
        if (!_entryMap.TryGetValue(command.Entry, out var location))
        {
            Reject(client, seq, "unknown-entry");
            return;
        }
        var playlist = location.Playlist;
        _playlists = _playlists.SetItem(IndexOfPlaylist(playlist.Id), playlist with { Entries = playlist.Entries.RemoveAt(location.Index) });
        RebuildEntryMap();
        EmitShow();
        EmitTransport();
    }

    private void OnMoveEntry(ClientId client, long seq, MoveEntry command)
    {
        if (!_entryMap.TryGetValue(command.Entry, out var location))
        {
            Reject(client, seq, "unknown-entry");
            return;
        }
        var playlist = location.Playlist;
        if (command.NewIndex < 0 || command.NewIndex > playlist.Entries.Length - 1)
        {
            Reject(client, seq, "bad-index");
            return;
        }
        var entry = playlist.Entries[location.Index];
        var entries = playlist.Entries.RemoveAt(location.Index).Insert(command.NewIndex, entry);
        _playlists = _playlists.SetItem(IndexOfPlaylist(playlist.Id), playlist with { Entries = entries });
        RebuildEntryMap();
        EmitShow();
        EmitTransport();
    }

    private void OnSetEntryOverrides(ClientId client, long seq, SetEntryOverrides command)
    {
        if (!_entryMap.TryGetValue(command.Entry, out var location))
        {
            Reject(client, seq, "unknown-entry");
            return;
        }
        var playlist = location.Playlist;
        var entry = playlist.Entries[location.Index];
        var entries = playlist.Entries.SetItem(location.Index, entry with { Overrides = command.Overrides });
        _playlists = _playlists.SetItem(IndexOfPlaylist(playlist.Id), playlist with { Entries = entries });
        RebuildEntryMap();
        EmitShow();
        EmitTransport();
    }

    private void OnMoveQueueItem(ClientId client, long seq, MoveQueueItem command)
    {
        if (command.From < 0 || command.From >= _queue.Count || command.To < 0 || command.To > _queue.Count - 1)
        {
            Reject(client, seq, "bad-index");
            return;
        }
        var item = _queue[command.From];
        _queue.RemoveAt(command.From);
        _queue.Insert(command.To, item);
        EmitQueue();
        EmitTransport();
    }

    private void OnSetMasterGain(ClientId client, long seq, SetMasterGain command)
    {
        if (command.GainDb is < MasterGainMinDb or > MasterGainMaxDb)
        {
            Reject(client, seq, "gain-out-of-range");
            return;
        }
        _masterGainDb = command.GainDb;
        _muted = false;
        _engine.SetMasterGain(command.GainDb);
        EmitMixer();
    }

    private void OnSetMuted(SetMuted command)
    {
        if (command.Muted == _muted)
        {
            return;
        }
        _muted = command.Muted;
        _engine.SetMasterGain(_muted ? SilenceDb : _masterGainDb);
        EmitMixer();
    }

    private void OnSetLocked(SetLocked command)
    {
        if (command.Locked == _locked)
        {
            return;
        }
        _locked = command.Locked;
        EmitShow();
    }

    private void OnTickShowClock()
    {
        if (!_clockRunning)
        {
            return;
        }
        _clockElapsed += ClockTick;
        EmitShow();
    }

    private void OnResetShowClock()
    {
        _clockElapsed = TimeSpan.Zero;
        _clockRunning = false;
        EmitShow();
    }

    private void StartClockIfNeeded()
    {
        if (_clockRunning)
        {
            return;
        }
        _clockRunning = true;
        EmitShow();
    }

    private void OnSetPanicFade(ClientId client, long seq, SetPanicFade command)
    {
        if (command.Duration < TimeSpan.Zero || command.Duration > PanicFadeMax)
        {
            Reject(client, seq, "panic-fade-out-of-range");
            return;
        }
        _panicFade = command.Duration;
        EmitMixer();
    }

    private void OnStreamEvent(StreamEvent e)
    {
        if (_current is { Handle: { } currentHandle } && currentHandle == e.Handle)
        {
            HandleCurrentEvent(e);
            return;
        }
        if (_retired.Contains(e.Handle))
        {
            _retired.Remove(e.Handle);
            if (e.Kind == StreamEventKind.Ended)
            {
                _engine.DisposeStream(e.Handle);
                _monitor?.Unbind(e.Handle);
            }
        }
    }

    private void HandleCurrentEvent(StreamEvent e)
    {
        if (e.Kind == StreamEventKind.Faulted)
        {
            if (_current is { } failed)
            {
                _faulted.Add(failed.Track.Id);
            }
            DisposeCurrentHandle();
            if (!StartFromOrder())
            {
                _status = TransportStatus.Stopped;
                _current = null;
            }
            EmitTransport();
            return;
        }

        switch (e.Reason)
        {
            case StreamEndReason.Completed:
            case StreamEndReason.CueOutReached:
            case StreamEndReason.StoppedByMarker:
                DisposeCurrentHandle();
                ApplyEndAction();
                break;
        }
    }

    private void ApplyEndAction()
    {
        switch (_current!.Settings.EndAction)
        {
            case EndAction.Pause:
                _status = TransportStatus.Paused;
                _atEndBoundary = true;
                EmitTransport();
                break;
            case EndAction.Stop:
                _status = TransportStatus.Stopped;
                _atEndBoundary = false;
                EmitTransport();
                break;
            case EndAction.Replay:
                RestartCurrent();
                break;
            case EndAction.Advance:
                AdvanceFromBoundary();
                break;
        }
    }

    private void AdvanceFromBoundary()
    {
        if (!StartFromOrder())
        {
            _status = TransportStatus.Stopped;
            _atEndBoundary = false;
            _current = null;
            EmitTransport();
        }
    }

    private bool StartFromOrder()
    {
        if (_queue.Count > 0)
        {
            var item = _queue[0];
            _queue.RemoveAt(0);
            var track = _trackMap[item.TrackId];
            var settings = item.EntryId is { } entryId && _entryMap.TryGetValue(entryId, out var location)
                ? EffectiveSettings.Resolve(location.Playlist.Entries[location.Index], track)
                : EffectiveSettings.ForTrack(track);
            _current = new DeckInstance { Entry = item.EntryId, Track = track, Settings = settings };
            StartStreamFor(_current);
            _status = TransportStatus.Playing;
            _atEndBoundary = false;
            _panicked = false;
            EmitQueue();
            EmitTransport();
            return true;
        }

        if (_activePlaylistId is { } playlistId)
        {
            var playlist = _playlists.First(p => p.Id == playlistId);
            if (_cursor < playlist.Entries.Length)
            {
                StartPlaylistEntry(playlist, _cursor);
                return true;
            }
        }
        return false;
    }

    private void StartPlaylistEntry(Playlist playlist, int index)
    {
        var entry = playlist.Entries[index];
        var track = _trackMap[entry.TrackId];
        var settings = EffectiveSettings.Resolve(entry, track);
        _activePlaylistId = playlist.Id;
        _cursor = index + 1;
        _current = new DeckInstance { Entry = entry.Id, Track = track, Settings = settings };
        StartStreamFor(_current);
        _status = TransportStatus.Playing;
        _atEndBoundary = false;
        _panicked = false;
        EmitShow();
        EmitTransport();
    }

    private void StartStreamFor(DeckInstance deck)
    {
        if (deck.Handle is { } previous)
        {
            _monitor?.Unbind(previous);
        }
        _faulted.Remove(deck.Track.Id);
        var settings = deck.Settings;
        var source = new TrackSource(deck.Track.FilePath, settings.CueIn, settings.CueOut);
        var options = new StreamOptions(
            StreamBus.Main,
            settings.Markers.Select(m => new MarkerSpec(m.Name, m.Position, m.Action)).ToImmutableArray());
        var handle = _engine.StartStream(source, options);
        deck.Handle = handle;
        _monitor?.Bind(handle, Content(deck));

        var fadeIn = settings.In;
        var mix = fadeIn.Duration > TimeSpan.Zero
            ? new MixParameters(settings.GainDb, new FadeSpec(fadeIn.Duration, fadeIn.Curve, settings.GainDb, StopWhenDone: false))
            : new MixParameters(settings.GainDb, null);
        _engine.SetMix(handle, mix);
        _engine.Transport(handle, TransportCommand.Play);
    }

    private void RestartCurrent()
    {
        var deck = _current!;
        if (deck.Handle is { } handle)
        {
            _engine.Transport(handle, TransportCommand.Stop);
            _engine.DisposeStream(handle);
            _retired.Remove(handle);
            _monitor?.Unbind(handle);
        }
        StartStreamFor(deck);
        _status = TransportStatus.Playing;
        _atEndBoundary = false;
        _panicked = false;
        EmitTransport();
    }

    private void ReleaseOld(DeckInstance? old, bool wasPlaying)
    {
        if (old?.Handle is not { } handle)
        {
            return;
        }
        var fadeOut = wasPlaying ? old.Settings.Out : null;
        if (fadeOut is { } fade && fade.Duration > TimeSpan.Zero)
        {
            _engine.SetMix(handle, new MixParameters(old.Settings.GainDb, new FadeSpec(fade.Duration, fade.Curve, SilenceDb, StopWhenDone: true)));
            _retired.Add(handle);
            _monitor?.Unbind(handle);
        }
        else
        {
            _engine.Transport(handle, TransportCommand.Stop);
            _engine.DisposeStream(handle);
            _monitor?.Unbind(handle);
        }
    }

    private void DisposeCurrentHandle()
    {
        if (_current is { Handle: { } handle })
        {
            _engine.DisposeStream(handle);
            _monitor?.Unbind(handle);
            _current.Handle = null;
        }
    }

    private TransportState BuildTransport()
    {
        var current = _current is null ? null : Content(_current);
        return new TransportState(_status, current, PeekNext(), [.. _faulted]);
    }

    private static DeckContent Content(DeckInstance deck) => new(
        deck.Entry,
        deck.Track.Id,
        deck.Settings.DisplayName,
        deck.Settings.Color,
        deck.Settings.EndAction,
        deck.Track.Duration,
        deck.Settings.CueIn,
        deck.Settings.CueOut);

    private DeckContent? PeekNext()
    {
        if (_queue.Count > 0)
        {
            var item = _queue[0];
            var track = _trackMap[item.TrackId];
            var settings = item.EntryId is { } entryId && _entryMap.TryGetValue(entryId, out var location)
                ? EffectiveSettings.Resolve(location.Playlist.Entries[location.Index], track)
                : EffectiveSettings.ForTrack(track);
            return new DeckContent(item.EntryId, track.Id, settings.DisplayName, settings.Color, settings.EndAction, track.Duration, settings.CueIn, settings.CueOut);
        }

        if (_activePlaylistId is { } playlistId)
        {
            var playlist = _playlists.FirstOrDefault(p => p.Id == playlistId);
            if (playlist is not null && _cursor < playlist.Entries.Length)
            {
                var entry = playlist.Entries[_cursor];
                var track = _trackMap[entry.TrackId];
                var settings = EffectiveSettings.Resolve(entry, track);
                return new DeckContent(entry.Id, track.Id, settings.DisplayName, settings.Color, settings.EndAction, track.Duration, settings.CueIn, settings.CueOut);
            }
        }
        return null;
    }

    private void Reject(ClientId client, long seq, string reason) => Emitted?.Invoke(new Rejected(client, seq, reason));

    private void Emit(StateEvent e) => Emitted?.Invoke(e);

    private void EmitShow() => Emit(new ShowDelta(++_showVersion, new ShowState(_playlists, _activePlaylistId, _locked, new ShowClockState(_clockElapsed, _clockRunning))));

    private void EmitTransport() => Emit(new TransportDelta(++_transportVersion, BuildTransport()));

    private void EmitQueue() => Emit(new QueueDelta(++_queueVersion, new QueueState([.. _queue])));

    private void EmitMixer() => Emit(new MixerDelta(++_mixerVersion, new MixerState(_masterGainDb, _muted, _panicFade)));

    private sealed class DeckInstance
    {
        public required EntryId? Entry { get; init; }
        public required Track Track { get; init; }
        public required PlaybackSettings Settings { get; init; }
        public StreamHandle? Handle { get; set; }
    }
}
