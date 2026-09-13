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
    private static readonly TimeSpan SmoothingMax = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ClockTick = TimeSpan.FromSeconds(1);

    private readonly IAudioEngine _engine;
    private readonly PlaybackMonitor? _monitor;
    private readonly Action<Action>? _marshal;

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
    private Smoothing _smoothing = Smoothing.Default;
    private bool _preRolled;
    private ImmutableArray<Script> _scripts = [];
    private TrackDigest _emittedDigest = TrackDigest.Empty;

    private int _showVersion;
    private int _transportVersion;
    private int _queueVersion;
    private int _mixerVersion;

    public Action<StateEvent>? Emitted { get; set; }

    public ShowController(IAudioEngine engine, PlaybackMonitor? monitor = null, Action<Action>? marshalEngineEvents = null)
    {
        _engine = engine;
        _monitor = monitor;
        _marshal = marshalEngineEvents;
        if (marshalEngineEvents is { } marshal)
        {
            _engine.Events += e => marshal(() => OnStreamEvent(e));
        }
        else
        {
            _engine.Events += OnStreamEvent;
        }
        if (monitor is not null)
        {
            monitor.Changed += OnMonitorPosition;
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
            case SeekTo seek:
                OnSeekTo(client, seq, seek);
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
            case ImportPlaylist importPlaylist:
                OnImportPlaylist(client, seq, importPlaylist);
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
            case CreateScript createScript:
                OnCreateScript(client, seq, createScript);
                break;
            case RenameScript renameScript:
                OnRenameScript(client, seq, renameScript);
                break;
            case DeleteScript deleteScript:
                OnDeleteScript(client, seq, deleteScript);
                break;
            case AddScriptLine addScriptLine:
                OnAddScriptLine(client, seq, addScriptLine);
                break;
            case UpdateScriptLine updateScriptLine:
                OnUpdateScriptLine(client, seq, updateScriptLine);
                break;
            case RemoveScriptLine removeScriptLine:
                OnRemoveScriptLine(client, seq, removeScriptLine);
                break;
            case MoveScriptLine moveScriptLine:
                OnMoveScriptLine(client, seq, moveScriptLine);
                break;
            case TickShowClock:
                OnTickShowClock();
                break;
            case StartShowClock:
                OnStartShowClock();
                break;
            case PauseShowClock:
                OnPauseShowClock();
                break;
            case ResetShowClock:
                OnResetShowClock();
                break;
            case SetPanicFade setPanicFade:
                OnSetPanicFade(client, seq, setPanicFade);
                break;
            case SetSmoothing setSmoothing:
                OnSetSmoothing(client, seq, setSmoothing);
                break;
            default:
                Reject(client, seq, "unknown-command");
                break;
        }
    }

    public ShowSnapshot Snapshot() => new(
        _showVersion,
        new ShowState(_playlists, _activePlaylistId, _locked, new ShowClockState(_clockElapsed, _clockRunning), _scripts, _emittedDigest),
        _transportVersion,
        BuildTransport(),
        _queueVersion,
        new QueueState([.. _queue]),
        _mixerVersion,
        new MixerState(_masterGainDb, _muted, _panicFade, _smoothing));

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
        _scripts = load.Scripts.IsDefault ? [] : load.Scripts;

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
        _clockElapsed = TimeSpan.Zero;
        _clockRunning = false;
        _scripts = restore.Scripts.IsDefault ? [] : restore.Scripts;
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
            if (_trackMap.TryGetValue(track.Id, out var existing))
            {
                if (!existing.Equals(track))
                {
                    _tracks = _tracks.Replace(existing, track);
                    _trackMap[track.Id] = track;
                    added = true;
                }
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
            if (_smoothing.Enabled && _smoothing.StopFade > TimeSpan.Zero)
            {
                _engine.SetMix(handle, new MixParameters(_current.Settings.GainDb, new FadeSpec(_smoothing.StopFade, FadeCurve.Linear, SilenceDb, StopWhenDone: true)));
                _retired.Add(handle);
                _monitor?.Unbind(handle);
                _current.Handle = null;
            }
            else
            {
                _engine.Transport(handle, TransportCommand.Stop);
                _engine.DisposeStream(handle);
                _retired.Remove(handle);
                _monitor?.Unbind(handle);
                _current.Handle = null;
            }
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
        ReleaseOld(old, wasPlaying, manual: true);
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

    private void OnSeekTo(ClientId client, long seq, SeekTo command)
    {
        if (_status is not (TransportStatus.Playing or TransportStatus.Paused) || _current is not { Handle: { } handle, Settings: { } settings } deck)
        {
            Reject(client, seq, "nothing-to-seek");
            return;
        }
        var end = settings.CueOut ?? deck.Track.Duration;
        var filePosition = command.FilePosition;
        if (filePosition < settings.CueIn)
        {
            filePosition = settings.CueIn;
        }
        if (filePosition > end)
        {
            filePosition = end;
        }
        _engine.Seek(handle, filePosition - settings.CueIn);
        _preRolled = false;
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
        StartPlaylistEntry(location.Playlist, location.Index, auto: false);
        ReleaseOld(old, wasPlaying, manual: true);
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
        SyncDigest();
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
        SyncDigest();
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
        SyncDigest();
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
        SyncDigest();
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

    private void OnImportPlaylist(ClientId client, long seq, ImportPlaylist command)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            Reject(client, seq, "bad-name");
            return;
        }
        if (command.Entries.IsDefaultOrEmpty)
        {
            Reject(client, seq, "empty-playlist");
            return;
        }
        foreach (var entry in command.Entries)
        {
            if (!_trackMap.ContainsKey(entry.Track))
            {
                Reject(client, seq, "unknown-track");
                return;
            }
        }
        var entries = command.Entries.Select(imported =>
            new PlaylistEntry(EntryId.New(), imported.Track, imported.Overrides));
        _playlists = _playlists.Add(new Playlist(PlaylistId.New(), command.Name, [.. entries]));
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

    private int IndexOfScript(ScriptId id)
    {
        for (var i = 0; i < _scripts.Length; i++)
        {
            if (_scripts[i].Id == id)
            {
                return i;
            }
        }
        return -1;
    }

    private static int IndexOfLine(Script script, ScriptLineId line)
    {
        for (var i = 0; i < script.Lines.Length; i++)
        {
            if (script.Lines[i].Id == line)
            {
                return i;
            }
        }
        return -1;
    }

    private void OnCreateScript(ClientId client, long seq, CreateScript command)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            Reject(client, seq, "bad-name");
            return;
        }
        _scripts = _scripts.Add(new Script(ScriptId.New(), command.Name, []));
        EmitShow();
    }

    private void OnRenameScript(ClientId client, long seq, RenameScript command)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            Reject(client, seq, "bad-name");
            return;
        }
        var index = IndexOfScript(command.Id);
        if (index < 0)
        {
            Reject(client, seq, "unknown-script");
            return;
        }
        _scripts = _scripts.SetItem(index, _scripts[index] with { Name = command.Name });
        EmitShow();
    }

    private void OnDeleteScript(ClientId client, long seq, DeleteScript command)
    {
        var index = IndexOfScript(command.Id);
        if (index < 0)
        {
            Reject(client, seq, "unknown-script");
            return;
        }
        _scripts = _scripts.RemoveAt(index);
        EmitShow();
    }

    private static ImmutableArray<Mention> ToMentions(ImmutableArray<TrackId> tracks) =>
        tracks.IsDefault ? [] : [.. tracks.Select(m => new Mention(m))];

    private void OnAddScriptLine(ClientId client, long seq, AddScriptLine command)
    {
        var index = IndexOfScript(command.Script);
        if (index < 0)
        {
            Reject(client, seq, "unknown-script");
            return;
        }
        if (command.AtElapsed < TimeSpan.Zero)
        {
            Reject(client, seq, "bad-time");
            return;
        }
        var mentions = ToMentions(command.Mentions);
        var script = _scripts[index];
        var line = new ScriptLine(ScriptLineId.New(), command.AtElapsed, command.Text, mentions);
        _scripts = _scripts.SetItem(index, script with { Lines = script.Lines.Add(line) });
        EmitShow();
    }

    private void OnUpdateScriptLine(ClientId client, long seq, UpdateScriptLine command)
    {
        var index = IndexOfScript(command.Script);
        if (index < 0)
        {
            Reject(client, seq, "unknown-script");
            return;
        }
        var script = _scripts[index];
        var lineIndex = IndexOfLine(script, command.Line);
        if (lineIndex < 0)
        {
            Reject(client, seq, "unknown-line");
            return;
        }
        if (command.AtElapsed < TimeSpan.Zero)
        {
            Reject(client, seq, "bad-time");
            return;
        }
        var mentions = ToMentions(command.Mentions);
        var line = script.Lines[lineIndex] with { AtElapsed = command.AtElapsed, Text = command.Text, Mentions = mentions };
        _scripts = _scripts.SetItem(index, script with { Lines = script.Lines.SetItem(lineIndex, line) });
        EmitShow();
    }

    private void OnRemoveScriptLine(ClientId client, long seq, RemoveScriptLine command)
    {
        var index = IndexOfScript(command.Script);
        if (index < 0)
        {
            Reject(client, seq, "unknown-script");
            return;
        }
        var script = _scripts[index];
        var lineIndex = IndexOfLine(script, command.Line);
        if (lineIndex < 0)
        {
            Reject(client, seq, "unknown-line");
            return;
        }
        _scripts = _scripts.SetItem(index, script with { Lines = script.Lines.RemoveAt(lineIndex) });
        EmitShow();
    }

    private void OnMoveScriptLine(ClientId client, long seq, MoveScriptLine command)
    {
        var index = IndexOfScript(command.Script);
        if (index < 0)
        {
            Reject(client, seq, "unknown-script");
            return;
        }
        var script = _scripts[index];
        var lineIndex = IndexOfLine(script, command.Line);
        if (lineIndex < 0)
        {
            Reject(client, seq, "unknown-line");
            return;
        }
        if (command.NewIndex < 0 || command.NewIndex > script.Lines.Length - 1)
        {
            Reject(client, seq, "bad-index");
            return;
        }
        var line = script.Lines[lineIndex];
        var lines = script.Lines.RemoveAt(lineIndex).Insert(command.NewIndex, line);
        _scripts = _scripts.SetItem(index, script with { Lines = lines });
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

    private void OnStartShowClock()
    {
        if (_clockRunning)
        {
            return;
        }
        _clockRunning = true;
        EmitShow();
    }

    private void OnPauseShowClock()
    {
        if (!_clockRunning)
        {
            return;
        }
        _clockRunning = false;
        EmitShow();
    }

    private void OnResetShowClock()
    {
        _clockElapsed = TimeSpan.Zero;
        _clockRunning = false;
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

    private void OnSetSmoothing(ClientId client, long seq, SetSmoothing command)
    {
        var value = command.Value;
        if (value.ManualCrossfade < TimeSpan.Zero || value.ManualCrossfade > SmoothingMax
            || value.AutoCrossfade < TimeSpan.Zero || value.AutoCrossfade > SmoothingMax
            || value.StartFade < TimeSpan.Zero || value.StartFade > SmoothingMax
            || value.StopFade < TimeSpan.Zero || value.StopFade > SmoothingMax)
        {
            Reject(client, seq, "smoothing-out-of-range");
            return;
        }
        _smoothing = value;
        _engine.SetSmoothing(value);
        EmitMixer();
    }

    private void OnMonitorPosition(PositionSnapshot snapshot)
    {
        if (_marshal is { } marshal)
        {
            marshal(() => CheckPreRoll(snapshot));
            return;
        }
        CheckPreRoll(snapshot);
    }

    private void CheckPreRoll(PositionSnapshot snapshot)
    {
        if (_preRolled || _status != TransportStatus.Playing || _current is not { Handle: { } } current)
        {
            return;
        }
        if (!Content(current).Equals(snapshot.Deck))
        {
            return;
        }
        if (!_smoothing.Enabled || _smoothing.AutoCrossfade <= TimeSpan.Zero)
        {
            return;
        }
        if (current.Settings.EndAction != EndAction.Advance)
        {
            return;
        }
        if (snapshot.Remaining > _smoothing.AutoCrossfade || !HasNext())
        {
            return;
        }
        _preRolled = true;
        AdvanceWithCrossfade();
    }

    private bool HasNext()
    {
        if (_queue.Count > 0)
        {
            return true;
        }
        if (_activePlaylistId is { } playlistId)
        {
            var playlist = _playlists.FirstOrDefault(p => p.Id == playlistId);
            if (playlist is not null && _cursor < playlist.Entries.Length)
            {
                return true;
            }
        }
        return false;
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
                if (_current!.Settings.EndAction == EndAction.Advance
                    && _smoothing.Enabled
                    && _smoothing.AutoCrossfade > TimeSpan.Zero)
                {
                    AdvanceWithCrossfade();
                }
                else
                {
                    DisposeCurrentHandle();
                    ApplyEndAction();
                }
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
            SyncDigest();
        }
    }

    private void AdvanceWithCrossfade()
    {
        var old = _current!;
        var handle = old.Handle;
        old.Handle = null;
        AdvanceFromBoundary();
        if (handle is { } faded)
        {
            _engine.SetMix(faded, new MixParameters(old.Settings.GainDb, new FadeSpec(_smoothing.AutoCrossfade, old.Settings.Out.Curve, SilenceDb, StopWhenDone: true)));
            _retired.Add(faded);
            _monitor?.Unbind(faded);
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
            StartStreamFor(_current, auto: true);
            _status = TransportStatus.Playing;
            _atEndBoundary = false;
            _panicked = false;
            EmitQueue();
            EmitTransport();
            SyncDigest();
            return true;
        }

        if (_activePlaylistId is { } playlistId)
        {
            var playlist = _playlists.First(p => p.Id == playlistId);
            if (_cursor < playlist.Entries.Length)
            {
                StartPlaylistEntry(playlist, _cursor, auto: true);
                return true;
            }
        }
        return false;
    }

    private void StartPlaylistEntry(Playlist playlist, int index, bool auto)
    {
        var entry = playlist.Entries[index];
        var track = _trackMap[entry.TrackId];
        var settings = EffectiveSettings.Resolve(entry, track);
        _activePlaylistId = playlist.Id;
        _cursor = index + 1;
        _current = new DeckInstance { Entry = entry.Id, Track = track, Settings = settings };
        StartStreamFor(_current, auto);
        _status = TransportStatus.Playing;
        _atEndBoundary = false;
        _panicked = false;
        EmitShow();
        EmitTransport();
    }

    private void StartStreamFor(DeckInstance deck, bool auto)
    {
        _preRolled = false;
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

        var fadeIn = ResolveFadeIn(settings.In, auto);
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
        StartStreamFor(deck, auto: false);
        _status = TransportStatus.Playing;
        _atEndBoundary = false;
        _panicked = false;
        EmitTransport();
    }

    private Fade ResolveFadeIn(Fade trackFade, bool auto)
    {
        if (!_smoothing.Enabled)
        {
            return trackFade;
        }
        var duration = auto ? _smoothing.AutoCrossfade : _smoothing.StartFade;
        return duration > TimeSpan.Zero ? new Fade(duration, trackFade.Curve) : Fade.None;
    }

    private void ReleaseOld(DeckInstance? old, bool wasPlaying, bool manual)
    {
        if (old?.Handle is not { } handle)
        {
            return;
        }
        var fadeOut = wasPlaying ? old.Settings.Out : null;
        if (_smoothing.Enabled && manual && wasPlaying && _smoothing.ManualCrossfade > TimeSpan.Zero)
        {
            fadeOut = new Fade(_smoothing.ManualCrossfade, old.Settings.Out.Curve);
        }
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

    private TrackDigest BuildDigest()
    {
        var names = new Dictionary<TrackId, string>();
        void Add(TrackId id)
        {
            if (!names.ContainsKey(id) && _trackMap.TryGetValue(id, out var track))
            {
                names.Add(id, EffectiveSettings.ForTrack(track).DisplayName);
            }
        }
        foreach (var playlist in _playlists)
        {
            foreach (var entry in playlist.Entries)
            {
                Add(entry.TrackId);
            }
        }
        foreach (var item in _queue)
        {
            Add(item.TrackId);
        }
        if (_current is not null)
        {
            Add(_current.Track.Id);
        }
        foreach (var script in _scripts)
        {
            foreach (var line in script.Lines)
            {
                foreach (var mention in line.Mentions)
                {
                    Add(mention.Track);
                }
            }
        }
        return new TrackDigest([.. names
            .OrderBy(pair => pair.Key.Value)
            .Select(pair => new TrackDigestEntry(pair.Key, pair.Value))]);
    }

    private static bool DigestEquals(TrackDigest left, TrackDigest right)
    {
        if (left.Entries.Length != right.Entries.Length)
        {
            return false;
        }
        for (var i = 0; i < left.Entries.Length; i++)
        {
            if (!left.Entries[i].Equals(right.Entries[i]))
            {
                return false;
            }
        }
        return true;
    }

    private void EmitShow()
    {
        _emittedDigest = BuildDigest();
        Emit(new ShowDelta(++_showVersion, new ShowState(_playlists, _activePlaylistId, _locked, new ShowClockState(_clockElapsed, _clockRunning), _scripts, _emittedDigest)));
    }

    private void SyncDigest()
    {
        var fresh = BuildDigest();
        if (!DigestEquals(fresh, _emittedDigest))
        {
            EmitShow();
        }
    }

    private void EmitTransport() => Emit(new TransportDelta(++_transportVersion, BuildTransport()));

    private void EmitQueue() => Emit(new QueueDelta(++_queueVersion, new QueueState([.. _queue])));

    private void EmitMixer() => Emit(new MixerDelta(++_mixerVersion, new MixerState(_masterGainDb, _muted, _panicFade, _smoothing)));

    private sealed class DeckInstance
    {
        public required EntryId? Entry { get; init; }
        public required Track Track { get; init; }
        public required PlaybackSettings Settings { get; init; }
        public StreamHandle? Handle { get; set; }
    }
}
