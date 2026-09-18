namespace Aria.Audio;

using Aria.Core.Playback;

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
    private readonly int _burstFrames;
    private readonly double _decay;
    private double _framesPerBeat;
    private double _gain;
    private int _beatsPerBar;
    private double _offsetFrames;
    private double _untilClick;
    private long _clickIndex;
    private long _totalFrames;
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
        _burstFrames = Math.Max(1, (int)Math.Round(BurstSeconds * sampleRate));
        _decay = _burstFrames / 4.0;
        ApplySettings(settings);
        Reset(0);
        _totalFrames = 0;
    }

    public int Channels => _channels;

    public int SampleRate => _sampleRate;

    public void UpdateSettings(ClickSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ApplySettings(settings);
        Reset(_totalFrames);
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
        _totalFrames += frames;
        return frames;
    }

    public void Seek(long frameIndex)
    {
        Reset(frameIndex);
        _totalFrames = frameIndex;
    }

    private void ApplySettings(ClickSettings settings)
    {
        _framesPerBeat = 60.0 / settings.Bpm * _sampleRate;
        _gain = Math.Pow(10.0, settings.GainDb / 20.0);
        _beatsPerBar = settings.BeatsPerBar;
        _offsetFrames = settings.OffsetMs / 1000.0 * _sampleRate;
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
