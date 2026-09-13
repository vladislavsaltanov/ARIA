namespace Aria.App.ViewModels;

using System.Collections.ObjectModel;
using System.Threading;
using Aria.App.Services;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;
using Aria.Core.State;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    public static readonly IReadOnlyList<string> GestureActions =
    [
        "play", "pause", "panic", "next", "replay", "lock", "toggle-script", "reset-clock",
    ];

    private readonly ICommandBus _bus;
    private readonly ClientId _client = new("desktop-settings");
    private readonly HotkeyService _hotkeys;
    private readonly string _hotkeysPath;
    private readonly AppSettingsStore _settingsStore;
    private readonly Action<AppSettings>? _rowSettingsApplied;
    private readonly IDisposable _subscription;
    private readonly SynchronizationContext? _sync;
    private double _panicFadeMs = 100;
    private EndAction _defaultEndAction = EndAction.Advance;
    private bool _smoothingEnabled;
    private double _manualCrossfadeMs;
    private double _autoCrossfadeMs;
    private double _startFadeMs;
    private double _stopFadeMs;
    private double _seekFadeMs;
    private long _seq;

    [ObservableProperty]
    private bool useFileName;

    [ObservableProperty]
    private string rowFormat = AppSettings.Default.RowFormat;

    [ObservableProperty]
    private string rowSettingsStatus = string.Empty;

    [ObservableProperty]
    private GestureRow? recordingRow;

    public ObservableCollection<GestureRow> Gestures { get; } = [];

    public SettingsViewModel(
        ICommandBus bus,
        HotkeyService hotkeys,
        string hotkeysPath,
        AppSettingsStore settingsStore,
        Action<AppSettings>? rowSettingsApplied = null,
        SynchronizationContext? sync = null)
    {
        _bus = bus;
        _hotkeys = hotkeys;
        _hotkeysPath = hotkeysPath;
        _settingsStore = settingsStore;
        _rowSettingsApplied = rowSettingsApplied;
        _sync = sync;
        _subscription = bus.Subscribe(Apply);
        var settings = settingsStore.Load();
        UseFileName = settings.UseFileName;
        RowFormat = settings.RowFormat;
        RefreshGestures();
        ApplyMixer(bus.Snapshot().Mixer);
        ApplySmoothing(settings.Smoothing);
        if (bus.Snapshot().Mixer.Smoothing != settings.Smoothing)
        {
            Submit(new SetSmoothing(settings.Smoothing));
        }
        _defaultEndAction = settings.DefaultEndAction;
        OnPropertyChanged(nameof(DefaultEndActionIndex));
        Submit(new SetDefaultEndAction(settings.DefaultEndAction));
    }

    public int DefaultEndActionIndex
    {
        get => _defaultEndAction switch
        {
            EndAction.Pause => 0,
            EndAction.Stop => 1,
            EndAction.Replay => 2,
            _ => 3,
        };
        set
        {
            var action = value switch
            {
                0 => EndAction.Pause,
                1 => EndAction.Stop,
                2 => EndAction.Replay,
                _ => EndAction.Advance,
            };
            if (SetProperty(ref _defaultEndAction, action))
            {
                SubmitEndAction();
            }
        }
    }

    public double PanicFadeMs
    {
        get => _panicFadeMs;
        set
        {
            if (SetProperty(ref _panicFadeMs, value))
            {
                Submit(new SetPanicFade(TimeSpan.FromMilliseconds(value)));
            }
        }
    }

    public bool SmoothingEnabled
    {
        get => _smoothingEnabled;
        set
        {
            if (SetProperty(ref _smoothingEnabled, value))
            {
                SubmitSmoothing();
            }
        }
    }

    public double ManualCrossfadeMs
    {
        get => _manualCrossfadeMs;
        set
        {
            if (SetProperty(ref _manualCrossfadeMs, value))
            {
                SubmitSmoothing();
            }
        }
    }

    public double AutoCrossfadeMs
    {
        get => _autoCrossfadeMs;
        set
        {
            if (SetProperty(ref _autoCrossfadeMs, value))
            {
                SubmitSmoothing();
            }
        }
    }

    public double StartFadeMs
    {
        get => _startFadeMs;
        set
        {
            if (SetProperty(ref _startFadeMs, value))
            {
                SubmitSmoothing();
            }
        }
    }

    public double StopFadeMs
    {
        get => _stopFadeMs;
        set
        {
            if (SetProperty(ref _stopFadeMs, value))
            {
                SubmitSmoothing();
            }
        }
    }

    public double SeekFadeMs
    {
        get => _seekFadeMs;
        set
        {
            if (SetProperty(ref _seekFadeMs, value))
            {
                SubmitSmoothing();
            }
        }
    }

    [RelayCommand]
    private void ResetClock() => Submit(new ResetShowClock());

    [RelayCommand]
    private void ResetGestures()
    {
        SaveBindings(HotkeyConfig.Default);
        RecordingRow = null;
    }

    [RelayCommand]
    private void SaveRowSettings()
    {
        var settings = new AppSettings(UseFileName, string.IsNullOrWhiteSpace(RowFormat) ? AppSettings.Default.RowFormat : RowFormat, CurrentSmoothing(), _defaultEndAction);
        RowFormat = settings.RowFormat;
        _settingsStore.Save(settings);
        _rowSettingsApplied?.Invoke(settings);
        RowSettingsStatus = "формат строк сохранён";
    }

    public void BeginRecord(GestureRow row)
    {
        foreach (var other in Gestures)
        {
            if (other.IsRecording)
            {
                other.IsRecording = false;
            }
        }
        row.Error = string.Empty;
        row.IsRecording = true;
        RecordingRow = row;
    }

    public void CancelRecord()
    {
        if (RecordingRow is { } row)
        {
            row.IsRecording = false;
        }
        RecordingRow = null;
    }

    public void RecordGesture(string gesture)
    {
        if (RecordingRow is not { } row)
        {
            return;
        }
        row.IsRecording = false;
        RecordingRow = null;
        var error = TrySetGesture(row, gesture);
        row.Error = error ?? string.Empty;
    }

    public string? TrySetGesture(GestureRow row, string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture))
        {
            return "пустой жест";
        }
        if (HotkeyService.IsSystemReserved(gesture))
        {
            return "жест занят системой macOS";
        }
        if (_hotkeys.ConflictFor(row.Action, gesture) is { } taken)
        {
            return $"жест уже у «{HotkeyLabels.Label(taken)}»";
        }
        var bindings = Gestures.Select(g => new HotkeyBinding(g.Action == row.Action ? gesture : g.Gesture, g.Action));
        SaveBindings(new HotkeyConfig([.. bindings]));
        return null;
    }

    public void Dispose() => _subscription.Dispose();

    private void Submit(Command command) => _bus.Submit(_client, Interlocked.Increment(ref _seq), command);

    private Smoothing CurrentSmoothing() => new(
        _smoothingEnabled,
        TimeSpan.FromMilliseconds(_manualCrossfadeMs),
        TimeSpan.FromMilliseconds(_autoCrossfadeMs),
        TimeSpan.FromMilliseconds(_startFadeMs),
        TimeSpan.FromMilliseconds(_stopFadeMs),
        TimeSpan.FromMilliseconds(_seekFadeMs));

    private void SubmitSmoothing()
    {
        var smoothing = CurrentSmoothing();
        _settingsStore.Save(new AppSettings(UseFileName, RowFormat, smoothing, _defaultEndAction));
        Submit(new SetSmoothing(smoothing));
    }

    private void SubmitEndAction()
    {
        _settingsStore.Save(new AppSettings(UseFileName, RowFormat, CurrentSmoothing(), _defaultEndAction));
        Submit(new SetDefaultEndAction(_defaultEndAction));
    }

    private void ApplySmoothing(Smoothing smoothing)
    {
        _smoothingEnabled = smoothing.Enabled;
        _manualCrossfadeMs = smoothing.ManualCrossfade.TotalMilliseconds;
        _autoCrossfadeMs = smoothing.AutoCrossfade.TotalMilliseconds;
        _startFadeMs = smoothing.StartFade.TotalMilliseconds;
        _stopFadeMs = smoothing.StopFade.TotalMilliseconds;
        _seekFadeMs = smoothing.SeekFade.TotalMilliseconds;
        OnPropertyChanged(nameof(SmoothingEnabled));
        OnPropertyChanged(nameof(ManualCrossfadeMs));
        OnPropertyChanged(nameof(AutoCrossfadeMs));
        OnPropertyChanged(nameof(StartFadeMs));
        OnPropertyChanged(nameof(StopFadeMs));
        OnPropertyChanged(nameof(SeekFadeMs));
    }

    private void SaveBindings(HotkeyConfig config)
    {
        config.Save(_hotkeysPath);
        _hotkeys.Reset(config);
        RefreshGestures();
    }

    private void RefreshGestures()
    {
        Gestures.Clear();
        foreach (var action in GestureActions)
        {
            Gestures.Add(new GestureRow(action, HotkeyLabels.Label(action), _hotkeys.GestureFor(action)));
        }
    }

    private void Apply(StateEvent e)
    {
        if (e is MixerDelta delta)
        {
            Post(() => ApplyMixer(delta.State));
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

    private void ApplyMixer(MixerState state)
    {
        _panicFadeMs = state.PanicFade.TotalMilliseconds;
        OnPropertyChanged(nameof(PanicFadeMs));
        ApplySmoothing(state.Smoothing);
    }

    public sealed class GestureRow(string action, string label, string gesture) : ObservableObject
    {
        private string _gesture = gesture;
        private string _error = string.Empty;
        private bool _isRecording;

        public string Action { get; } = action;

        public string Label { get; } = label;

        public string Gesture
        {
            get => _gesture;
            set => SetProperty(ref _gesture, value);
        }

        public string Error
        {
            get => _error;
            set
            {
                if (SetProperty(ref _error, value))
                {
                    OnPropertyChanged(nameof(HasError));
                }
            }
        }

        public bool HasError => _error.Length > 0;

        public bool IsRecording
        {
            get => _isRecording;
            set
            {
                if (SetProperty(ref _isRecording, value))
                {
                    OnPropertyChanged(nameof(RecordText));
                }
            }
        }

        public string RecordText => IsRecording ? "нажмите…" : Gesture;
    }
}
