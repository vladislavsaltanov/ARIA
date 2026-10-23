namespace Aria.Audio;

public sealed class TestClickGenerator
{
    private const double BurstSeconds = 0.04;
    private const double Frequency = 2000.0;
    private const double Amplitude = 0.5;

    private readonly float[] _burst;
    private readonly int _channels;
    private readonly int _period;

    public TestClickGenerator(int sampleRate, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        _channels = channels;
        _period = sampleRate;
        var length = (int)Math.Round(BurstSeconds * sampleRate);
        var decay = length / 4.0;
        _burst = new float[length];
        for (var index = 0; index < length; index++)
        {
            _burst[index] = (float)(Amplitude * Math.Sin(2.0 * Math.PI * Frequency * index / sampleRate) * Math.Exp(-index / decay));
        }
    }

    public void Mix(Span<float> block, long outputFrame)
    {
        var frames = block.Length / _channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var phase = (outputFrame + frame) % _period;
            if (phase >= _burst.Length)
            {
                continue;
            }
            var value = _burst[phase];
            for (var channel = 0; channel < _channels; channel++)
            {
                block[frame * _channels + channel] += value;
            }
        }
    }
}
