namespace Aria.App.ViewModels;

using System.Collections.ObjectModel;
using Aria.App.Services;
using Aria.Core.Commands;
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
    private readonly ClientId _client = new("desktop");
    private readonly HotkeyService _hotkeys;
    private readonly string _hotkeysPath;
    private readonly AppSettingsStore _settingsStore;
    private readonly Action<AppSettings>? _rowSettingsApplied;
    private readonly IDisposable _subscription;
    private double _panicFadeMs = 100;
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
        Action<AppSettings>? rowSettingsApplied = null)
    {
        _bus = bus;
        _hotkeys = hotkeys;
        _hotkeysPath = hotkeysPath;
        _settingsStore = settingsStore;
        _rowSettingsApplied = rowSettingsApplied;
        _subscription = bus.Subscribe(Apply);
        var settings = settingsStore.Load();
        UseFileName = settings.UseFileName;
        RowFormat = settings.RowFormat;
        RefreshGestures();
        ApplyMixer(bus.Snapshot().Mixer);
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
        var settings = new AppSettings(UseFileName, string.IsNullOrWhiteSpace(RowFormat) ? AppSettings.Default.RowFormat : RowFormat);
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
        switch (e)
        {
            case MixerDelta delta:
                ApplyMixer(delta.State);
                break;
        }
    }

    private void ApplyMixer(MixerState state)
    {
        _panicFadeMs = state.PanicFade.TotalMilliseconds;
        OnPropertyChanged(nameof(PanicFadeMs));
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
