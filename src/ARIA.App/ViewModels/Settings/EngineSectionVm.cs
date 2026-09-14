namespace Aria.App.ViewModels.Settings;

using Aria.App.Services;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.State;
using CommunityToolkit.Mvvm.ComponentModel;

public sealed partial class EngineSectionVm : ObservableObject
{
    private readonly Action<Command> _submit;
    private readonly Func<AppSettings> _snapshot;
    private readonly Action<AppSettings> _save;
    private double _panicFadeMs = 100;
    private bool _smoothingEnabled;
    private double _manualCrossfadeMs;
    private double _autoCrossfadeMs;
    private double _startFadeMs;
    private double _stopFadeMs;
    private double _seekFadeMs;

    public IReadOnlyList<FadeSetting> FadeSettings { get; }

    public EngineSectionVm(Action<Command> submit, Func<AppSettings> snapshot, Action<AppSettings> save)
    {
        _submit = submit;
        _snapshot = snapshot;
        _save = save;
        FadeSettings =
        [
            new("фейд PANIC", 2000, () => PanicFadeMs, v => PanicFadeMs = v, () => true),
            new("кроссфейд вручную", 2000, () => ManualCrossfadeMs, v => ManualCrossfadeMs = v, () => SmoothingEnabled),
            new("кроссфейд по очереди", 3000, () => AutoCrossfadeMs, v => AutoCrossfadeMs = v, () => SmoothingEnabled),
            new("скорость запуска", 2000, () => StartFadeMs, v => StartFadeMs = v, () => SmoothingEnabled),
            new("скорость затухания", 2000, () => StopFadeMs, v => StopFadeMs = v, () => SmoothingEnabled),
            new("кроссфейд перемотки", 2000, () => SeekFadeMs, v => SeekFadeMs = v, () => SmoothingEnabled),
        ];
    }

    public double PanicFadeMs
    {
        get => _panicFadeMs;
        set
        {
            if (SetProperty(ref _panicFadeMs, value))
            {
                _submit(new SetPanicFade(TimeSpan.FromMilliseconds(value)));
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

    public Smoothing CurrentSmoothing() => new(
        _smoothingEnabled,
        TimeSpan.FromMilliseconds(_manualCrossfadeMs),
        TimeSpan.FromMilliseconds(_autoCrossfadeMs),
        TimeSpan.FromMilliseconds(_startFadeMs),
        TimeSpan.FromMilliseconds(_stopFadeMs),
        TimeSpan.FromMilliseconds(_seekFadeMs));

    public void ApplySmoothing(Smoothing smoothing)
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
        RefreshFades();
    }

    public void ApplyMixer(MixerState state)
    {
        _panicFadeMs = state.PanicFade.TotalMilliseconds;
        OnPropertyChanged(nameof(PanicFadeMs));
        ApplySmoothing(state.Smoothing);
    }

    private void SubmitSmoothing()
    {
        var smoothing = CurrentSmoothing();
        _save(_snapshot() with { Smoothing = smoothing });
        _submit(new SetSmoothing(smoothing));
        RefreshFades();
    }

    private void RefreshFades()
    {
        foreach (var fade in FadeSettings)
        {
            fade.Refresh();
        }
    }
}
