namespace Aria.Core.Runtime;

using System.Collections.Immutable;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.State;

// Owns PlayerState: mutate only here, on control thread.
public sealed class ShowController : IShowHandler
{
    private const double MasterGainMinDb = -80.0;
    private const double MasterGainMaxDb = 12.0;
    private const double SilenceDb = -80.0;
    private const double PreviewGainMinDb = -80.0;
    private const double PreviewGainMaxDb = 12.0;
    private static readonly TimeSpan PanicFadeMax = TimeSpan.FromMilliseconds(2000);
    private static readonly TimeSpan SmoothingMax = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ClockTick = TimeSpan.FromSeconds(1);

    private readonly IAudioEngine _engine;
    private readonly PlaybackMonitor? _monitor;
    private readonly Action<Action>? _marshal;

    private ImmutableArray<Track> _tracks = [];
    private ImmutableArray<Project> _projects = [];
    private Dictionary<TrackId, Track> _trackMap = [];
    private Dictionary<EntryId, (Project Project, int Index)> _entryMap = [];
    private ProjectId? _activeProjectId;
    private int _cursor;

    private readonly List<QueueItem> _queue = [];

    private TransportStatus _status = TransportStatus.Stopped;
    private DeckInstance? _current;
    private readonly HashSet<StreamHandle> _retired = [];
    private readonly HashSet<TrackId> _faulted = [];
    private readonly Dictionary<TrackId, SourceOpenFault> _faultCauses = [];
    private readonly TimeSpan _openTimeout;
    private long _openSeq;
    private (long Seq, DeckInstance Deck)? _pendingOpen;
    private bool _atEndBoundary;
    private bool _panicked;

    private double _masterGainDb;
    private bool _muted;
    private bool _locked;
    private GlobalAudioSettings _globalAudio = GlobalAudioSettings.Default;
    private TimeSpan _clockElapsed;
    private bool _clockRunning;
    private TimeSpan _panicFade = TimeSpan.FromMilliseconds(100);
    private Smoothing _smoothing = Smoothing.Default;
    private EndAction _defaultEndAction = EndAction.Advance;
    private bool _preRolled;
    private ImmutableArray<Script> _scripts = [];
    private TrackDigest _emittedDigest = TrackDigest.Empty;

    private int _showVersion;
    private int _transportVersion;
    private int _queueVersion;
    private int _mixerVersion;

    public Action<StateEvent>? Emitted { get; set; }

    public ShowController(IAudioEngine engine, PlaybackMonitor? monitor = null, Action<Action>? marshalEngineEvents = null, TimeSpan? streamOpenTimeout = null)
    {
        _engine = engine;
        _monitor = monitor;
        _marshal = marshalEngineEvents;
        _openTimeout = streamOpenTimeout ?? TimeSpan.FromSeconds(15);
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
            case MarkMissing markMissing:
                OnMarkMissing(client, seq, markMissing);
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
            case PlayTrack playTrack:
                OnPlayTrack(client, seq, playTrack);
                break;
            case RemoveFromQueue removeFromQueue:
                OnRemoveFromQueue(client, seq, removeFromQueue);
                break;
            case ClearQueue:
                OnClearQueue();
                break;
            case CreateProject createProject:
                OnCreateProject(client, seq, createProject);
                break;
            case RenameProject renameProject:
                OnRenameProject(client, seq, renameProject);
                break;
            case DeleteProject deleteProject:
                OnDeleteProject(client, seq, deleteProject);
                break;
            case SetActiveProject setActiveProject:
                OnSetActiveProject(client, seq, setActiveProject);
                break;
            case AddEntry addEntry:
                OnAddEntry(client, seq, addEntry);
                break;
            case ImportProject importProject:
                OnImportProject(client, seq, importProject);
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
            case SetGlobalAudio setGlobalAudio:
                OnSetGlobalAudio(client, seq, setGlobalAudio);
                break;
            case SetTrackAudio setTrackAudio:
                OnSetTrackAudio(client, seq, setTrackAudio);
                break;
            case SetEntryAudio setEntryAudio:
                OnSetEntryAudio(client, seq, setEntryAudio);
                break;
            case StartPreviewTrack startPreviewTrack:
                OnStartPreviewTrack(client, seq, startPreviewTrack);
                break;
            case StopPreview:
                OnStopPreview();
                break;
            case SetPreviewGain setPreviewGain:
                OnSetPreviewGain(client, seq, setPreviewGain);
                break;
            case SetPreviewMuted setPreviewMuted:
                OnSetPreviewMuted(setPreviewMuted);
                break;
            case NormalizeTrack normalize:
                OnNormalizeTrack(client, seq, normalize);
                break;
            case NormalizeProject normalizeProject:
                OnNormalizeProject(client, seq, normalizeProject);
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
            case SetDefaultEndAction setDefaultEndAction:
                OnSetDefaultEndAction(client, seq, setDefaultEndAction);
                break;
            default:
                Reject(client, seq, "unknown-command");
                break;
        }
    }

    public TrackAudioSettings? TrackAudio(TrackId id) =>
        _trackMap.TryGetValue(id, out var track) ? track.Defaults.Audio : null;

    public ShowSnapshot Snapshot() => new(
        _showVersion,
        new ShowState(_projects, _activeProjectId, _locked, new ShowClockState(_clockElapsed, _clockRunning), _scripts, _emittedDigest, _defaultEndAction),
        _transportVersion,
        BuildTransport(),
        _queueVersion,
        new QueueState([.. _queue]),
        _mixerVersion,
        new MixerState(_masterGainDb, _muted, _panicFade, _smoothing, _globalAudio));

    private void OnLoadShow(ClientId client, long seq, LoadShow load)
    {
        if (_status != TransportStatus.Stopped)
        {
            Reject(client, seq, "show-load-requires-stopped");
            return;
        }
        if (load.Tracks.IsDefault || load.Projects.IsDefault)
        {
            Reject(client, seq, "show-data-required");
            return;
        }
        if (!ValidateShow(load.Tracks, load.Projects, []))
        {
            Reject(client, seq, "invalid-show");
            return;
        }
        if (load.Active is { } active && !_projects.Any(p => p.Id == active) && !load.Projects.Any(p => p.Id == active))
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
        _projects = load.Projects;
        _trackMap = load.Tracks.ToDictionary(t => t.Id);
        RebuildEntryMap();
        _activeProjectId = load.Active ?? (_projects.Length > 0 ? _projects[0].Id : null);
        _cursor = 0;
        _queue.Clear();
        _current = null;
        _atEndBoundary = false;
        _panicked = false;
        _clockElapsed = TimeSpan.Zero;
        _clockRunning = false;
        _scripts = MigrateScripts(load.Scripts, load.Projects, _activeProjectId);

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
        if (restore.Tracks.IsDefault || restore.Projects.IsDefault || restore.Queue.IsDefault)
        {
            Reject(client, seq, "show-data-required");
            return;
        }
        if (!ValidateShow(restore.Tracks, restore.Projects, restore.Queue))
        {
            Reject(client, seq, "invalid-show");
            return;
        }
        if (restore.Active is { } active && !restore.Projects.Any(p => p.Id == active))
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
        _projects = restore.Projects;
        _trackMap = restore.Tracks.ToDictionary(t => t.Id);
        RebuildEntryMap();
        _activeProjectId = restore.Active ?? (_projects.Length > 0 ? _projects[0].Id : null);
        _cursor = 0;
        _queue.Clear();
        _queue.AddRange(restore.Queue);
        _masterGainDb = restore.MasterGainDb;
        _muted = false;
        _panicFade = restore.PanicFade;
        _clockElapsed = TimeSpan.Zero;
        _clockRunning = false;
        _scripts = MigrateScripts(restore.Scripts, restore.Projects, _activeProjectId);
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
        var faultCleared = false;
        foreach (var track in merge.Tracks)
        {
            if (_trackMap.TryGetValue(track.Id, out var existing))
            {
                if (!existing.Equals(track))
                {
                    _tracks = _tracks.Replace(existing, track);
                    _trackMap[track.Id] = track;
                    faultCleared |= _faulted.Remove(track.Id);
                    _faultCauses.Remove(track.Id);
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
        if (faultCleared)
        {
            EmitTransport();
        }
    }

    private void OnMarkMissing(ClientId client, long seq, MarkMissing markMissing)
    {
        if (markMissing.Tracks.IsDefault || markMissing.Tracks.Length == 0)
        {
            Reject(client, seq, "show-data-required");
            return;
        }
        var added = false;
        foreach (var id in markMissing.Tracks)
        {
            if (_trackMap.ContainsKey(id))
            {
                added |= _faulted.Add(id);
            }
        }
        if (added)
        {
            EmitTransport();
        }
    }

    private static bool ValidateShow(ImmutableArray<Track> tracks, ImmutableArray<Project> projects, ImmutableArray<QueueItem> queue)
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
        foreach (var project in projects)
        {
            foreach (var entry in project.Entries)
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
        foreach (var project in _projects)
        {
            for (var i = 0; i < project.Entries.Length; i++)
            {
                _entryMap[project.Entries[i].Id] = (project, i);
            }
        }
    }

    private int IndexOfProject(ProjectId id)
    {
        for (var i = 0; i < _projects.Length; i++)
        {
            if (_projects[i].Id == id)
            {
                return i;
            }
        }
        return -1;
    }

    private bool RequireName(string name, ClientId client, long seq)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            Reject(client, seq, "bad-name");
            return false;
        }
        return true;
    }

    private bool RequireProject(ProjectId id, ClientId client, long seq, out int index)
    {
        index = IndexOfProject(id);
        if (index < 0)
        {
            Reject(client, seq, "unknown-playlist");
            return false;
        }
        return true;
    }

    private bool RequireScript(ScriptId id, ClientId client, long seq, out int index)
    {
        index = IndexOfScript(id);
        if (index < 0)
        {
            Reject(client, seq, "unknown-script");
            return false;
        }
        return true;
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
        _engine.StopPreview();
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
        if (_status == TransportStatus.Playing && _smoothing.Enabled && _smoothing.SeekFade > TimeSpan.Zero)
        {
            SeekWithCrossfade(deck, filePosition);
            return;
        }
        _engine.Seek(handle, filePosition - settings.CueIn);
        _preRolled = false;
    }

    private void SeekWithCrossfade(DeckInstance deck, TimeSpan filePosition)
    {
        // New stream, not in-place seek: overlap fades, no click.
        var settings = deck.Settings;
        var old = deck.Handle!.Value;
        _engine.SetMix(old, new MixParameters(settings.GainDb, new FadeSpec(_smoothing.SeekFade, settings.Out.Curve, SilenceDb, StopWhenDone: true)));
        _retired.Add(old);
        _monitor?.Unbind(old);
        var source = new TrackSource(deck.Track.FilePath, filePosition, settings.CueOut, settings.Audio);
        var options = new StreamOptions(
            StreamBus.Main,
            settings.Markers.Select(m => new MarkerSpec(m.Name, m.Position, m.Action)).ToImmutableArray());
        var handle = _engine.StartStream(source, options);
        deck.Handle = handle;
        deck.Settings = settings with { CueIn = filePosition };
        _monitor?.Bind(handle, Content(deck));
        _engine.SetMix(handle, new MixParameters(settings.GainDb, new FadeSpec(_smoothing.SeekFade, settings.In.Curve, settings.GainDb, StopWhenDone: false)));
        _engine.Transport(handle, TransportCommand.Play);
        _preRolled = false;
    }

    private void OnPanic()
    {
        if (_status == TransportStatus.Panicked)
        {
            return;
        }
        _engine.Panic(new PanicSpec(_panicFade));
        _engine.StopPreview();
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
        StartProjectEntry(location.Project, location.Index, auto: false);
        ReleaseOld(old, wasPlaying, manual: true);
    }

    private void OnEnqueueEntry(ClientId client, long seq, EnqueueEntry command)
    {
        if (!_entryMap.TryGetValue(command.Entry, out var location))
        {
            Reject(client, seq, "unknown-entry");
            return;
        }
        var entry = location.Project.Entries[location.Index];
        if (!_trackMap.TryGetValue(entry.TrackId, out var track))
        {
            Reject(client, seq, "unknown-track");
            return;
        }
        var settings = EffectiveSettings.Resolve(entry, track, _defaultEndAction);
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
        var settings = EffectiveSettings.ForTrack(track, _defaultEndAction);
        _queue.Add(new QueueItem(null, track.Id, settings.DisplayName, settings.Color));
        EmitQueue();
        EmitTransport();
        SyncDigest();
    }

    private void OnPlayTrack(ClientId client, long seq, PlayTrack command)
    {
        if (_panicked)
        {
            Reject(client, seq, "panicked");
            return;
        }
        if (!_trackMap.TryGetValue(command.Track, out var track))
        {
            Reject(client, seq, "unknown-track");
            return;
        }
        if (!FindActiveEntry(command.Track, out var project, out var index))
        {
            Reject(client, seq, "track-not-in-playlist");
            return;
        }
        var settings = EffectiveSettings.ForTrack(track, _defaultEndAction);
        _queue.Insert(0, new QueueItem(null, track.Id, settings.DisplayName, settings.Color));
        var wasPlaying = _status == TransportStatus.Playing;
        var old = _current;
        if (!StartFromOrder())
        {
            Reject(client, seq, "nothing-to-play");
            return;
        }
        _activeProjectId = project.Id;
        _cursor = index + 1;
        ReleaseOld(old, wasPlaying, manual: true);
    }

    private bool FindActiveEntry(TrackId track, out Project project, out int index)
    {
        project = null!;
        index = -1;
        if (_activeProjectId is not { } projectId)
        {
            return false;
        }
        var found = _projects.FirstOrDefault(p => p.Id == projectId);
        if (found is null)
        {
            return false;
        }
        for (var i = 0; i < found.Entries.Length; i++)
        {
            if (found.Entries[i].TrackId == track)
            {
                project = found;
                index = i;
                return true;
            }
        }
        return false;
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

    private void OnCreateProject(ClientId client, long seq, CreateProject command)
    {
        if (!RequireName(command.Name, client, seq))
        {
            return;
        }
        _projects = _projects.Add(new Project(ProjectId.New(), command.Name, []));
        RebuildEntryMap();
        EmitShow();
        EmitTransport();
    }

    private void OnRenameProject(ClientId client, long seq, RenameProject command)
    {
        if (!RequireName(command.Name, client, seq))
        {
            return;
        }
        if (!RequireProject(command.Id, client, seq, out var index))
        {
            return;
        }
        _projects = _projects.SetItem(index, _projects[index] with { Name = command.Name });
        RebuildEntryMap();
        EmitShow();
    }

    private void OnDeleteProject(ClientId client, long seq, DeleteProject command)
    {
        if (!RequireProject(command.Id, client, seq, out var index))
        {
            return;
        }
        var removed = _projects[index];
        _projects = _projects.RemoveAt(index);
        _scripts = _scripts.RemoveAll(s => s.Project == command.Id);
        if (_activeProjectId == command.Id)
        {
            _activeProjectId = null;
            _cursor = 0;
        }
        var gone = removed.Entries.Select(entry => entry.Id).ToHashSet();
        if (_current?.Entry is { } currentEntry && gone.Contains(currentEntry))
        {
            DropCurrentPlayback();
        }
        var queueChanged = _queue.RemoveAll(item => item.EntryId is { } entryId && gone.Contains(entryId)) > 0;
        RebuildEntryMap();
        EmitShow();
        if (queueChanged)
        {
            EmitQueue();
        }
        EmitTransport();
    }

    private void OnSetActiveProject(ClientId client, long seq, SetActiveProject command)
    {
        if (_panicked)
        {
            Reject(client, seq, "panicked");
            return;
        }
        if (!RequireProject(command.Id, client, seq, out var index))
        {
            return;
        }
        _activeProjectId = _projects[index].Id;
        _cursor = 0;
        EmitShow();
        EmitTransport();
    }

    private void OnAddEntry(ClientId client, long seq, AddEntry command)
    {
        if (!RequireProject(command.Project, client, seq, out var index))
        {
            return;
        }
        if (!_trackMap.ContainsKey(command.Track))
        {
            Reject(client, seq, "unknown-track");
            return;
        }
        var project = _projects[index];
        if (command.Index is { } position && (position < 0 || position > project.Entries.Length))
        {
            Reject(client, seq, "bad-index");
            return;
        }
        var entry = new ProjectEntry(EntryId.New(), command.Track);
        var entries = command.Index is { } at ? project.Entries.Insert(at, entry) : project.Entries.Add(entry);
        _projects = _projects.SetItem(index, project with { Entries = entries });
        RebuildEntryMap();
        EmitShow();
        EmitTransport();
    }

    private void OnImportProject(ClientId client, long seq, ImportProject command)
    {
        if (!RequireName(command.Name, client, seq))
        {
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
            new ProjectEntry(EntryId.New(), imported.Track, imported.Overrides));
        _projects = _projects.Add(new Project(ProjectId.New(), command.Name, [.. entries]));
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
        var project = location.Project;
        _projects = _projects.SetItem(IndexOfProject(project.Id), project with { Entries = project.Entries.RemoveAt(location.Index) });
        if (_current?.Entry == command.Entry)
        {
            DropCurrentPlayback();
        }
        var queueChanged = _queue.RemoveAll(item => item.EntryId == command.Entry) > 0;
        RebuildEntryMap();
        EmitShow();
        if (queueChanged)
        {
            EmitQueue();
        }
        EmitTransport();
    }

    private void OnMoveEntry(ClientId client, long seq, MoveEntry command)
    {
        if (!_entryMap.TryGetValue(command.Entry, out var location))
        {
            Reject(client, seq, "unknown-entry");
            return;
        }
        var project = location.Project;
        if (command.NewIndex < 0 || command.NewIndex > project.Entries.Length - 1)
        {
            Reject(client, seq, "bad-index");
            return;
        }
        var entry = project.Entries[location.Index];
        var entries = project.Entries.RemoveAt(location.Index).Insert(command.NewIndex, entry);
        _projects = _projects.SetItem(IndexOfProject(project.Id), project with { Entries = entries });
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
        var project = location.Project;
        var entry = project.Entries[location.Index];
        var entries = project.Entries.SetItem(location.Index, entry with { Overrides = command.Overrides });
        _projects = _projects.SetItem(IndexOfProject(project.Id), project with { Entries = entries });
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

    private void OnSetGlobalAudio(ClientId client, long seq, SetGlobalAudio command)
    {
        if (AudioValidation.ValidateGlobal(command.Value) is { } reason)
        {
            Reject(client, seq, reason);
            return;
        }
        _globalAudio = command.Value;
        _engine.SetGlobalAudio(command.Value);
        PushCurrentAudio();
        EmitMixer();
    }

    private void OnStartPreviewTrack(ClientId client, long seq, StartPreviewTrack command)
    {
        if (_panicked)
        {
            Reject(client, seq, "panicked");
            return;
        }
        if (!_trackMap.TryGetValue(command.Track, out var track))
        {
            Reject(client, seq, "unknown-track");
            return;
        }
        var settings = EffectiveSettings.ForTrack(track, _defaultEndAction);
        var source = new TrackSource(track.FilePath, TimeSpan.Zero, null, settings.Audio);
        var options = new StreamOptions(
            StreamBus.Preview,
            settings.Markers.Select(m => new MarkerSpec(m.Name, m.Position, m.Action)).ToImmutableArray());
        _engine.StartPreview(source, options);
    }

    private void OnStopPreview() => _engine.StopPreview();

    private void OnSetPreviewGain(ClientId client, long seq, SetPreviewGain command)
    {
        if (command.GainDb is < PreviewGainMinDb or > PreviewGainMaxDb)
        {
            Reject(client, seq, "gain-out-of-range");
            return;
        }
        _engine.SetPreviewGain(command.GainDb);
    }

    private void OnSetPreviewMuted(SetPreviewMuted command) => _engine.SetPreviewMuted(command.Muted);

    private void OnSetTrackAudio(ClientId client, long seq, SetTrackAudio command)
    {
        if (AudioValidation.ValidateTrack(command.Audio) is { } reason)
        {
            Reject(client, seq, reason);
            return;
        }
        if (!_trackMap.TryGetValue(command.Track, out var existing))
        {
            Reject(client, seq, "unknown-track");
            return;
        }
        var updated = existing with { Defaults = existing.Defaults with { Audio = command.Audio } };
        _tracks = _tracks.Replace(existing, updated);
        _trackMap[command.Track] = updated;
        if (_current?.Track.Id == command.Track)
        {
            PushCurrentAudio();
        }
        EmitShow();
    }

    private void OnNormalizeTrack(ClientId client, long seq, NormalizeTrack command)
    {
        if (!_globalAudio.NormalizeEnabled)
        {
            Reject(client, seq, "normalize-disabled");
            return;
        }
        if (!_trackMap.TryGetValue(command.Track, out var existing))
        {
            Reject(client, seq, "unknown-track");
            return;
        }
        var measured = _engine.ScanTrackLufs(existing.FilePath);
        if (double.IsNaN(measured))
        {
            Reject(client, seq, "lufs-scan-failed");
            return;
        }
        var current = existing.Defaults.Audio ?? TrackAudioSettings.Default;
        var adjusted = current with { MeasuredLufs = measured, NormalizeEnabled = true };
        if (AudioValidation.ValidateTrack(adjusted) is { } reason)
        {
            Reject(client, seq, reason);
            return;
        }
        var updated = existing with { Defaults = existing.Defaults with { Audio = adjusted } };
        _tracks = _tracks.Replace(existing, updated);
        _trackMap[command.Track] = updated;
        if (_current?.Track.Id == command.Track)
        {
            PushCurrentAudio();
        }
        EmitShow();
    }

    private void OnNormalizeProject(ClientId client, long seq, NormalizeProject command)
    {
        if (!_globalAudio.NormalizeEnabled)
        {
            Reject(client, seq, "normalize-disabled");
            return;
        }
        var index = IndexOfProject(command.Project);
        if (index < 0)
        {
            Reject(client, seq, "unknown-playlist");
            return;
        }
        var ids = _projects[index].Entries.Select(e => e.TrackId).Distinct().ToArray();
        if (ids.Length == 0)
        {
            Reject(client, seq, "nothing-to-normalize");
            return;
        }
        var changed = false;
        foreach (var id in ids)
        {
            if (!_trackMap.TryGetValue(id, out var existing))
            {
                continue;
            }
            var measured = _engine.ScanTrackLufs(existing.FilePath);
            if (double.IsNaN(measured))
            {
                continue;
            }
            var audio = existing.Defaults.Audio ?? TrackAudioSettings.Default;
            var adjusted = audio with { MeasuredLufs = measured, NormalizeEnabled = true };
            if (AudioValidation.ValidateTrack(adjusted) is not null)
            {
                continue;
            }
            var updated = existing with { Defaults = existing.Defaults with { Audio = adjusted } };
            _tracks = _tracks.Replace(existing, updated);
            _trackMap[id] = updated;
            changed = true;
        }
        if (!changed)
        {
            Reject(client, seq, "lufs-scan-failed");
            return;
        }
        if (_current is not null && ids.Contains(_current.Track.Id))
        {
            PushCurrentAudio();
        }
        EmitShow();
    }

    private void PushCurrentAudio()
    {
        if (_current?.Handle is not { } handle)
        {
            return;
        }
        if (CurrentEffectiveAudio() is not { } audio)
        {
            return;
        }
        _engine.SetVoiceAudio(handle, audio);
    }

    private TrackAudioSettings? CurrentEffectiveAudio()
    {
        if (_current is null)
        {
            return null;
        }
        if (!_trackMap.TryGetValue(_current.Track.Id, out var track))
        {
            return null;
        }
        if (_current.Entry is { } entryId && _entryMap.TryGetValue(entryId, out var location))
        {
            var entry = location.Project.Entries[location.Index];
            return entry.Overrides?.Audio ?? track.Defaults.Audio;
        }
        return track.Defaults.Audio;
    }

    private void OnSetEntryAudio(ClientId client, long seq, SetEntryAudio command)
    {
        if (command.Audio is { } audio && AudioValidation.ValidateTrack(audio) is { } reason)
        {
            Reject(client, seq, reason);
            return;
        }
        if (!_entryMap.TryGetValue(command.Entry, out var location))
        {
            Reject(client, seq, "unknown-entry");
            return;
        }
        var project = location.Project;
        var entry = project.Entries[location.Index];
        var overrides = (entry.Overrides ?? new ProjectOverrides()) with { Audio = command.Audio };
        var entries = project.Entries.SetItem(location.Index, entry with { Overrides = overrides });
        _projects = _projects.SetItem(IndexOfProject(project.Id), project with { Entries = entries });
        RebuildEntryMap();
        if (_current?.Entry is { } currentEntry && currentEntry == command.Entry)
        {
            PushCurrentAudio();
        }
        EmitShow();
    }

    private static ImmutableArray<Script> MigrateScripts(ImmutableArray<Script> scripts, ImmutableArray<Project> projects, ProjectId? active)
    {
        if (scripts.IsDefault)
        {
            return [];
        }
        var known = projects.Select(p => p.Id).ToHashSet();
        return [.. scripts.Select(s => s.Project is null || !known.Contains(s.Project.Value) ? s with { Project = active } : s)];
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
        if (!RequireName(command.Name, client, seq))
        {
            return;
        }
        _scripts = _scripts.Add(new Script(ScriptId.New(), command.Name, [], command.Project ?? _activeProjectId));
        EmitShow();
    }

    private void OnRenameScript(ClientId client, long seq, RenameScript command)
    {
        if (!RequireName(command.Name, client, seq))
        {
            return;
        }
        if (!RequireScript(command.Id, client, seq, out var index))
        {
            return;
        }
        _scripts = _scripts.SetItem(index, _scripts[index] with { Name = command.Name });
        EmitShow();
    }

    private void OnDeleteScript(ClientId client, long seq, DeleteScript command)
    {
        if (!RequireScript(command.Id, client, seq, out var index))
        {
            return;
        }
        _scripts = _scripts.RemoveAt(index);
        EmitShow();
    }

    private static ImmutableArray<Mention> ToMentions(ImmutableArray<TrackId> tracks) =>
        tracks.IsDefault ? [] : [.. tracks.Select(m => new Mention(m))];

    private void OnAddScriptLine(ClientId client, long seq, AddScriptLine command)
    {
        if (!RequireScript(command.Script, client, seq, out var index))
        {
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
        if (!RequireScript(command.Script, client, seq, out var index))
        {
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
        if (!RequireScript(command.Script, client, seq, out var index))
        {
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
        if (!RequireScript(command.Script, client, seq, out var index))
        {
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
            || value.StopFade < TimeSpan.Zero || value.StopFade > SmoothingMax
            || value.SeekFade < TimeSpan.Zero || value.SeekFade > SmoothingMax)
        {
            Reject(client, seq, "smoothing-out-of-range");
            return;
        }
        _smoothing = value;
        _engine.SetSmoothing(value);
        EmitMixer();
    }

    private void OnSetDefaultEndAction(ClientId client, long seq, SetDefaultEndAction command)
    {
        if (!Enum.IsDefined(command.Action))
        {
            Reject(client, seq, "default-end-action-unknown");
            return;
        }
        _defaultEndAction = command.Action;
        EmitShow();
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
        if (snapshot.Remaining > _smoothing.ManualCrossfade || !HasNext())
        {
            return;
        }
        var wasPlaying = _status == TransportStatus.Playing;
        var old = _current;
        if (!StartFromOrder(_smoothing.ManualCrossfade))
        {
            return;
        }
        _preRolled = true;
        ReleaseOld(old, wasPlaying, manual: true);
    }

    private bool HasNext()
    {
        if (_queue.Count > 0)
        {
            return true;
        }
        if (_activeProjectId is { } projectId)
        {
            var project = _projects.FirstOrDefault(p => p.Id == projectId);
            if (project is not null && _cursor < project.Entries.Length)
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
            _engine.DisposeStream(e.Handle);
            _monitor?.Unbind(e.Handle);
        }
    }

    private void HandleCurrentEvent(StreamEvent e)
    {
        if (e.Kind == StreamEventKind.Faulted)
        {
            FaultCurrent(e.Detail);
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
        if (_queue.Count > 0)
        {
            AdvanceFromBoundary();
            return;
        }
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

    private void AdvanceFromBoundary(TimeSpan? lead = null)
    {
        if (!StartFromOrder(lead))
        {
            _status = TransportStatus.Stopped;
            _atEndBoundary = false;
            _current = null;
            EmitTransport();
            SyncDigest();
        }
    }

    private void AdvanceWithCrossfade(TimeSpan? lead = null)
    {
        var old = _current!;
        var handle = old.Handle;
        old.Handle = null;
        AdvanceFromBoundary(lead);
        if (handle is { } faded)
        {
            _engine.SetMix(faded, new MixParameters(old.Settings.GainDb, new FadeSpec(lead ?? _smoothing.AutoCrossfade, _smoothing.Enabled ? FadeCurve.Exponential : old.Settings.Out.Curve, SilenceDb, StopWhenDone: true)));
            _retired.Add(faded);
            _monitor?.Unbind(faded);
        }
    }

    private bool StartFromOrder(TimeSpan? lead = null)
    {
        if (_queue.Count > 0)
        {
            var item = _queue[0];
            _queue.RemoveAt(0);
            var track = _trackMap[item.TrackId];
            var settings = item.EntryId is { } entryId && _entryMap.TryGetValue(entryId, out var location)
                ? EffectiveSettings.Resolve(location.Project.Entries[location.Index], track, _defaultEndAction)
                : EffectiveSettings.ForTrack(track, _defaultEndAction);
            _current = new DeckInstance { Entry = item.EntryId, Track = track, Settings = settings };
            StartStreamFor(_current, auto: true, lead);
            _status = TransportStatus.Playing;
            _atEndBoundary = false;
            _panicked = false;
            EmitQueue();
            EmitTransport();
            SyncDigest();
            return true;
        }

        if (_activeProjectId is { } projectId)
        {
            var project = _projects.First(p => p.Id == projectId);
            if (_cursor < project.Entries.Length)
            {
                StartProjectEntry(project, _cursor, auto: true, lead);
                return true;
            }
        }
        return false;
    }

    private void StartProjectEntry(Project project, int index, bool auto, TimeSpan? lead = null)
    {
        var entry = project.Entries[index];
        var track = _trackMap[entry.TrackId];
        var settings = EffectiveSettings.Resolve(entry, track, _defaultEndAction);
        _activeProjectId = project.Id;
        _cursor = index + 1;
        _current = new DeckInstance { Entry = entry.Id, Track = track, Settings = settings };
        StartStreamFor(_current, auto, lead);
        _status = TransportStatus.Playing;
        _atEndBoundary = false;
        _panicked = false;
        EmitShow();
        EmitTransport();
    }

    private void FaultCurrent(string? detail)
    {
        if (_current is { } failed)
        {
            _faulted.Add(failed.Track.Id);
            _faultCauses[failed.Track.Id] = ParseFaultCause(detail);
        }
        DisposeCurrentHandle();
        _status = TransportStatus.Stopped;
        _current = null;
        EmitTransport();
    }

    private void StartStreamFor(DeckInstance deck, bool auto, TimeSpan? lead = null)
    {
        _preRolled = false;
        if (deck.Handle is { } previous)
        {
            _monitor?.Unbind(previous);
        }
        _faulted.Remove(deck.Track.Id);
        _faultCauses.Remove(deck.Track.Id);
        var settings = deck.Settings;
        var source = new TrackSource(deck.Track.FilePath, settings.CueIn, settings.CueOut, settings.Audio);
        var options = new StreamOptions(
            StreamBus.Main,
            settings.Markers.Select(m => new MarkerSpec(m.Name, m.Position, m.Action)).ToImmutableArray());
        var engine = _engine;
        if (_marshal is not { } marshal)
        {
            FinishOpen(deck, engine.StartStream(source, options), auto, lead);
            return;
        }
        var seq = ++_openSeq;
        _pendingOpen = (seq, deck);
        _ = Task.Run(() => engine.StartStream(source, options)).ContinueWith(
            task => marshal(() => CompleteOpen(seq, deck, auto, task, lead)),
            TaskScheduler.Default);
        _ = Task.Delay(_openTimeout).ContinueWith(
            _ => marshal(() => OpenExpired(seq, deck)),
            TaskScheduler.Default);
    }

    private void FinishOpen(DeckInstance deck, StreamHandle handle, bool auto, TimeSpan? lead = null)
    {
        deck.Handle = handle;
        _monitor?.Bind(handle, Content(deck));
        var settings = deck.Settings;
        var fadeIn = ResolveFadeIn(settings.In, auto, lead);
        var mix = fadeIn.Duration > TimeSpan.Zero
            ? new MixParameters(settings.GainDb, new FadeSpec(fadeIn.Duration, fadeIn.Curve, settings.GainDb, StopWhenDone: false))
            : new MixParameters(settings.GainDb, null);
        _engine.Transport(handle, TransportCommand.Play);
        _engine.SetMix(handle, mix);
    }

    private void CompleteOpen(long seq, DeckInstance deck, bool auto, Task<StreamHandle> task, TimeSpan? lead = null)
    {
        if (_pendingOpen is not { } pending || pending.Seq != seq || !ReferenceEquals(_current, deck))
        {
            if (task.Status == TaskStatus.RanToCompletion)
            {
                _engine.DisposeStream(task.Result);
            }
            else
            {
                _ = task.Exception;
            }
            return;
        }
        _pendingOpen = null;
        if (task.Status != TaskStatus.RanToCompletion)
        {
            FaultCurrent(SourceOpenFault.Unknown.ToString());
            return;
        }
        FinishOpen(deck, task.Result, auto, lead);
    }

    private void OpenExpired(long seq, DeckInstance deck)
    {
        if (_pendingOpen is not { } pending || pending.Seq != seq || !ReferenceEquals(_current, deck))
        {
            return;
        }
        _pendingOpen = null;
        FaultCurrent(SourceOpenFault.Unknown.ToString());
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

    private Fade ResolveFadeIn(Fade trackFade, bool auto, TimeSpan? lead = null)
    {
        if (!_smoothing.Enabled)
        {
            return trackFade;
        }
        var duration = auto ? (lead ?? _smoothing.AutoCrossfade) : _smoothing.StartFade;
        var curve = auto ? FadeCurve.Logarithmic : trackFade.Curve;
        return duration > TimeSpan.Zero ? new Fade(duration, curve) : Fade.None;
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
            fadeOut = new Fade(_smoothing.ManualCrossfade, FadeCurve.Exponential);
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

    private void DropCurrentPlayback()
    {
        if (_current is { Handle: { } handle })
        {
            _engine.Transport(handle, TransportCommand.Stop);
            _engine.DisposeStream(handle);
            _retired.Remove(handle);
            _monitor?.Unbind(handle);
        }
        _current = null;
        _status = TransportStatus.Stopped;
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
        return new TransportState(_status, current, PeekNext(), [.. _faulted], [.. _faulted.Select(id => new FaultCause(id, _faultCauses.GetValueOrDefault(id, SourceOpenFault.Unknown)))]);
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
                ? EffectiveSettings.Resolve(location.Project.Entries[location.Index], track, _defaultEndAction)
                : EffectiveSettings.ForTrack(track, _defaultEndAction);
            return new DeckContent(item.EntryId, track.Id, settings.DisplayName, settings.Color, settings.EndAction, track.Duration, settings.CueIn, settings.CueOut);
        }

        if (_activeProjectId is { } projectId)
        {
            var project = _projects.FirstOrDefault(p => p.Id == projectId);
            if (project is not null && _cursor < project.Entries.Length)
            {
                var entry = project.Entries[_cursor];
                var track = _trackMap[entry.TrackId];
                var settings = EffectiveSettings.Resolve(entry, track, _defaultEndAction);
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
                names.Add(id, EffectiveSettings.ForTrack(track, _defaultEndAction).DisplayName);
            }
        }
        foreach (var project in _projects)
        {
            foreach (var entry in project.Entries)
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
        Emit(new ShowDelta(++_showVersion, new ShowState(_projects, _activeProjectId, _locked, new ShowClockState(_clockElapsed, _clockRunning), _scripts, _emittedDigest, _defaultEndAction)));
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

    private static SourceOpenFault ParseFaultCause(string? detail) =>
        Enum.TryParse<SourceOpenFault>(detail, out var cause) ? cause : SourceOpenFault.Unknown;

    private void EmitQueue() => Emit(new QueueDelta(++_queueVersion, new QueueState([.. _queue])));

    private void EmitMixer() => Emit(new MixerDelta(++_mixerVersion, new MixerState(_masterGainDb, _muted, _panicFade, _smoothing, _globalAudio)));

    private sealed class DeckInstance
    {
        public required EntryId? Entry { get; init; }
        public required Track Track { get; init; }
        public required PlaybackSettings Settings { get; set; }
        public StreamHandle? Handle { get; set; }
    }
}
