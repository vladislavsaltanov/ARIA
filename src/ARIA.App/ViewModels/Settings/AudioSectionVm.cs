namespace Aria.App.ViewModels.Settings;

using Aria.App.Services;
using Aria.Audio;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.State;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public sealed partial class AudioSectionVm : ObservableObject
{
    private readonly Action<Command> _submit;
    private bool _limiterEnabled = LimiterSettings.Default.Enabled;
    private double _limiterThresholdDb = LimiterSettings.Default.ThresholdDb;
    private double _limiterReleaseMs = LimiterSettings.Default.ReleaseMs;
    private double _pan;
    private double _hpfHz;
    private bool _mono;
    private double _normalizeTargetLufs = -16.0;
    private bool _normalizeEnabled;
    private double _zoneGreenDb = -15.0;
    private double _zoneYellowDb = -9.0;
    private double _zoneRedDb = -5.0;
    private double _previewGainDb;
    private bool _previewMuted;
    private readonly Func<ProjectId?>? _activeProject;
    private readonly AudioOutputService? _outputs;
    private bool _measuringProject;
    private string _normalizeProjectStatus = "Не измерялся";

    public AudioSectionVm(Action<Command> submit, Func<ProjectId?>? activeProject = null, AudioOutputService? outputs = null)
    {
        _submit = submit;
        _activeProject = activeProject;
        _outputs = outputs;
        EqBands = [.. AudioEq.DefaultFrequencies.Select((f, i) => new EqBandVm(BandLabel(i, f), f, 0, SubmitGlobal))];
        if (_outputs is not null)
        {
            _outputs.Changed += OnOutputsChanged;
        }
    }

    public void Dispose()
    {
        if (_outputs is not null)
        {
            _outputs.Changed -= OnOutputsChanged;
        }
    }

    public IReadOnlyList<OutputDevice> Outputs => _outputs?.Devices ?? [];

    public string SelectedOutputId
    {
        get => _outputs?.SelectedId ?? string.Empty;
        set
        {
            if (_outputs?.Select(value) == true)
            {
                OnPropertyChanged();
                OnPropertyChanged(nameof(OutputStatus));
            }
        }
    }

    public string OutputStatus => _outputs?.Status ?? string.Empty;

    public string SelectedPreviewOutputId
    {
        get => _outputs?.SelectedPreviewId ?? string.Empty;
        set
        {
            if (_outputs?.SelectPreview(value) == true)
            {
                OnPropertyChanged();
                OnPropertyChanged(nameof(PreviewOutputStatus));
            }
        }
    }

    public string PreviewOutputStatus => _outputs?.PreviewStatus ?? string.Empty;

    private void OnOutputsChanged()
    {
        OnPropertyChanged(nameof(Outputs));
        OnPropertyChanged(nameof(SelectedOutputId));
        OnPropertyChanged(nameof(OutputStatus));
        OnPropertyChanged(nameof(SelectedPreviewOutputId));
        OnPropertyChanged(nameof(PreviewOutputStatus));
    }

    public string NormalizeProjectStatus
    {
        get => _normalizeProjectStatus;
        private set => SetProperty(ref _normalizeProjectStatus, value);
    }

    [RelayCommand]
    private void MeasureProject()
    {
        if (_activeProject?.Invoke() is not { } id)
        {
            return;
        }
        _measuringProject = true;
        NormalizeProjectStatus = "Замер выполняется…";
        _submit(new NormalizeProject(id));
    }

    public void OnShow()
    {
        if (!_measuringProject)
        {
            return;
        }
        _measuringProject = false;
        NormalizeProjectStatus = "Готово";
    }

    public IReadOnlyList<EqBandVm> EqBands { get; }

    public bool LimiterEnabled
    {
        get => _limiterEnabled;
        set
        {
            if (SetProperty(ref _limiterEnabled, value))
            {
                SubmitGlobal();
            }
        }
    }

    public double LimiterThresholdDb
    {
        get => _limiterThresholdDb;
        set
        {
            if (SetProperty(ref _limiterThresholdDb, Math.Clamp(value, -24.0, 0.0)))
            {
                SubmitGlobal();
            }
        }
    }

    public double LimiterReleaseMs
    {
        get => _limiterReleaseMs;
        set
        {
            if (SetProperty(ref _limiterReleaseMs, Math.Clamp(value, 10.0, 1000.0)))
            {
                SubmitGlobal();
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
                SubmitGlobal();
            }
        }
    }

    public double HpfHz
    {
        get => _hpfHz;
        set
        {
            if (SetProperty(ref _hpfHz, Math.Clamp(value, 0.0, 400.0)))
            {
                SubmitGlobal();
            }
        }
    }

    public bool Mono
    {
        get => _mono;
        set
        {
            if (SetProperty(ref _mono, value))
            {
                SubmitGlobal();
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
                SubmitGlobal();
            }
        }
    }

    public double NormalizeTargetLufs
    {
        get => _normalizeTargetLufs;
        set
        {
            if (SetProperty(ref _normalizeTargetLufs, Math.Clamp(value, -36.0, -6.0)))
            {
                SubmitGlobal();
            }
        }
    }

    public double ZoneGreenDb
    {
        get => _zoneGreenDb;
        set
        {
            if (SetProperty(ref _zoneGreenDb, Math.Clamp(value, -60.0, 0.0)))
            {
                SubmitGlobal();
            }
        }
    }

    public double ZoneYellowDb
    {
        get => _zoneYellowDb;
        set
        {
            if (SetProperty(ref _zoneYellowDb, Math.Clamp(value, -60.0, 0.0)))
            {
                SubmitGlobal();
            }
        }
    }

    public double ZoneRedDb
    {
        get => _zoneRedDb;
        set
        {
            if (SetProperty(ref _zoneRedDb, Math.Clamp(value, -60.0, 0.0)))
            {
                SubmitGlobal();
            }
        }
    }

    public double PreviewGainDb
    {
        get => _previewGainDb;
        set
        {
            if (SetProperty(ref _previewGainDb, Math.Clamp(value, -80.0, 12.0)))
            {
                _submit(new SetPreviewGain(_previewGainDb));
            }
        }
    }

    public bool PreviewMuted
    {
        get => _previewMuted;
        set
        {
            if (SetProperty(ref _previewMuted, value))
            {
                _submit(new SetPreviewMuted(value));
            }
        }
    }

    public GlobalAudioSettings CurrentGlobal() => new(
        _pan,
        _mono,
        _hpfHz,
        new AudioEq([.. EqBands.Select(b => new EqBand(b.FrequencyHz, (float)b.GainDb, 1))]),
        new LimiterSettings(_limiterEnabled, _limiterThresholdDb, _limiterReleaseMs),
        _normalizeTargetLufs,
        new LufsMeterZones(_zoneGreenDb, _zoneYellowDb, _zoneRedDb),
        _normalizeEnabled);

    public void ApplyMixer(MixerState state)
    {
        var global = state.EffectiveGlobal;
        _pan = global.Pan;
        _mono = global.Mono;
        _hpfHz = global.HpfHz;
        _limiterEnabled = global.Limiter.Enabled;
        _limiterThresholdDb = global.Limiter.ThresholdDb;
        _limiterReleaseMs = global.Limiter.ReleaseMs;
        _normalizeTargetLufs = global.NormalizeTargetLufs;
        _normalizeEnabled = global.NormalizeEnabled;
        _zoneGreenDb = global.EffectiveZones.GreenDb;
        _zoneYellowDb = global.EffectiveZones.YellowDb;
        _zoneRedDb = global.EffectiveZones.RedDb;
        for (var i = 0; i < EqBands.Count && i < global.Eq.Bands.Length; i++)
        {
            EqBands[i].SetGainSilently(global.Eq.Bands[i].GainDb);
        }
        OnPropertyChanged(nameof(Pan));
        OnPropertyChanged(nameof(Mono));
        OnPropertyChanged(nameof(HpfHz));
        OnPropertyChanged(nameof(LimiterEnabled));
        OnPropertyChanged(nameof(LimiterThresholdDb));
        OnPropertyChanged(nameof(LimiterReleaseMs));
        OnPropertyChanged(nameof(NormalizeTargetLufs));
        OnPropertyChanged(nameof(NormalizeEnabled));
        OnPropertyChanged(nameof(ZoneGreenDb));
        OnPropertyChanged(nameof(ZoneYellowDb));
        OnPropertyChanged(nameof(ZoneRedDb));
    }

    private void SubmitGlobal() => _submit(new SetGlobalAudio(CurrentGlobal()));

    public static string BandLabel(int index, float frequency) => index switch
    {
        3 => "1 кГц",
        4 => "2,5 кГц",
        5 => "6,3 кГц",
        6 => "12 кГц",
        _ => frequency.ToString("0"),
    };

    public sealed partial class EqBandVm : ObservableObject
    {
        private readonly Action _changed;
        private double _gainDb;

        public EqBandVm(string label, float frequencyHz, double gainDb, Action changed)
        {
            Label = label;
            FrequencyHz = frequencyHz;
            _gainDb = gainDb;
            _changed = changed;
        }

        public string Label { get; }

        public float FrequencyHz { get; }

        public double GainDb
        {
            get => _gainDb;
            set
            {
                var clamped = Math.Clamp(value, -15.0, 15.0);
                if (SetProperty(ref _gainDb, clamped))
                {
                    _changed();
                }
            }
        }

        public void SetGainSilently(double gainDb)
        {
            SetProperty(ref _gainDb, gainDb);
        }
    }
}
