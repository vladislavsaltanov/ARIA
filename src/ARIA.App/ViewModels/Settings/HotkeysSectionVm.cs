namespace Aria.App.ViewModels.Settings;

using System.Collections.ObjectModel;
using Aria.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public sealed partial class HotkeysSectionVm : ObservableObject
{
    public static readonly IReadOnlyList<string> GestureActions =
    [
        "play", "pause", "panic", "next", "replay", "lock", "toggle-script", "reset-clock",
    ];

    private readonly HotkeyService _hotkeys;
    private readonly string _hotkeysPath;

    [ObservableProperty]
    private GestureRow? recordingRow;

    public ObservableCollection<GestureRow> Gestures { get; } = [];

    public HotkeysSectionVm(HotkeyService hotkeys, string hotkeysPath)
    {
        _hotkeys = hotkeys;
        _hotkeysPath = hotkeysPath;
        RefreshGestures();
    }

    [RelayCommand]
    private void ResetGestures()
    {
        SaveBindings(HotkeyConfig.Default);
        RecordingRow = null;
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
