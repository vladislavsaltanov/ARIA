namespace Aria.Audio;

public interface IAudioSink
{
    int SampleRate { get; }

    int Channels { get; }

    int Write(ReadOnlySpan<float> samples);

    void Flush();
}

public sealed class NullSink(int sampleRate, int channels) : IAudioSink
{
    public int SampleRate { get; } = sampleRate;

    public int Channels { get; } = channels;

    public int Write(ReadOnlySpan<float> samples) => samples.Length;

    public void Flush()
    {
    }
}

public sealed class CapturingSink(int sampleRate, int channels) : IAudioSink
{
    private readonly object _gate = new();
    private readonly List<float[]> _blocks = [];
    private int _totalFrames;

    public int SampleRate { get; } = sampleRate;

    public int Channels { get; } = channels;

    public IReadOnlyList<float[]> Blocks
    {
        get
        {
            lock (_gate)
            {
                return [.. _blocks];
            }
        }
    }

    public int TotalFrames
    {
        get
        {
            lock (_gate)
            {
                return _totalFrames;
            }
        }
    }

    public int Write(ReadOnlySpan<float> samples)
    {
        lock (_gate)
        {
            _blocks.Add(samples.ToArray());
            _totalFrames += samples.Length / Channels;
            return samples.Length;
        }
    }

    public void Flush()
    {
    }
}
