namespace Aria.App.ViewModels;

using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Text;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;
using Aria.Core.State;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public sealed partial class ScriptPanelViewModel : ObservableObject, IDisposable
{
    private const int SuggestLimit = 5;

    private readonly ICommandBus _bus;
    private readonly ClientId _client = new("desktop-scripts");
    private readonly Func<ImmutableArray<Track>>? _trackSource;
    private readonly IDisposable _subscription;
    private readonly SynchronizationContext? _sync;
    private readonly HashSet<ScriptId> _knownScripts = [];
    private readonly HashSet<ScriptLineId> _knownLines = [];
    private string? _lastScriptKey;
    private bool _editNextArrival;
    private bool _addLinePending;
    private TimeSpan _elapsed;
    private bool _clockRunning;
    private int _newScriptCounter;
    private long _seq;

    [ObservableProperty]
    private ScriptVm? selectedScript;

    [ObservableProperty]
    private TrackId? highlightedTrack;

    public ObservableCollection<ScriptVm> Scripts { get; } = [];

    public ObservableCollection<ScriptLineVm> Lines { get; } = [];

    partial void OnSelectedScriptChanged(ScriptVm? value) => OnPropertyChanged(nameof(ShowEmptyScript));

    public ScriptPanelViewModel(ICommandBus bus, Func<ImmutableArray<Track>>? trackSource = null, SynchronizationContext? sync = null)
    {
        _bus = bus;
        _trackSource = trackSource;
        _sync = sync;
        _subscription = bus.Subscribe(Apply);
        Scripts.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasScripts));
            OnPropertyChanged(nameof(ShowNoScripts));
        };
        Lines.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasLines));
            OnPropertyChanged(nameof(ShowEmptyScript));
        };
        Rebuild(bus.Snapshot().Show);
    }

    public TimeSpan Elapsed => _elapsed;

    public bool HasScripts => Scripts.Count > 0;

    public bool ShowNoScripts => Scripts.Count == 0;

    public bool HasLines => Lines.Count > 0;

    public bool ShowEmptyScript => SelectedScript is not null && Lines.Count == 0;

    [RelayCommand]
    private void CreateScript()
    {
        CommitOpenEdit();
        foreach (var script in Scripts)
        {
            _knownScripts.Add(script.Id);
        }
        Submit(new CreateScript(UniqueScriptName()));
    }

    public void SelectScript(ScriptVm script)
    {
        CommitOpenEdit();
        SelectedScript = script;
        Rebuild(_bus.Snapshot().Show);
    }

    public void RenameSelected(string? name)
    {
        CommitOpenEdit();
        if (SelectedScript is not { } script || string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        Submit(new RenameScript(script.Id, name));
    }

    [RelayCommand]
    public void DeleteSelected()
    {
        CommitOpenEdit();
        if (SelectedScript is not { } script)
        {
            return;
        }
        Submit(new DeleteScript(script.Id));
    }

    [RelayCommand]
    private void AddLine()
    {
        CommitOpenEdit();
        if (SelectedScript is null)
        {
            _addLinePending = true;
            Submit(new CreateScript(UniqueScriptName()));
            return;
        }
        _knownLines.Clear();
        foreach (var line in Lines)
        {
            _knownLines.Add(line.Id);
        }
        _editNextArrival = true;
        Submit(new AddScriptLine(SelectedScript.Id, _elapsed, string.Empty, []));
    }

    public void BeginEdit(ScriptLineVm line) => line.StartEdit();

    public bool CommitEdit(ScriptLineVm line)
    {
        if (SelectedScript is null || !line.IsEditing)
        {
            return false;
        }
        if (!TryParseLineTime(line.EditTimeText, out var atElapsed))
        {
            return false;
        }
        var staged = line.StagedMentions.Select(m => m.Track).ToImmutableArray();
        if (atElapsed != line.AtElapsed
            || line.EditText != line.Text
            || !staged.SequenceEqual(line.Mentions.Select(m => m.Track)))
        {
            Submit(new UpdateScriptLine(SelectedScript.Id, line.Id, atElapsed, line.EditText, staged));
        }
        line.FinishEdit();
        Rebuild(_bus.Snapshot().Show);
        return true;
    }

    public void CommitEditAndNewLine(ScriptLineVm line)
    {
        if (CommitEdit(line))
        {
            AddLine();
        }
    }

    public void CancelEdit(ScriptLineVm line) => line.CancelEdit();

    public void CommitOpenEdit()
    {
        foreach (var line in Lines.ToList())
        {
            if (line.IsEditing)
            {
                CommitEdit(line);
            }
        }
    }

    public void RemoveLine(ScriptLineVm line)
    {
        CommitOpenEdit();
        if (SelectedScript is null)
        {
            return;
        }
        Submit(new RemoveScriptLine(SelectedScript.Id, line.Id));
    }

    public void MoveLine(int from, int to)
    {
        CommitOpenEdit();
        if (SelectedScript is null || from < 0 || from >= Lines.Count || to < 0 || to >= Lines.Count || from == to)
        {
            return;
        }
        Submit(new MoveScriptLine(SelectedScript.Id, Lines[from].Id, to));
    }

    public void MarkNow(ScriptLineVm line) => line.EditTimeText = FormatLineTime(_elapsed);

    public void ExecuteLine(ScriptLineVm line)
    {
        if (line.IsEditing)
        {
            return;
        }
        CommitOpenEdit();
        switch (line.Mentions.Count)
        {
            case 0:
                break;
            case 1 when line.Mentions[0].IsDangling || !IsKnownTrack(line.Mentions[0].Track):
                break;
            case 1:
                Submit(new EnqueueTrack(line.Mentions[0].Track));
                break;
            default:
                line.CandidatesVisible = !line.CandidatesVisible;
                break;
        }
    }

    public void ChooseCandidate(ScriptLineVm line, MentionVm mention)
    {
        CommitOpenEdit();
        if (mention.IsDangling || !IsKnownTrack(mention.Track))
        {
            line.CandidatesVisible = false;
            return;
        }
        Submit(new EnqueueTrack(mention.Track));
        line.CandidatesVisible = false;
    }

    public void ExecuteMention(MentionVm mention)
    {
        if (mention.IsDangling || !IsKnownTrack(mention.Track))
        {
            return;
        }
        CommitOpenEdit();
        Submit(new EnqueueTrack(mention.Track));
    }

    private bool IsKnownTrack(TrackId track) =>
        (_trackSource?.Invoke() ?? []).Any(t => t.Id == track);

    public void InsertMention(ScriptLineVm line, TrackId track)
    {
        if (line.StagedMentions.Any(m => m.Track == track))
        {
            return;
        }
        line.StagedMentions.Add(ResolveMention(track));
    }

    public void RemoveStagedMention(ScriptLineVm line, MentionVm mention) => line.StagedMentions.Remove(mention);

    public IReadOnlyList<Track> SuggestTracks(string query)
    {
        var source = _trackSource?.Invoke() ?? [];
        if (string.IsNullOrWhiteSpace(query))
        {
            return source.Take(SuggestLimit).ToList();
        }
        return source
            .Where(t => t.DefaultName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(SuggestLimit)
            .ToList();
    }

    public void RefreshSuggestions(ScriptLineVm line, string query)
    {
        line.Suggestions.Clear();
        foreach (var track in SuggestTracks(query))
        {
            line.Suggestions.Add(track);
        }
        line.SuggestionsVisible = line.Suggestions.Count > 0;
    }

    public void CloseSuggestions(ScriptLineVm line) => line.SuggestionsVisible = false;

    public void Dispose() => _subscription.Dispose();

    public static string FormatLineTime(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }
        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{(int)value.TotalMinutes}:{value.Seconds:00}";
    }

    public static bool TryParseLineTime(string? text, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }
        var parts = text.Trim().Split(':');
        if (parts.Length == 2
            && int.TryParse(parts[0], out var minutes) && minutes >= 0
            && int.TryParse(parts[1], out var seconds) && seconds is >= 0 and < 60)
        {
            value = new TimeSpan(0, minutes, seconds);
            return true;
        }
        if (parts.Length == 3
            && int.TryParse(parts[0], out var hours) && hours >= 0
            && int.TryParse(parts[1], out minutes) && minutes is >= 0 and < 60
            && int.TryParse(parts[2], out seconds) && seconds is >= 0 and < 60)
        {
            value = new TimeSpan(hours, minutes, seconds);
            return true;
        }
        return false;
    }

    private void Submit(Command command) => _bus.Submit(_client, Interlocked.Increment(ref _seq), command);

    private void Apply(StateEvent e)
    {
        if (e is ShowDelta)
        {
            Post(() => Rebuild(_bus.Snapshot().Show));
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

    private void Rebuild(ShowState state)
    {
        _elapsed = state.Clock.Elapsed;
        _clockRunning = state.Clock.Running;
        var tracks = _trackSource?.Invoke() ?? [];
        SyncScripts(state);
        SyncLines(state, tracks);
        RefreshFollow(state);
        if (_addLinePending)
        {
            _addLinePending = false;
            if (SelectedScript is not null)
            {
                _knownLines.Clear();
                foreach (var line in Lines)
                {
                    _knownLines.Add(line.Id);
                }
                _editNextArrival = true;
                Submit(new AddScriptLine(SelectedScript.Id, _elapsed, string.Empty, []));
            }
        }
    }

    private void SyncScripts(ShowState state)
    {
        var selectedId = SelectedScript?.Id;
        var key = BuildScriptKey(state, selectedId);
        if (key == _lastScriptKey && SelectedScript is not null)
        {
            return;
        }
        _lastScriptKey = key;
        Scripts.Clear();
        foreach (var script in state.Scripts)
        {
            Scripts.Add(new ScriptVm(script.Id, script.Name));
        }
        if (!Scripts.Any(s => s.Id == selectedId))
        {
            selectedId = Scripts.FirstOrDefault(s => !_knownScripts.Contains(s.Id))?.Id
                ?? Scripts.FirstOrDefault()?.Id;
        }
        _knownScripts.Clear();
        foreach (var script in Scripts)
        {
            _knownScripts.Add(script.Id);
        }
        SelectedScript = Scripts.FirstOrDefault(s => s.Id == selectedId) ?? Scripts.FirstOrDefault();
    }

    private static bool SameOrder(List<ScriptLineVm> fresh, ObservableCollection<ScriptLineVm> current)
    {
        if (fresh.Count != current.Count)
        {
            return false;
        }
        for (var index = 0; index < fresh.Count; index++)
        {
            if (!ReferenceEquals(fresh[index], current[index]))
            {
                return false;
            }
        }
        return true;
    }

    private static string BuildScriptKey(ShowState state, ScriptId? selectedId)
    {
        var sb = new StringBuilder();
        sb.Append(selectedId);
        foreach (var script in state.Scripts)
        {
            sb.Append('|').Append(script.Id).Append(':').Append(script.Name);
        }
        return sb.ToString();
    }

    private void SyncLines(ShowState state, ImmutableArray<Track> tracks)
    {
        var script = SelectedScript is null
            ? null
            : state.Scripts.FirstOrDefault(s => s.Id == SelectedScript.Id);
        if (script is null)
        {
            Lines.Clear();
            return;
        }
        var byId = Lines.ToDictionary(l => l.Id);
        var fresh = new List<ScriptLineVm>(script.Lines.Length);
        foreach (var line in script.Lines)
        {
            if (!byId.TryGetValue(line.Id, out var vm))
            {
                vm = new ScriptLineVm(line.Id, line.AtElapsed, line.Text);
            }
            if (!vm.IsEditing)
            {
                vm.Refresh(line.AtElapsed, line.Text, ResolveMentions(line.Mentions, tracks));
            }
            else
            {
                vm.RefreshStagedNames(tracks);
            }
            fresh.Add(vm);
        }
        if (!SameOrder(fresh, Lines) && Lines.All(l => !l.IsEditing))
        {
            Lines.Clear();
            foreach (var vm in fresh)
            {
                Lines.Add(vm);
            }
        }
        if (_editNextArrival)
        {
            _editNextArrival = false;
            var arrival = Lines.FirstOrDefault(l => !_knownLines.Contains(l.Id) && !l.IsEditing)
                ?? Lines.LastOrDefault(l => !l.IsEditing);
            arrival?.StartEdit();
        }
        _knownLines.Clear();
    }

    private void RefreshFollow(ShowState state)
    {
        var script = SelectedScript is null
            ? null
            : state.Scripts.FirstOrDefault(s => s.Id == SelectedScript.Id);
        var current = script is null ? null : ScriptFollow.CurrentLine(script, _elapsed);
        foreach (var line in Lines)
        {
            if (!line.IsEditing)
            {
                line.IsCurrent = current is not null && line.Id == current.Id;
            }
            line.WallTimeTip = WallTimeTip(line.AtElapsed);
        }
    }

    private string WallTimeTip(TimeSpan atElapsed) =>
        _clockRunning
            ? (DateTime.Now - _elapsed + atElapsed).ToString("HH:mm:ss")
            : "часы не запущены";

    private string UniqueScriptName()
    {
        var taken = new HashSet<string>(Scripts.Select(s => s.Name), StringComparer.Ordinal);
        if (taken.Add("Новый сценарий"))
        {
            return "Новый сценарий";
        }
        while (true)
        {
            _newScriptCounter++;
            var candidate = $"Новый сценарий {_newScriptCounter}";
            if (taken.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private MentionVm ResolveMention(TrackId track) => ResolveMentions([new Mention(track)], _trackSource?.Invoke() ?? [])[0];

    private List<MentionVm> ResolveMentions(ImmutableArray<Mention> mentions, ImmutableArray<Track> tracks)
    {
        var result = new List<MentionVm>(mentions.Length);
        foreach (var mention in mentions)
        {
            result.Add(ResolveDisplay(mention.Track, tracks));
        }
        return result;
    }

    private static MentionVm ResolveDisplay(TrackId track, ImmutableArray<Track> tracks)
    {
        var known = tracks.FirstOrDefault(t => t.Id == track);
        return known is null
            ? new MentionVm(track, "—", true, "--:--")
            : new MentionVm(track, known.DefaultName, false, known.Duration.ToString(@"mm\:ss"));
    }

    public sealed class ScriptVm(ScriptId id, string name)
    {
        public ScriptId Id { get; } = id;

        public string Name { get; } = name;
    }

    public sealed class MentionVm(TrackId track, string displayName, bool isDangling, string durationText)
    {
        public TrackId Track { get; } = track;

        public string DisplayName { get; } = displayName;

        public bool IsDangling { get; } = isDangling;

        public string DurationText { get; } = durationText;

        public string Tooltip => IsDangling ? "повисшее упоминание" : $"{DisplayName} · {DurationText}";
    }

    public sealed class ScriptLineVm : ObservableObject
    {
        private TimeSpan _atElapsed;
        private string _text;
        private bool _hasText;
        private bool _isCurrent;
        private bool _isEditing;
        private bool _candidatesVisible;
        private bool _suggestionsVisible;
        private string _editTimeText = string.Empty;
        private string _editText = string.Empty;

        public ScriptLineVm(ScriptLineId id, TimeSpan atElapsed, string text)
        {
            Id = id;
            _atElapsed = atElapsed;
            _text = text;
            _hasText = text.Length > 0;
            DisplayTime = FormatLineTime(atElapsed);
        }

        public ScriptLineId Id { get; }

        public TimeSpan AtElapsed
        {
            get => _atElapsed;
            private set
            {
                if (SetProperty(ref _atElapsed, value))
                {
                    DisplayTime = FormatLineTime(value);
                }
            }
        }

        public string Text
        {
            get => _text;
            private set
            {
                if (SetProperty(ref _text, value))
                {
                    HasText = value.Length > 0;
                }
            }
        }

        public bool HasText
        {
            get => _hasText;
            private set => SetProperty(ref _hasText, value);
        }

        public string DisplayTime
        {
            get;
            private set => SetProperty(ref field, value);
        } = string.Empty;

        public bool IsCurrent
        {
            get => _isCurrent;
            set => SetProperty(ref _isCurrent, value);
        }

        public bool IsEditing
        {
            get => _isEditing;
            private set => SetProperty(ref _isEditing, value);
        }

        public bool CandidatesVisible
        {
            get => _candidatesVisible;
            set => SetProperty(ref _candidatesVisible, value);
        }

        public bool SuggestionsVisible
        {
            get => _suggestionsVisible;
            set => SetProperty(ref _suggestionsVisible, value);
        }

        public string WallTimeTip
        {
            get;
            set
            {
                if (value != field)
                {
                    field = value;
                    OnPropertyChanged();
                }
            }
        } = string.Empty;

        public string EditTimeText
        {
            get => _editTimeText;
            set => SetProperty(ref _editTimeText, value);
        }

        public string EditText
        {
            get => _editText;
            set => SetProperty(ref _editText, value);
        }

        public ObservableCollection<MentionVm> Mentions { get; } = [];

        public ObservableCollection<MentionVm> Candidates { get; } = [];

        public ObservableCollection<MentionVm> StagedMentions { get; } = [];

        public ObservableCollection<Track> Suggestions { get; } = [];

        public void StartEdit()
        {
            EditTimeText = FormatLineTime(AtElapsed);
            EditText = Text;
            StagedMentions.Clear();
            foreach (var mention in Mentions)
            {
                StagedMentions.Add(mention);
            }
            IsEditing = true;
        }

        public void FinishEdit() => IsEditing = false;

        public void CancelEdit()
        {
            StagedMentions.Clear();
            IsEditing = false;
        }

        public void Refresh(TimeSpan atElapsed, string text, List<MentionVm> mentions)
        {
            AtElapsed = atElapsed;
            Text = text;
            Mentions.Clear();
            foreach (var mention in mentions)
            {
                Mentions.Add(mention);
            }
            Candidates.Clear();
            foreach (var mention in mentions)
            {
                Candidates.Add(mention);
            }
        }

        public void RefreshStagedNames(ImmutableArray<Track> tracks)
        {
            for (var i = 0; i < StagedMentions.Count; i++)
            {
                var staged = StagedMentions[i];
                var resolved = ResolveDisplay(staged.Track, tracks);
                if (resolved.DisplayName != staged.DisplayName || resolved.IsDangling != staged.IsDangling)
                {
                    StagedMentions[i] = resolved;
                }
            }
        }
    }
}
