namespace Aria.App.ViewModels.Settings;

using Aria.App.Services;
using Aria.Core.Commands;
using Aria.Core.Model;
using CommunityToolkit.Mvvm.ComponentModel;

public sealed partial class PlaybackSectionVm : ObservableObject
{
    private readonly Action<Command> _submit;
    private readonly Func<AppSettings> _snapshot;
    private readonly Action<AppSettings> _save;
    private readonly Action<AppSettings>? _applied;
    private EndAction _defaultEndAction;
    private bool _meterSmoothingEnabled;
    private int _meterSmoothingReleaseMs;

    public PlaybackSectionVm(Action<Command> submit, Func<AppSettings> snapshot, Action<AppSettings> save, EndAction initial, MeterSmoothing? smoothing = null, Action<AppSettings>? applied = null)
    {
        _submit = submit;
        _snapshot = snapshot;
        _save = save;
        _defaultEndAction = initial;
        var effective = smoothing ?? MeterSmoothing.Default;
        _meterSmoothingEnabled = effective.Enabled;
        _meterSmoothingReleaseMs = effective.ReleaseMs;
        _applied = applied;
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

    public EndAction CurrentEndAction => _defaultEndAction;

    public bool MeterSmoothingEnabled
    {
        get => _meterSmoothingEnabled;
        set
        {
            if (SetProperty(ref _meterSmoothingEnabled, value))
            {
                SaveMeterSmoothing();
            }
        }
    }

    public int MeterSmoothingReleaseMs
    {
        get => _meterSmoothingReleaseMs;
        set
        {
            var clamped = Math.Clamp(value, 0, 2000);
            if (SetProperty(ref _meterSmoothingReleaseMs, clamped))
            {
                SaveMeterSmoothing();
            }
        }
    }

    private void SaveMeterSmoothing()
    {
        var settings = _snapshot() with { MeterSmoothing = new MeterSmoothing(_meterSmoothingEnabled, _meterSmoothingReleaseMs) };
        _save(settings);
        _applied?.Invoke(settings);
    }

    private void SubmitEndAction()
    {
        _save(_snapshot() with { DefaultEndAction = _defaultEndAction });
        _submit(new SetDefaultEndAction(_defaultEndAction));
    }
}
