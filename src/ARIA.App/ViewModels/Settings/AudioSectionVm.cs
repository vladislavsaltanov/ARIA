namespace Aria.App.ViewModels.Settings;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.State;
using CommunityToolkit.Mvvm.ComponentModel;

public sealed partial class AudioSectionVm : ObservableObject
{
    private readonly Action<Command> _submit;
    private bool _limiterEnabled = LimiterSettings.Default.Enabled;
    private double _limiterThresholdDb = LimiterSettings.Default.ThresholdDb;
    private double _limiterReleaseMs = LimiterSettings.Default.ReleaseMs;
    private double _pan;
    private double _hpfHz;
    private bool _mono;
    private double _previewGainDb;
    private bool _previewMuted;

    public AudioSectionVm(Action<Command> submit)
    {
        _submit = submit;
        EqBands = [.. AudioEq.DefaultFrequencies.Select((f, i) => new EqBandVm(BandLabel(i, f), f, 0, SubmitGlobal))];
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
        new LimiterSettings(_limiterEnabled, _limiterThresholdDb, _limiterReleaseMs));

    public void ApplyMixer(MixerState state)
    {
        var global = state.EffectiveGlobal;
        _pan = global.Pan;
        _mono = global.Mono;
        _hpfHz = global.HpfHz;
        _limiterEnabled = global.Limiter.Enabled;
        _limiterThresholdDb = global.Limiter.ThresholdDb;
        _limiterReleaseMs = global.Limiter.ReleaseMs;
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
