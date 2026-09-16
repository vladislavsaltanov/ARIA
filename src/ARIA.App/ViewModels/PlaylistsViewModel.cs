namespace Aria.App.ViewModels;

using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Text;
using Aria.App.Services;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.Runtime;
using Aria.Core.State;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public sealed partial class PlaylistsViewModel : ObservableObject, IDisposable
{
    private readonly ICommandBus _bus;
    private readonly ClientId _client = new("desktop-playlists");
    private readonly Func<ImmutableArray<Track>>? _trackSource;
    private readonly WaveformThumbs? _thumbs;
    private readonly Func<TopLevel?>? _topLevel;
    private readonly Func<Task<IReadOnlyList<string>>>? _folderPicker;
    private Func<IReadOnlyList<string>, IProgress<string>?, Task<ImportReport>>? _audioImport;
    private readonly Func<TrackId, TrackAudioSettings?>? _trackAudio;
    private readonly SynchronizationContext? _sync;
    private readonly HashSet<TrackId> _faulted = [];
    private readonly Dictionary<TrackId, SourceOpenFault> _faultCauses = [];
    private string? _awaitedPlaylistName;
    private readonly IDisposable _subscription;
    private ShowState? _lastShow;
    private TrackId? _linkedTrackId;
    private AppSettings _rowSettings = AppSettings.Default;
    private const int MaxListedImportErrors = 30;
    private long _seq;
    private string? _lastKey;
    private readonly TimeSpan _transientStatusTtl;
    private CancellationTokenSource? _statusClear;

    [ObservableProperty]
    private PlaylistVm? selectedPlaylist;

    [ObservableProperty]
    private EntryVm? selectedEntry;

    [ObservableProperty]
    private string centerSearchText = string.Empty;

    [ObservableProperty]
    private string entryCountText = string.Empty;

    [ObservableProperty]
    private string playlistIoStatus = string.Empty;

    [ObservableProperty]
    private string lastImportError = string.Empty;

    public event Action<string>? ImportFailed;

    public event Action<string>? AudioImportIncomplete;

    public event Action<PlaylistImportReport>? PlaylistImportMissing;

    public event Action<string, string>? ExportSucceeded;

    public event Action<EntryVm>? RevealRequested;

    private PlaylistId? _revealPlaylist;

    private List<TrackId>? _revealTracks;

    public Func<IReadOnlyList<string>, IProgress<string>?, Task<ImportReport>>? AudioImport
    {
        get => _audioImport;
        set => _audioImport = value;
    }

    public Func<TrackId, string, Task<bool>>? TrackRelink { get; set; }

    public bool IsTrackMissing(EntryVm entry)
    {
        if (_faultCauses.TryGetValue(entry.TrackId, out var cause) && cause is not SourceOpenFault.Unknown)
        {
            return cause == SourceOpenFault.Missing;
        }
        var path = TrackPath(entry.TrackId);
        return path is null || !File.Exists(path);
    }

    public string DescribeFault(EntryVm entry)
    {
        var path = TrackPath(entry.TrackId);
        if (_faultCauses.TryGetValue(entry.TrackId, out var cause))
        {
            if (cause == SourceOpenFault.Missing)
            {
                return MissingMessage(entry, path);
            }
            if (cause == SourceOpenFault.Undecodable)
            {
                return DecodeMessage(entry, path);
            }
        }
        if (path is null || !File.Exists(path))
        {
            return MissingMessage(entry, path);
        }
        return DecodeMessage(entry, path);
    }

    private static string MissingMessage(EntryVm entry, string? path) =>
        path is null
            ? $"Файл не найден.\nТрек «{entry.DisplayName}» не загрузился — файл переместили, переименовали или удалили."
            : $"Файл не найден: {path}\nТрек «{entry.DisplayName}» не загрузился — файл переместили, переименовали или удалили.";

    private static string DecodeMessage(EntryVm entry, string? path) =>
        $"Не удалось декодировать «{entry.DisplayName}».\nФайл на месте ({path}), но движок не смог его открыть — возможно, он повреждён или формат не поддерживается.";

    public async Task<bool> RelinkEntryAsync(EntryVm entry, string newPath)
    {
        var relink = TrackRelink;
        if (relink is null)
        {
            return false;
        }
        var ok = await relink(entry.TrackId, newPath);
        if (ok)
        {
            SetTransientStatus($"трек заменён: {entry.DisplayName}");
        }
        return ok;
    }

    private string? TrackPath(TrackId id)
    {
        foreach (var track in _trackSource?.Invoke() ?? [])
        {
            if (track.Id == id)
            {
                return track.FilePath;
            }
        }
        return null;
    }

    public ObservableCollection<PlaylistVm> Playlists { get; } = [];

    public ObservableCollection<EntryVm> VisibleEntries { get; } = [];

    public PlaylistsViewModel(ICommandBus bus, Func<ImmutableArray<Track>>? trackSource = null, WaveformThumbs? thumbs = null, AppSettings? rowSettings = null, SynchronizationContext? sync = null, Func<TopLevel?>? topLevel = null, Func<IReadOnlyList<string>, IProgress<string>?, Task<ImportReport>>? audioImport = null, TimeSpan? transientStatusTtl = null, Func<TrackId, TrackAudioSettings?>? trackAudio = null, Func<Task<IReadOnlyList<string>>>? folderPicker = null)
    {
        _bus = bus;
        _trackSource = trackSource;
        _thumbs = thumbs;
        _rowSettings = rowSettings ?? AppSettings.Default;
        _transientStatusTtl = transientStatusTtl ?? TimeSpan.FromSeconds(10);
        _sync = sync;
        _topLevel = topLevel;
        _folderPicker = folderPicker;
        _audioImport = audioImport;
        _trackAudio = trackAudio;
        _subscription = bus.Subscribe(Apply);
        var snapshot = bus.Snapshot();
        foreach (var id in snapshot.Transport.Faulted)
        {
            _faulted.Add(id);
        }
        MirrorFaultCauses(snapshot.Transport);
        Rebuild(snapshot.Show, _trackSource?.Invoke() ?? []);
    }

    public void UpdateRowSettings(AppSettings settings)
    {
        _rowSettings = settings;
        if (_lastShow is { } show)
        {
            Rebuild(show, _trackSource?.Invoke() ?? []);
        }
    }

    [RelayCommand]
    private void CreatePlaylist()
    {
        _awaitedPlaylistName = $"Новый плейлист {++_newPlaylistCounter}";
        Submit(new CreatePlaylist(_awaitedPlaylistName));
    }

    [RelayCommand]
    private void RenamePlaylist(string? name)
    {
        if (SelectedPlaylist is not { } playlist || string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        Submit(new RenamePlaylist(playlist.Id, name));
    }

    [RelayCommand]
    private void DeletePlaylist()
    {
        if (SelectedPlaylist is not { } playlist)
        {
            return;
        }
        Submit(new DeletePlaylist(playlist.Id));
    }

    [RelayCommand]
    private void ActivatePlaylist()
    {
        if (SelectedPlaylist is not { } playlist)
        {
            return;
        }
        Submit(new SetActivePlaylist(playlist.Id));
    }

    [RelayCommand]
    private void RemoveEntry()
    {
        if (SelectedEntry is not { } entry)
        {
            return;
        }
        RemoveEntryAt(entry);
    }

    [RelayCommand]
    private async Task ExportPlaylistAsync()
    {
        var topLevel = _topLevel?.Invoke();
        if (topLevel is null)
        {
            SetTransientStatus("экспорт недоступен");
            return;
        }
        if (SelectedPlaylist is null)
        {
            SetTransientStatus("нет плейлиста для экспорта");
            return;
        }
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Экспорт плейлиста",
            SuggestedFileName = SelectedPlaylist.Name + PlaylistFormat.FileExtension,
            FileTypeChoices =
            [
                new FilePickerFileType("ARIA-плейлист") { Patterns = [$"*{PlaylistFormat.FileExtension}"] },
            ],
        });
        if (file is null)
        {
            return;
        }
        await FinishExportAsync(() => file.OpenWriteAsync(), file.Name);
    }

    public async Task FinishExportAsync(Func<Task<Stream>> openWrite, string fileName)
    {
        await using var stream = await openWrite();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(ExportSelectedDocument());
        var count = SelectedPlaylist?.Entries.Count ?? 0;
        PlaylistIoStatus = string.Empty;
        ExportSucceeded?.Invoke(fileName, $"Сохранено: {fileName}\nТреков: {count}");
    }

    [RelayCommand]
    private async Task ImportPlaylistAsync()
    {
        var topLevel = _topLevel?.Invoke();
        if (topLevel is null)
        {
            SetTransientStatus("импорт недоступен");
            return;
        }
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Импорт плейлиста",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("ARIA-плейлист") { Patterns = [$"*{PlaylistFormat.FileExtension}", "*.json"] },
            ],
        });
        if (files.Count == 0)
        {
            return;
        }
        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream);
        await ImportDocumentAsync(await reader.ReadToEndAsync());
    }

    [RelayCommand]
    private async Task ImportAudio()
    {
        var topLevel = _topLevel?.Invoke();
        if (topLevel is null)
        {
            SetTransientStatus("импорт недоступен");
            return;
        }
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Импорт аудио в плейлист",
            AllowMultiple = true,
            FileTypeFilter =
            [
                AudioFileTypes.Filter,
            ],
        });
        if (files.Count == 0)
        {
            return;
        }
        await ImportAudioFilesAsync(files.Select(file => file.Path.LocalPath));
    }

    [RelayCommand]
    private async Task ImportAudioFolder()
    {
        var folders = await (_folderPicker ?? PickAudioFolderAsync)();
        if (folders.Count == 0)
        {
            return;
        }
        await ImportAudioFilesAsync(folders);
    }

    private async Task<IReadOnlyList<string>> PickAudioFolderAsync()
    {
        var topLevel = _topLevel?.Invoke();
        if (topLevel is null)
        {
            return [];
        }
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Импорт папки с аудио",
            AllowMultiple = true,
        });
        return [.. folders.Select(folder => folder.Path.LocalPath)];
    }

    public async Task<IReadOnlyList<TrackId>> ImportAudioFilesAsync(IEnumerable<string> paths, bool silent = false)
    {
        var inputs = paths.ToArray();
        if (inputs.Length == 0)
        {
            return [];
        }
        if (SelectedPlaylist is null)
        {
            SetTransientStatus("нет плейлиста для импорта");
            return [];
        }
        if (_audioImport is null)
        {
            SetTransientStatus("импорт недоступен");
            return [];
        }
        IProgress<string>? progress = silent ? null : new Progress<string>(name => PlaylistIoStatus = $"импорт: {name}");
        var report = await _audioImport(inputs, progress);
        var tracks = _trackSource?.Invoke() ?? [];
        var ordered = new List<TrackId>();
        foreach (var input in inputs)
        {
            foreach (var track in tracks
                .Where(t => MatchesInput(input, t.FilePath))
                .OrderBy(t => t.FilePath, StringComparer.Ordinal))
            {
                if (!ordered.Contains(track.Id))
                {
                    ordered.Add(track.Id);
                }
            }
        }
        var target = SelectedPlaylist;
        _revealPlaylist = ordered.Count > 0 ? target.Id : null;
        _revealTracks = ordered.Count > 0 ? ordered : null;
        foreach (var trackId in ordered)
        {
            Submit(new AddEntry(target.Id, trackId, null));
        }
        if (!silent)
        {
            SetTransientStatus($"импортировано: {report.Added}, пропущено: {report.Skipped}, ошибок: {report.Failed.Length}");
        }
        if (report.Failed.Length > 0)
        {
            var listed = string.Join("\n", report.Failed.Take(MaxListedImportErrors));
            if (report.Failed.Length > MaxListedImportErrors)
            {
                listed += $"\n…и ещё {report.Failed.Length - MaxListedImportErrors}";
            }
            AudioImportIncomplete?.Invoke("Не удалось распознать файлы:\n" + listed);
        }
        return ordered;
    }

    private static bool MatchesInput(string input, string trackPath)
    {
        if (trackPath.Equals(input, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        var root = input.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trackPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public string ExportSelectedDocument()
    {
        if (SelectedPlaylist is null)
        {
            throw new InvalidOperationException("Нет выбранного плейлиста");
        }
        var files = (_trackSource?.Invoke() ?? []).ToDictionary(t => t.Id, t => t.FilePath);
        var entries = SelectedPlaylist.Entries.Select(entry => new PlaylistExportEntry(
            files.GetValueOrDefault(entry.TrackId, entry.DisplayName),
            entry.Overrides));
        return PlaylistFormat.Export(SelectedPlaylist.Name, entries);
    }

    public Task<PlaylistImportReport> ImportDocumentAsync(string json)
    {
        PlaylistFileDocument document;
        try
        {
            document = PlaylistFormat.Import(json);
        }
        catch (PlaylistFormatException e)
        {
            LastImportError = e.Message;
            SetTransientStatus("импорт не удался");
            ImportFailed?.Invoke(e.Message);
            return Task.FromResult(new PlaylistImportReport(string.Empty, 0, [], 0, e.Message));
        }
        var tracks = _trackSource?.Invoke() ?? [];
        var byPath = tracks.ToDictionary(t => UnicodePaths.Key(t.FilePath), t => t.Id);
        var byName = tracks
            .GroupBy(t => UnicodePaths.Key(Path.GetFileName(t.FilePath)), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        var imports = new List<ImportPlaylistEntry>();
        var missing = new List<string>();
        var pendingTransitions = 0;
        foreach (var entry in document.Entries)
        {
            if (!byPath.TryGetValue(UnicodePaths.Key(entry.File), out var trackId)
                && !byName.TryGetValue(UnicodePaths.Key(Path.GetFileName(entry.File)), out trackId))
            {
                missing.Add(entry.File);
                continue;
            }
            if (entry.Transition is { Kind: var kind } && !kind.Equals("cut", StringComparison.OrdinalIgnoreCase))
            {
                pendingTransitions++;
            }
            imports.Add(new ImportPlaylistEntry(trackId, PlaylistFormat.ToOverrides(entry)));
        }
        if (imports.Count == 0)
        {
            var empty = new PlaylistImportReport(document.Name, 0, [.. missing], 0, "нет известных треков");
            if (empty.MissingFiles.Length > 0)
            {
                PlaylistImportMissing?.Invoke(empty);
            }
            return Task.FromResult(empty);
        }
        _awaitedPlaylistName = document.Name;
        Submit(new ImportPlaylist(document.Name, [.. imports]));
        LastImportError = string.Empty;
        var report = new PlaylistImportReport(document.Name, imports.Count, [.. missing], pendingTransitions);
        SetTransientStatus(Describe(report));
        if (report.MissingFiles.Length > 0)
        {
            PlaylistImportMissing?.Invoke(report);
        }
        return Task.FromResult(report);
    }

    private static string Describe(PlaylistImportReport report)
    {
        if (report.Error is not null)
        {
            return "импорт не удался";
        }
        var text = $"импортировано: {report.PlaylistName} ({report.Added})";
        if (report.MissingFiles.Length > 0)
        {
            text += $", пропущено файлов: {report.MissingFiles.Length}";
        }
        if (report.PendingTransitions > 0)
        {
            text += $", переходы встык: {report.PendingTransitions}";
        }
        return text;
    }

    public void RemoveEntryAt(EntryVm entry) => Submit(new RemoveEntry(entry.Id));

    public void SetEntryEndAction(EntryVm entry, EndAction? action)
    {
        PlaylistOverrides? merged;
        if (entry.Overrides is { } current)
        {
            var next = current with { EndAction = action };
            merged = next == new PlaylistOverrides() ? null : next;
        }
        else if (action is null)
        {
            return;
        }
        else
        {
            merged = new PlaylistOverrides(EndAction: action);
        }
        Submit(new SetEntryOverrides(entry.Id, merged));
    }

    public TrackAudioVm CreateEntryAudioEditor(EntryVm entry)
    {
        var global = _bus.Snapshot().Mixer.EffectiveGlobal;
        var initial = entry.Overrides?.Audio
            ?? _trackAudio?.Invoke(entry.TrackId)
            ?? _trackSource?.Invoke().FirstOrDefault(t => t.Id == entry.TrackId)?.Defaults.Audio;
        var editor = new TrackAudioVm(
            Submit,
            entry.TrackId,
            initial,
            entry.Id,
            global.NormalizeTargetLufs,
            global.NormalizeEnabled,
            () => _trackAudio?.Invoke(entry.TrackId)?.MeasuredLufs)
        {
            InheritTrackSettings = entry.Overrides?.Audio is null,
        };
        return editor;
    }

    public void DeletePlaylistAt(PlaylistVm playlist) => Submit(new DeletePlaylist(playlist.Id));

    public void MoveEntry(EntryId id, int newIndex)
    {
        if (SelectedPlaylist is not { } playlist)
        {
            return;
        }
        var from = EntryIndex(id);
        if (from < 0 || newIndex < 0 || newIndex >= playlist.Entries.Count)
        {
            return;
        }
        Submit(new MoveEntry(id, newIndex));
    }

    public int EntryIndex(EntryId id)
    {
        if (SelectedPlaylist is not { } playlist)
        {
            return -1;
        }
        for (var i = 0; i < playlist.Entries.Count; i++)
        {
            if (playlist.Entries[i].Id == id)
            {
                return i;
            }
        }
        return -1;
    }

    public void AddEntryAt(TrackId track, int index)
    {
        if (SelectedPlaylist is not { } playlist)
        {
            return;
        }
        Submit(new AddEntry(playlist.Id, track, Math.Clamp(index, 0, playlist.Entries.Count)));
    }

    public void EnqueueEntry(EntryVm entry) => Submit(new EnqueueEntry(entry.Id));

    public void PlayEntry(EntryVm entry) => Submit(new JumpTo(entry.Id));

    public void SetLinkedTrack(TrackId? track)
    {
        if (_linkedTrackId == track)
        {
            return;
        }
        _linkedTrackId = track;
        if (_lastShow is { } show)
        {
            Rebuild(show, _trackSource?.Invoke() ?? []);
        }
    }

    private void Post(Action work)
    {
        if (_sync is { } sync)
        {
            sync.Post(_ => work(), null);
        }
        else
        {
            work();
        }
    }

    public void Dispose()
    {
        _statusClear?.Cancel();
        _statusClear?.Dispose();
        _subscription.Dispose();
    }

    private void SetTransientStatus(string text)
    {
        PlaylistIoStatus = text;
        _statusClear?.Cancel();
        _statusClear?.Dispose();
        var cts = new CancellationTokenSource();
        _statusClear = cts;
        _ = ClearStatusAfterDelayAsync(cts.Token);
    }

    private async Task ClearStatusAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(_transientStatusTtl, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        Post(() => PlaylistIoStatus = string.Empty);
    }

    private void Submit(Command command) => _bus.Submit(_client, Interlocked.Increment(ref _seq), command);

    private void Apply(StateEvent e)
    {
        Post(() => ApplyOnUi(e));
    }

    private void ApplyOnUi(StateEvent e)
    {
        switch (e)
        {
            case ShowDelta delta:
                Rebuild(delta.State, _trackSource?.Invoke() ?? []);
                break;
            case TransportDelta delta:
                var incoming = new HashSet<TrackId>(delta.State.Faulted);
                var incomingCauses = FaultCauseMap(delta.State);
                if (!incoming.SetEquals(_faulted) || !CausesEqual(incomingCauses))
                {
                    _faulted.Clear();
                    foreach (var id in incoming)
                    {
                        _faulted.Add(id);
                    }
                    MirrorFaultCauses(delta.State);
                    if (_lastShow is { } show)
                    {
                        Rebuild(show, _trackSource?.Invoke() ?? []);
                    }
                }
                break;
        }
    }

    private void MirrorFaultCauses(TransportState state)
    {
        _faultCauses.Clear();
        foreach (var entry in FaultCauseMap(state))
        {
            _faultCauses[entry.Key] = entry.Value;
        }
    }

    private static Dictionary<TrackId, SourceOpenFault> FaultCauseMap(TransportState state)
    {
        var map = new Dictionary<TrackId, SourceOpenFault>();
        if (state.FaultCauses.IsDefault)
        {
            return map;
        }
        foreach (var mark in state.FaultCauses)
        {
            map[mark.Track] = mark.Cause;
        }
        return map;
    }

    private bool CausesEqual(Dictionary<TrackId, SourceOpenFault> incoming)
    {
        if (incoming.Count != _faultCauses.Count)
        {
            return false;
        }
        foreach (var entry in incoming)
        {
            if (!_faultCauses.TryGetValue(entry.Key, out var cause) || cause != entry.Value)
            {
                return false;
            }
        }
        return true;
    }

    private string BuildKey(ShowState state, ImmutableArray<Track> tracks)
    {
        var sb = new StringBuilder();
        sb.Append(state.ActiveId);
        foreach (var playlist in state.Playlists)
        {
            sb.Append('|').Append(playlist.Id).Append(':').Append(playlist.Name)
                .Append(playlist.Id == state.ActiveId ? "*" : ".");
            foreach (var entry in playlist.Entries)
            {
                sb.Append('|').Append(entry)
                    .Append(RuntimeHelpers.GetHashCode(_thumbs?.For(entry.TrackId)));
            }
        }
        foreach (var track in tracks)
        {
            sb.Append('|').Append(track);
        }
        var faultedHash = 0;
        foreach (var id in _faulted)
        {
            faultedHash ^= HashCode.Combine(id, _faultCauses.GetValueOrDefault(id, SourceOpenFault.Unknown));
        }
        sb.Append('|').Append(faultedHash).Append('|').Append(_linkedTrackId).Append('|').Append(_rowSettings);
        return sb.ToString();
    }

    private void Rebuild(ShowState state, ImmutableArray<Track> tracks)
    {
        _lastShow = state;
        var key = BuildKey(state, tracks);
        if (key == _lastKey)
        {
            return;
        }
        _lastKey = key;

        _newPlaylistCounter = Math.Max(_newPlaylistCounter, state.Playlists.Length);
        var trackNames = tracks.ToDictionary(t => t.Id, t => t.DefaultName);
        var trackEndActions = tracks.ToDictionary(t => t.Id, t => t.Defaults.EndAction);
        var trackDurations = tracks.ToDictionary(t => t.Id, t => t.Duration);
        var trackFiles = tracks.ToDictionary(t => t.Id, t => Path.GetFileName(t.FilePath));
        var selectedPlaylistId = SelectedPlaylist?.Id;
        var selectedEntryId = SelectedEntry?.Id;
        var awaitedName = _awaitedPlaylistName;

        Playlists.Clear();
        foreach (var playlist in state.Playlists)
        {
            var playlistVm = new PlaylistVm(playlist.Id, playlist.Name, playlist.Id == state.ActiveId);
            for (var index = 0; index < playlist.Entries.Length; index++)
            {
                var entry = playlist.Entries[index];
                var displayName = entry.Overrides?.Name ?? trackNames.GetValueOrDefault(entry.TrackId, $"track {entry.TrackId.Value:N}");
                var fileName = trackFiles.GetValueOrDefault(entry.TrackId, displayName);
                var duration = trackDurations.GetValueOrDefault(entry.TrackId, TimeSpan.Zero);
                var position = $"{index + 1:00}";
                var trackEndAction = trackEndActions.GetValueOrDefault(entry.TrackId, EndAction.Advance);
                var effectiveEndAction = entry.Overrides?.EndAction
                    ?? (trackEndAction != EndAction.Advance ? trackEndAction : _rowSettings.DefaultEndAction);
                playlistVm.Entries.Add(new EntryVm(
                    entry.Id,
                    entry.TrackId,
                    displayName,
                    fileName,
                    RowFormatter.Format(
                        _rowSettings.RowFormat,
                        position,
                        RowFormatter.DisplayName(_rowSettings, displayName, fileName),
                        fileName,
                        duration.ToString(@"mm\:ss")),
                    entry.Overrides?.Color,
                    entry.Overrides?.Note,
                    entry.Overrides,
                    _thumbs?.For(entry.TrackId),
                    _faulted.Contains(entry.TrackId),
                    position,
                    duration,
                    _linkedTrackId == entry.TrackId,
                    effectiveEndAction));
            }
            Playlists.Add(playlistVm);
        }

        PlaylistVm? awaited = null;
        if (awaitedName is not null)
        {
            awaited = Playlists.FirstOrDefault(p => p.Name == awaitedName);
            if (awaited is not null)
            {
                _awaitedPlaylistName = null;
            }
        }
        SelectedPlaylist = awaited ?? Playlists.FirstOrDefault(p => p.Id == selectedPlaylistId) ?? Playlists.FirstOrDefault();
        SelectedEntry = SelectedPlaylist?.Entries.FirstOrDefault(e => e.Id == selectedEntryId) ?? SelectedPlaylist?.Entries.FirstOrDefault();
        RefreshVisible();
        RefreshCenterHeader();
        FireReveal();
    }

    private void FireReveal()
    {
        var tracks = _revealTracks;
        var target = _revealPlaylist;
        _revealPlaylist = null;
        _revealTracks = null;
        if (tracks is not { Count: > 0 } || target is null || SelectedPlaylist?.Id != target)
        {
            return;
        }
        var row = SelectedPlaylist.Entries.FirstOrDefault(e => tracks.Contains(e.TrackId));
        if (row is not null)
        {
            SelectedEntry = row;
            RevealRequested?.Invoke(row);
        }
    }

    partial void OnSelectedPlaylistChanged(PlaylistVm? value)
    {
        RefreshVisible();
        RefreshCenterHeader();
    }

    partial void OnCenterSearchTextChanged(string value)
    {
        RefreshVisible();
        RefreshCenterHeader();
    }

    [RelayCommand]
    private void ClearSearch() => CenterSearchText = string.Empty;

    private void RefreshCenterHeader()
    {
        if (SelectedPlaylist is null)
        {
            EntryCountText = string.Empty;
            return;
        }
        var seconds = 0;
        foreach (var entry in SelectedPlaylist.Entries)
        {
            seconds += (int)entry.Duration.TotalSeconds;
        }
        var total = seconds >= 3600
            ? $"{seconds / 3600}:{seconds % 3600 / 60:00}:{seconds % 60:00}"
            : $"{seconds / 60}:{seconds % 60:00}";
        var count = SelectedPlaylist.Entries.Count;
        var shown = VisibleEntries.Count;
        EntryCountText = CenterSearchText.Length == 0 || shown == count
            ? $"{count} {TrackCountWord(count)} · {total}"
            : $"{shown} из {count} · {total}";
    }

    private static string TrackCountWord(int count)
    {
        var mod10 = count % 10;
        var mod100 = count % 100;
        if (mod10 == 1 && mod100 != 11)
        {
            return "трек";
        }
        if (mod10 is 2 or 3 or 4 && mod100 is not (12 or 13 or 14))
        {
            return "трека";
        }
        return "треков";
    }

    private void RefreshVisible()
    {
        VisibleEntries.Clear();
        if (SelectedPlaylist is null)
        {
            return;
        }
        foreach (var entry in SelectedPlaylist.Entries)
        {
            if (CenterSearchText.Length == 0 || entry.DisplayName.Contains(CenterSearchText, StringComparison.OrdinalIgnoreCase))
            {
                VisibleEntries.Add(entry);
            }
        }
    }

    private int _newPlaylistCounter;

    public sealed record PlaylistImportReport(
        string PlaylistName,
        int Added,
        ImmutableArray<string> MissingFiles,
        int PendingTransitions,
        string? Error = null);

    public sealed class PlaylistVm(PlaylistId id, string name, bool isActive)
    {
        public PlaylistId Id { get; } = id;

        public string Name { get; } = name;

        public bool IsActive { get; } = isActive;

        public ObservableCollection<EntryVm> Entries { get; } = [];
    }

    public sealed record EntryVm(
        EntryId Id,
        TrackId TrackId,
        string DisplayName,
        string FileName,
        string RowText,
        string? Color,
        string? Note,
        PlaylistOverrides? Overrides,
        StreamGeometry? Waveform,
        bool IsFaulted,
        string Position,
        TimeSpan Duration,
        bool IsLinked = false,
        EndAction EffectiveEndAction = EndAction.Advance)
    {
        public bool HasOverrides => Overrides is not null;

        public bool HasNote => !string.IsNullOrEmpty(Note);

        public bool HasEndActionOverride => Overrides?.EndAction is not null;

        public string EndActionBadge => Overrides?.EndAction switch
        {
            EndAction.Pause => "Пауза",
            EndAction.Stop => "Стоп",
            EndAction.Replay => "Повтор",
            EndAction.Advance => "Далее",
            _ => string.Empty,
        };

        public string EndActionTip => HasEndActionOverride ? $"Действие в конце: {EndActionBadge}" : string.Empty;

        public string DurationText => Duration.ToString(@"mm\:ss");

        public IBrush ColorBrush => string.IsNullOrEmpty(Color) ? Brushes.DimGray : new SolidColorBrush(Avalonia.Media.Color.Parse(Color));
    }
}
