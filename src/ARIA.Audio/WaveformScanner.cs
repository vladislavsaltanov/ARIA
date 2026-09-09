namespace Aria.Audio;

using System.Collections.Immutable;
using Aria.Core.Model;

public sealed class WaveformScanner
{
    private const int MaxBlockFrames = 8192;

    private readonly MiniaudioSourceFactory _factory;

    public WaveformScanner(MiniaudioSourceFactory factory)
    {
        _factory = factory;
    }

    public WaveformPeaks Scan(string filePath, TrackId trackId, int pointsPerSecond)
    {
        var source = _factory.Open(filePath, TimeSpan.Zero, null);
        if (source is null)
        {
            throw new FileNotFoundException(filePath);
        }
        using var scope = (IDisposable)source;

        var channels = source.Channels;
        var sampleRate = source.SampleRate;
        var framesPerBucket = Math.Max(1, sampleRate / Math.Max(1, pointsPerSecond));
        var blockFrames = Math.Clamp(sampleRate / 4, 1, MaxBlockFrames);
        var buffer = new float[blockFrames * channels];
        var points = new List<PeakPoint>();
        var min = float.MaxValue;
        var max = float.MinValue;
        var filled = 0;

        while (source.ReadFrames(buffer) is var read && read > 0)
        {
            for (var frame = 0; frame < read; frame++)
            {
                var sum = 0f;
                var offset = frame * channels;
                for (var channel = 0; channel < channels; channel++)
                {
                    sum += buffer[offset + channel];
                }
                var value = sum / channels;
                if (value < min)
                {
                    min = value;
                }
                if (value > max)
                {
                    max = value;
                }
                filled++;
                if (filled == framesPerBucket)
                {
                    points.Add(new PeakPoint(min, max));
                    min = float.MaxValue;
                    max = float.MinValue;
                    filled = 0;
                }
            }
        }

        if (filled > 0)
        {
            points.Add(new PeakPoint(min, max));
        }

        return new WaveformPeaks(trackId, pointsPerSecond, sampleRate, [.. points]);
    }
}