namespace Aria.App.ViewModels;

using Aria.App.ViewModels.Settings;
using Aria.Core.Commands;
using Aria.Core.Model;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public sealed partial class TrackAudioVm : ObservableObject
{
    public const double NormalizeDefaultLufs = -16.0;

    public const double DefaultBpm = 120.0;

    private readonly Action<Command> _submit;
    private readonly TrackId _trackId;
    private readonly EntryId? _entryId;
    private double _gainDb;
    private double _pan;
    private bool _hasGrid;
    private double _bpm = DefaultBpm;
    private int _beatsPerBar;
    private double _firstBeatMs;
    private bool _canTakeCurrentPosition;
    private readonly TimeSpan _trackDuration;
    private readonly Func<TimeSpan?>? _currentPosition;
    private bool _inheritTrackSettings;
    private double _normalizeTargetLufs;
    private bool _normalizeEnabled;
    private double? _measuredLufs;
    private bool _ownTargetLufs;
    private double _targetLufs = NormalizeDefaultLufs;
    private readonly bool _globalNormalizeEnabled;
    private readonly Func<double?>? _measureReader;

    public TrackAudioVm(Action<Command> submit, TrackId trackId, TrackAudioSettings? initial = null, EntryId? entryId = null, double normalizeTargetLufs = NormalizeDefaultLufs, bool globalNormalizeEnabled = true, Func<double?>? measureReader = null, BeatGrid? initialGrid = null, TimeSpan trackDuration = default, Func<TimeSpan?>? currentPosition = null)
    {
        _submit = submit;
        _trackId = trackId;
        _entryId = entryId;
        _normalizeTargetLufs = Math.Clamp(normalizeTargetLufs, -36.0, -6.0);
        _globalNormalizeEnabled = globalNormalizeEnabled;
        _measureReader = measureReader;
        var audio = initial ?? TrackAudioSettings.Default;
        _normalizeEnabled = audio.NormalizeEnabled;
        _measuredLufs = audio.MeasuredLufs;
        _ownTargetLufs = audio.NormalizeTargetLufs is { } own;
        _targetLufs = audio.NormalizeTargetLufs ?? NormalizeDefaultLufs;
        _gainDb = audio.GainDb;
        _pan = audio.Pan;
        _trackDuration = trackDuration;
        _currentPosition = currentPosition;
        _hasGrid = initialGrid is not null;
        _bpm = ClampBpm(initialGrid?.Bpm ?? DefaultBpm);
        _beatsPerBar = ClampBeats(initialGrid?.BeatsPerBar ?? 4);
        _firstBeatMs = ClampFirstBeat(initialGrid?.FirstBeat.TotalMilliseconds ?? 0);
        _canTakeCurrentPosition = currentPosition?.Invoke() is not null;
        EqBands = [.. audio.Eq.Bands.Select((b, i) => new AudioSectionVm.EqBandVm(AudioSectionVm.BandLabel(i, b.FrequencyHz), b.FrequencyHz, b.GainDb, SubmitCurrent))];
        NormalizeRequest = id => _submit(new NormalizeTrack(id));
    }

    public IReadOnlyList<AudioSectionVm.EqBandVm> EqBands { get; }

    public bool IsEntry => _entryId is not null;

    public double NormalizeTargetLufs => _normalizeTargetLufs;

    public double EffectiveTargetLufs => _ownTargetLufs ? _targetLufs : _normalizeTargetLufs;

    public bool OwnTargetLufs
    {
        get => _ownTargetLufs;
        set
        {
            if (SetProperty(ref _ownTargetLufs, value))
            {
                OnPropertyChanged(nameof(NormalizeStatus));
                SubmitCurrent();
            }
        }
    }

    public double TargetLufs
    {
        get => _targetLufs;
        set
        {
            if (SetProperty(ref _targetLufs, Math.Clamp(value, -36.0, -6.0)))
            {
                OnPropertyChanged(nameof(NormalizeStatus));
                SubmitCurrent();
            }
        }
    }

    public bool NormalizeEnabled
    {
        get => _normalizeEnabled;
        set
        {
            if (SetProperty(ref _normalizeEnabled, value))
            {
                OnPropertyChanged(nameof(NormalizeStatus));
                SubmitCurrent();
            }
        }
    }

    public void RefreshMeasurement()
    {
        CanTakeCurrentPosition = _currentPosition?.Invoke() is not null;
        if (_measureReader?.Invoke() is { } measured && measured != _measuredLufs)
        {
            _measuredLufs = measured;
            OnPropertyChanged(nameof(NormalizeStatus));
        }
    }

    public string NormalizeStatus
    {
        get
        {
            if (!_globalNormalizeEnabled)
            {
                return "Выключена в настройках Звука";
            }
            if (!_normalizeEnabled)
            {
                return "Нормализация выключена";
            }
            if (_measuredLufs is not { } measured)
            {
                return "Включена, измерение не выполнено";
            }
            var offset = EffectiveTargetLufs - measured;
            return $"Измерено {measured.ToString("F1", CultureInfo.InvariantCulture)} LUFS, поправка {offset:+0.0;-0.0} дБ";
        }
    }

    public Action<TrackId>? NormalizeRequest { get; set; }

    public double GainDb
    {
        get => _gainDb;
        set
        {
            if (SetProperty(ref _gainDb, Math.Clamp(value, -60.0, 12.0)))
            {
                SubmitCurrent();
            }
        }
    }

    public double Pan
    {
        get => _pan;
        set
        {
            if (SetProperty(ref _pan, Math.Clamp(value, -1.0, 1.0)))
            {
                SubmitCurrent();
            }
        }
    }

    public bool HasGrid
    {
        get => _hasGrid;
        set
        {
            if (SetProperty(ref _hasGrid, value))
            {
                _submit(new SetTrackGrid(_trackId, _hasGrid ? CurrentGrid() : null));
            }
        }
    }

    public double Bpm
    {
        get => _bpm;
        set
        {
            if (SetProperty(ref _bpm, ClampBpm(value)))
            {
                SubmitGrid();
            }
        }
    }

    public int BeatsPerBar
    {
        get => _beatsPerBar;
        set
        {
            if (SetProperty(ref _beatsPerBar, ClampBeats(value)))
            {
                SubmitGrid();
            }
        }
    }

    public double FirstBeatMs
    {
        get => _firstBeatMs;
        set
        {
            if (SetProperty(ref _firstBeatMs, ClampFirstBeat(value)))
            {
                SubmitGrid();
            }
        }
    }

    public bool CanTakeCurrentPosition
    {
        get => _canTakeCurrentPosition;
        private set
        {
            if (SetProperty(ref _canTakeCurrentPosition, value))
            {
                TakeCurrentPositionCommand.NotifyCanExecuteChanged();
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanTakeCurrentPosition))]
    private void TakeCurrentPosition()
    {
        if (_currentPosition?.Invoke() is { } position)
        {
            FirstBeatMs = Math.Round(position.TotalMilliseconds);
        }
    }

    private BeatGrid CurrentGrid() => new(_bpm, _beatsPerBar, TimeSpan.FromMilliseconds(_firstBeatMs));

    private void SubmitGrid()
    {
        if (_hasGrid)
        {
            _submit(new SetTrackGrid(_trackId, CurrentGrid()));
        }
    }

    private static double ClampBpm(double value) =>
        Math.Round(Math.Clamp(value, BeatGridValidation.BpmMin, BeatGridValidation.BpmMax), 2);

    private static int ClampBeats(int value) =>
        Math.Clamp(value, BeatGridValidation.BeatsMin, BeatGridValidation.BeatsMax);

    private double ClampFirstBeat(double value)
    {
        var max = _trackDuration > TimeSpan.Zero ? _trackDuration.TotalMilliseconds - 1 : double.MaxValue;
        return Math.Clamp(Math.Round(value), 0, max);
    }

    public bool InheritTrackSettings
    {
        get => _inheritTrackSettings;
        set
        {
            if (SetProperty(ref _inheritTrackSettings, value))
            {
                SubmitCurrent();
            }
        }
    }

    public TrackAudioSettings Current() => new(
        _gainDb,
        _pan,
        new AudioEq([.. EqBands.Select(b => new EqBand(b.FrequencyHz, (float)b.GainDb, 1))]),
        _normalizeEnabled,
        _measuredLufs,
        _ownTargetLufs ? _targetLufs : null);

    [RelayCommand]
    private void Preview() => _submit(new StartPreviewTrack(_trackId));

    [RelayCommand]
    private void StopPreview() => _submit(new StopPreview());

    [RelayCommand]
    private void Normalize()
    {
        if (!_globalNormalizeEnabled)
        {
            return;
        }
        NormalizeEnabled = true;
        NormalizeRequest?.Invoke(_trackId);
    }

    private void SubmitCurrent()
    {
        if (_entryId is { } entry)
        {
            _submit(new SetEntryAudio(entry, _inheritTrackSettings ? null : Current()));
            return;
        }
        _submit(new SetTrackAudio(_trackId, Current()));
    }
}
