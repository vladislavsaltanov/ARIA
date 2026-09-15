namespace Aria.App.ViewModels;

using Aria.App.ViewModels.Settings;
using Aria.Core.Commands;
using Aria.Core.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public sealed partial class TrackAudioVm : ObservableObject
{
    public const double NormalizeTargetLufs = -16.0;

    private readonly Action<Command> _submit;
    private readonly TrackId _trackId;
    private readonly EntryId? _entryId;
    private double _gainDb;
    private double _pan;
    private bool _inheritTrackSettings;

    public TrackAudioVm(Action<Command> submit, TrackId trackId, TrackAudioSettings? initial = null, EntryId? entryId = null)
    {
        _submit = submit;
        _trackId = trackId;
        _entryId = entryId;
        var audio = initial ?? TrackAudioSettings.Default;
        _gainDb = audio.GainDb;
        _pan = audio.Pan;
        EqBands = [.. audio.Eq.Bands.Select((b, i) => new AudioSectionVm.EqBandVm(AudioSectionVm.BandLabel(i, b.FrequencyHz), b.FrequencyHz, b.GainDb, SubmitCurrent))];
        NormalizeRequest = (id, target) => _submit(new NormalizeTrackToLufs(id, target));
    }

    public IReadOnlyList<AudioSectionVm.EqBandVm> EqBands { get; }

    public bool IsEntry => _entryId is not null;

    public Action<TrackId, double>? NormalizeRequest { get; set; }

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
        new AudioEq([.. EqBands.Select(b => new EqBand(b.FrequencyHz, (float)b.GainDb, 1))]));

    [RelayCommand]
    private void Preview() => _submit(new StartPreviewTrack(_trackId));

    [RelayCommand]
    private void StopPreview() => _submit(new StopPreview());

    [RelayCommand]
    private void Normalize() => NormalizeRequest?.Invoke(_trackId, NormalizeTargetLufs);

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
