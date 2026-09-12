namespace Aria.Audio;

public interface ISampleSource
{
    int Channels { get; }

    int SampleRate { get; }

    int ReadFrames(Span<float> destination);

    void Seek(long frameIndex);
}

public sealed class SilenceSource : ISampleSource
{
    public SilenceSource(int channels, int sampleRate)
    {
        Channels = channels;
        SampleRate = sampleRate;
    }

    public int Channels { get; }

    public int SampleRate { get; }

    public int ReadFrames(Span<float> destination)
    {
        destination.Clear();
        return destination.Length / Channels;
    }

    public void Seek(long frameIndex)
    {
    }
}

public sealed class SineSource : ISampleSource
{
    private const double TwoPi = Math.PI * 2.0;

    private readonly double _frequency;
    private readonly double _amplitude;
    private readonly double _delta;
    private double _phase;

    public SineSource(int channels, int sampleRate, double frequency, double amplitude)
    {
        Channels = channels;
        SampleRate = sampleRate;
        _frequency = frequency;
        _amplitude = amplitude;
        _delta = TwoPi * frequency / sampleRate;
    }

    public int Channels { get; }

    public int SampleRate { get; }

    public double Frequency => _frequency;

    public int ReadFrames(Span<float> destination)
    {
        var frames = destination.Length / Channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var value = (float)(_amplitude * Math.Sin(_phase));
            _phase += _delta;
            if (_phase > TwoPi)
            {
                _phase -= TwoPi;
            }
            for (var channel = 0; channel < Channels; channel++)
            {
                destination[frame * Channels + channel] = value;
            }
        }
        return frames;
    }

    public void Seek(long frameIndex)
    {
        var phase = frameIndex * _delta % TwoPi;
        _phase = phase < 0 ? phase + TwoPi : phase;
    }
}
