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

public sealed partial class ProjectsViewModel : ObservableObject, IDisposable
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
    private string? _awaitedProjectName;
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
    private ProjectVm? selectedProject;

    [ObservableProperty]
    private EntryVm? selectedEntry;

    [ObservableProperty]
    private string centerSearchText = string.Empty;

    [ObservableProperty]
    private string entryCountText = string.Empty;

    [ObservableProperty]
    private string projectIoStatus = string.Empty;

    [ObservableProperty]
    private string lastImportError = string.Empty;

    public event Action<string>? ImportFailed;

    public event Action<string>? AudioImportIncomplete;

    public event Action<ProjectImportReport>? ProjectImportMissing;

    public event Action<string, string>? ExportSucceeded;

    public event Action<EntryVm>? RevealRequested;

    private ProjectId? _revealProject;

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

    public ObservableCollection<ProjectVm> Projects { get; } = [];

    public ObservableCollection<EntryVm> VisibleEntries { get; } = [];

    public ProjectsViewModel(ICommandBus bus, Func<ImmutableArray<Track>>? trackSource = null, WaveformThumbs? thumbs = null, AppSettings? rowSettings = null, SynchronizationContext? sync = null, Func<TopLevel?>? topLevel = null, Func<IReadOnlyList<string>, IProgress<string>?, Task<ImportReport>>? audioImport = null, TimeSpan? transientStatusTtl = null, Func<TrackId, TrackAudioSettings?>? trackAudio = null, Func<Task<IReadOnlyList<string>>>? folderPicker = null)
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
    private void CreateProject()
    {
        _awaitedProjectName = $"Новый проект {++_newProjectCounter}";
        Submit(new CreateProject(_awaitedProjectName));
    }

    [RelayCommand]
    private void RenameProject(string? name)
    {
        if (SelectedProject is not { } project || string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        Submit(new RenameProject(project.Id, name));
    }

    [RelayCommand]
    private void DeleteProject()
    {
        if (SelectedProject is not { } project)
        {
            return;
        }
        Submit(new DeleteProject(project.Id));
    }

    [RelayCommand]
    private void ActivateProject()
    {
        if (SelectedProject is not { } project)
        {
            return;
        }
        Submit(new SetActiveProject(project.Id));
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
    private async Task ExportProjectAsync()
    {
        var topLevel = _topLevel?.Invoke();
        if (topLevel is null)
        {
            SetTransientStatus("экспорт недоступен");
            return;
        }
        if (SelectedProject is null)
        {
            SetTransientStatus("нет проекта для экспорта");
            return;
        }
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Экспорт проекта",
            SuggestedFileName = SelectedProject.Name + ProjectFormat.FileExtension,
            FileTypeChoices =
            [
                new FilePickerFileType("ARIA-проект") { Patterns = [$"*{ProjectFormat.FileExtension}"] },
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
        var count = SelectedProject?.Entries.Count ?? 0;
        ProjectIoStatus = string.Empty;
        ExportSucceeded?.Invoke(fileName, $"Сохранено: {fileName}\nТреков: {count}");
    }

    [RelayCommand]
    private async Task ImportProjectAsync()
    {
        var topLevel = _topLevel?.Invoke();
        if (topLevel is null)
        {
            SetTransientStatus("импорт недоступен");
            return;
        }
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Импорт проекта",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("ARIA-проект") { Patterns = [$"*{ProjectFormat.FileExtension}", "*.json"] },
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
            Title = "Импорт аудио в проект",
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

    public Task<IReadOnlyList<TrackId>> ImportAudioFilesAsync(IEnumerable<string> paths, bool silent = false)
    {
        if (SelectedProject is null)
        {
            SetTransientStatus("нет проекта для импорта");
            return Task.FromResult<IReadOnlyList<TrackId>>([]);
        }
        return ImportIntoAsync(paths, silent, SelectedProject);
    }

    public async Task ImportDroppedPathsAsync(IEnumerable<string> paths)
    {
        var inputs = paths.ToArray();
        var files = inputs.Where(input => !Directory.Exists(input)).ToArray();
        if (files.Length > 0)
        {
            await ImportAudioFilesAsync(files);
        }
        foreach (var folder in inputs.Where(Directory.Exists))
        {
            var name = UniqueProjectName(FolderProjectName(folder));
            _awaitedProjectName = name;
            Submit(new CreateProject(name));
            var target = await WaitForProjectAsync(name);
            if (target is null)
            {
                SetTransientStatus($"не удалось создать проект: {name}");
                continue;
            }
            await ImportIntoAsync([folder], false, target);
        }
    }

    private async Task<IReadOnlyList<TrackId>> ImportIntoAsync(IEnumerable<string> paths, bool silent, ProjectVm target)
    {
        var inputs = paths.ToArray();
        if (inputs.Length == 0)
        {
            return [];
        }
        if (_audioImport is null)
        {
            SetTransientStatus("импорт недоступен");
            return [];
        }
        IProgress<string>? progress = silent ? null : new Progress<string>(name => ProjectIoStatus = $"импорт: {name}");
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
        _revealProject = ordered.Count > 0 ? target.Id : null;
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

    private string UniqueProjectName(string baseName)
    {
        if (!Projects.Any(p => p.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase)))
        {
            return baseName;
        }
        var suffix = 2;
        while (Projects.Any(p => p.Name.Equals($"{baseName} {suffix}", StringComparison.OrdinalIgnoreCase)))
        {
            suffix++;
        }
        return $"{baseName} {suffix}";
    }

    private static string FolderProjectName(string folder)
    {
        var trimmed = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? "Новая папка" : name;
    }

    private async Task<ProjectVm?> WaitForProjectAsync(string name)
    {
        var deadline = Environment.TickCount64 + 5000;
        while (Environment.TickCount64 < deadline)
        {
            var found = Projects.FirstOrDefault(p => p.Name == name);
            if (found is not null)
            {
                return found;
            }
            await Task.Delay(20);
        }
        return Projects.FirstOrDefault(p => p.Name == name);
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
        if (SelectedProject is null)
        {
            throw new InvalidOperationException("Нет выбранного проекта");
        }
        var files = (_trackSource?.Invoke() ?? []).ToDictionary(t => t.Id, t => t.FilePath);
        var entries = SelectedProject.Entries.Select(entry => new ProjectExportEntry(
            files.GetValueOrDefault(entry.TrackId, entry.DisplayName),
            entry.Overrides));
        return ProjectFormat.Export(SelectedProject.Name, entries);
    }

    public Task<ProjectImportReport> ImportDocumentAsync(string json)
    {
        ProjectFileDocument document;
        try
        {
            document = ProjectFormat.Import(json);
        }
        catch (ProjectFormatException e)
        {
            LastImportError = e.Message;
            SetTransientStatus("импорт не удался");
            ImportFailed?.Invoke(e.Message);
            return Task.FromResult(new ProjectImportReport(string.Empty, 0, [], 0, e.Message));
        }
        var tracks = _trackSource?.Invoke() ?? [];
        var byPath = tracks.ToDictionary(t => UnicodePaths.Key(t.FilePath), t => t.Id);
        var byName = tracks
            .GroupBy(t => UnicodePaths.Key(Path.GetFileName(t.FilePath)), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        var imports = new List<ImportProjectEntry>();
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
            imports.Add(new ImportProjectEntry(trackId, ProjectFormat.ToOverrides(entry)));
        }
        if (imports.Count == 0)
        {
            var empty = new ProjectImportReport(document.Name, 0, [.. missing], 0, "нет известных треков");
            if (empty.MissingFiles.Length > 0)
            {
                ProjectImportMissing?.Invoke(empty);
            }
            return Task.FromResult(empty);
        }
        _awaitedProjectName = document.Name;
        Submit(new ImportProject(document.Name, [.. imports]));
        LastImportError = string.Empty;
        var report = new ProjectImportReport(document.Name, imports.Count, [.. missing], pendingTransitions);
        SetTransientStatus(Describe(report));
        if (report.MissingFiles.Length > 0)
        {
            ProjectImportMissing?.Invoke(report);
        }
        return Task.FromResult(report);
    }

    private static string Describe(ProjectImportReport report)
    {
        if (report.Error is not null)
        {
            return "импорт не удался";
        }
        var text = $"импортировано: {report.ProjectName} ({report.Added})";
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
        ProjectOverrides? merged;
        if (entry.Overrides is { } current)
        {
            var next = current with { EndAction = action };
            merged = next == new ProjectOverrides() ? null : next;
        }
        else if (action is null)
        {
            return;
        }
        else
        {
            merged = new ProjectOverrides(EndAction: action);
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

    public void DeleteProjectAt(ProjectVm project) => Submit(new DeleteProject(project.Id));

    public void MoveEntry(EntryId id, int newIndex)
    {
        if (SelectedProject is not { } project)
        {
            return;
        }
        var from = EntryIndex(id);
        if (from < 0 || newIndex < 0 || newIndex >= project.Entries.Count)
        {
            return;
        }
        Submit(new MoveEntry(id, newIndex));
    }

    public int EntryIndex(EntryId id)
    {
        if (SelectedProject is not { } project)
        {
            return -1;
        }
        for (var i = 0; i < project.Entries.Count; i++)
        {
            if (project.Entries[i].Id == id)
            {
                return i;
            }
        }
        return -1;
    }

    public void AddEntryAt(TrackId track, int index)
    {
        if (SelectedProject is not { } project)
        {
            return;
        }
        Submit(new AddEntry(project.Id, track, Math.Clamp(index, 0, project.Entries.Count)));
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
        ProjectIoStatus = text;
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
        Post(() => ProjectIoStatus = string.Empty);
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
        foreach (var project in state.Projects)
        {
            sb.Append('|').Append(project.Id).Append(':').Append(project.Name)
                .Append(project.Id == state.ActiveId ? "*" : ".");
            foreach (var entry in project.Entries)
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

        _newProjectCounter = Math.Max(_newProjectCounter, state.Projects.Length);
        var trackNames = tracks.ToDictionary(t => t.Id, t => t.DefaultName);
        var trackEndActions = tracks.ToDictionary(t => t.Id, t => t.Defaults.EndAction);
        var trackDurations = tracks.ToDictionary(t => t.Id, t => t.Duration);
        var trackFiles = tracks.ToDictionary(t => t.Id, t => Path.GetFileName(t.FilePath));
        var selectedProjectId = SelectedProject?.Id;
        var selectedEntryId = SelectedEntry?.Id;
        var awaitedName = _awaitedProjectName;

        Projects.Clear();
        foreach (var project in state.Projects)
        {
            var projectVm = new ProjectVm(project.Id, project.Name, project.Id == state.ActiveId);
            for (var index = 0; index < project.Entries.Length; index++)
            {
                var entry = project.Entries[index];
                var displayName = entry.Overrides?.Name ?? trackNames.GetValueOrDefault(entry.TrackId, $"track {entry.TrackId.Value:N}");
                var fileName = trackFiles.GetValueOrDefault(entry.TrackId, displayName);
                var duration = trackDurations.GetValueOrDefault(entry.TrackId, TimeSpan.Zero);
                var position = $"{index + 1:00}";
                var trackEndAction = trackEndActions.GetValueOrDefault(entry.TrackId, EndAction.Advance);
                var effectiveEndAction = entry.Overrides?.EndAction
                    ?? (trackEndAction != EndAction.Advance ? trackEndAction : _rowSettings.DefaultEndAction);
                projectVm.Entries.Add(new EntryVm(
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
            Projects.Add(projectVm);
        }

        ProjectVm? awaited = null;
        if (awaitedName is not null)
        {
            awaited = Projects.FirstOrDefault(p => p.Name == awaitedName);
            if (awaited is not null)
            {
                _awaitedProjectName = null;
            }
        }
        SelectedProject = awaited ?? Projects.FirstOrDefault(p => p.Id == selectedProjectId) ?? Projects.FirstOrDefault();
        SelectedEntry = SelectedProject?.Entries.FirstOrDefault(e => e.Id == selectedEntryId) ?? SelectedProject?.Entries.FirstOrDefault();
        RefreshVisible();
        RefreshCenterHeader();
        FireReveal();
    }

    private void FireReveal()
    {
        var tracks = _revealTracks;
        var target = _revealProject;
        _revealProject = null;
        _revealTracks = null;
        if (tracks is not { Count: > 0 } || target is null || SelectedProject?.Id != target)
        {
            return;
        }
        var row = SelectedProject.Entries.FirstOrDefault(e => tracks.Contains(e.TrackId));
        if (row is not null)
        {
            SelectedEntry = row;
            RevealRequested?.Invoke(row);
        }
    }

    partial void OnSelectedProjectChanged(ProjectVm? value)
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
        if (SelectedProject is null)
        {
            EntryCountText = string.Empty;
            return;
        }
        var seconds = 0;
        foreach (var entry in SelectedProject.Entries)
        {
            seconds += (int)entry.Duration.TotalSeconds;
        }
        var total = seconds >= 3600
            ? $"{seconds / 3600}:{seconds % 3600 / 60:00}:{seconds % 60:00}"
            : $"{seconds / 60}:{seconds % 60:00}";
        var count = SelectedProject.Entries.Count;
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
        if (SelectedProject is null)
        {
            return;
        }
        foreach (var entry in SelectedProject.Entries)
        {
            if (CenterSearchText.Length == 0 || entry.DisplayName.Contains(CenterSearchText, StringComparison.OrdinalIgnoreCase))
            {
                VisibleEntries.Add(entry);
            }
        }
    }

    private int _newProjectCounter;

    public sealed record ProjectImportReport(
        string ProjectName,
        int Added,
        ImmutableArray<string> MissingFiles,
        int PendingTransitions,
        string? Error = null);

    public sealed class ProjectVm(ProjectId id, string name, bool isActive)
    {
        public ProjectId Id { get; } = id;

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
        ProjectOverrides? Overrides,
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
