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

    private readonly Action<Command> _submit;
    private readonly TrackId _trackId;
    private readonly EntryId? _entryId;
    private double _gainDb;
    private double _pan;
    private bool _inheritTrackSettings;
    private double _normalizeTargetLufs;
    private bool _normalizeEnabled;
    private double? _measuredLufs;
    private readonly bool _globalNormalizeEnabled;
    private readonly Func<double?>? _measureReader;

    public TrackAudioVm(Action<Command> submit, TrackId trackId, TrackAudioSettings? initial = null, EntryId? entryId = null, double normalizeTargetLufs = NormalizeDefaultLufs, bool globalNormalizeEnabled = true, Func<double?>? measureReader = null)
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
        _gainDb = audio.GainDb;
        _pan = audio.Pan;
        EqBands = [.. audio.Eq.Bands.Select((b, i) => new AudioSectionVm.EqBandVm(AudioSectionVm.BandLabel(i, b.FrequencyHz), b.FrequencyHz, b.GainDb, SubmitCurrent))];
        NormalizeRequest = id => _submit(new NormalizeTrack(id));
    }

    public IReadOnlyList<AudioSectionVm.EqBandVm> EqBands { get; }

    public bool IsEntry => _entryId is not null;

    public double NormalizeTargetLufs => _normalizeTargetLufs;

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
            var offset = _normalizeTargetLufs - measured;
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
        _measuredLufs);

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
