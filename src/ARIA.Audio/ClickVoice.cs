namespace Aria.Audio;

public sealed record ClickSettings
{
    public ClickSettings(double bpm, int beatsPerBar, double gainDb, double offsetMs)
    {
        if (bpm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bpm));
        }
        if (beatsPerBar < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(beatsPerBar));
        }
        Bpm = bpm;
        BeatsPerBar = beatsPerBar;
        GainDb = gainDb;
        OffsetMs = offsetMs;
    }

    public double Bpm { get; init; }

    public int BeatsPerBar { get; init; }

    public double GainDb { get; init; }

    public double OffsetMs { get; init; }
}

public sealed class ClickVoice : ISampleSource
{
    private const double TwoPi = Math.PI * 2.0;
    private const double BurstSeconds = 0.04;
    private const double AccentFrequency = 2000.0;
    private const double BeatFrequency = 1000.0;
    private const double AccentAmplitude = 1.0;
    private const double BeatAmplitude = 0.5;

    private readonly int _channels;
    private readonly int _sampleRate;
    private readonly double _framesPerBeat;
    private readonly double _gain;
    private readonly int _beatsPerBar;
    private readonly double _offsetFrames;
    private readonly int _burstFrames;
    private readonly double _decay;
    private ClickSettings _settings;
    private double _untilClick;
    private long _clickIndex;
    private int _burstTaken;
    private double _burstFrequency;
    private double _burstAmplitude;

    public ClickVoice(int channels, int sampleRate, ClickSettings settings)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentNullException.ThrowIfNull(settings);
        _channels = channels;
        _sampleRate = sampleRate;
        _settings = settings;
        _framesPerBeat = 60.0 / settings.Bpm * sampleRate;
        _gain = Math.Pow(10.0, settings.GainDb / 20.0);
        _beatsPerBar = settings.BeatsPerBar;
        _offsetFrames = settings.OffsetMs / 1000.0 * sampleRate;
        _burstFrames = Math.Max(1, (int)Math.Round(BurstSeconds * sampleRate));
        _decay = _burstFrames / 4.0;
        Reset(0);
    }

    public int Channels => _channels;

    public int SampleRate => _sampleRate;

    public void UpdateSettings(ClickSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    public int ReadFrames(Span<float> destination)
    {
        var frames = destination.Length / _channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var value = 0.0;
            if (_untilClick <= 0)
            {
                var accent = _clickIndex % _beatsPerBar == 0;
                _burstFrequency = accent ? AccentFrequency : BeatFrequency;
                _burstAmplitude = accent ? AccentAmplitude : BeatAmplitude;
                _burstTaken = 0;
                _clickIndex++;
                _untilClick += _framesPerBeat;
            }
            if (_burstTaken < _burstFrames)
            {
                var t = _burstTaken;
                value = _gain * _burstAmplitude * Math.Sin(TwoPi * _burstFrequency * t / _sampleRate) * Math.Exp(-t / _decay);
                _burstTaken++;
            }
            var sample = (float)value;
            for (var channel = 0; channel < _channels; channel++)
            {
                destination[frame * _channels + channel] = sample;
            }
            _untilClick -= 1.0;
        }
        return frames;
    }

    public void Seek(long frameIndex)
    {
        Reset(frameIndex);
    }

    private void Reset(long frameIndex)
    {
        _untilClick = _offsetFrames - frameIndex;
        _clickIndex = 0;
        while (_untilClick < 0)
        {
            _untilClick += _framesPerBeat;
            _clickIndex++;
        }
        _burstTaken = _burstFrames;
    }
}
