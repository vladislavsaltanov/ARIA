namespace Aria.Audio;

using System.Collections.Immutable;
using Aria.Core.Model;

public sealed class WaveformScanner
{
    private const int MaxBlockFrames = 8192;

    private struct Bucket
    {
        public float Min = float.MaxValue;
        public float Max = float.MinValue;
        public int Filled;

        public Bucket()
        {
        }

        public void AccumulateFrame(float[] buffer, int offset, int channels)
        {
            var sum = 0f;
            for (var channel = 0; channel < channels; channel++)
            {
                sum += buffer[offset + channel];
            }
            var value = sum / channels;
            if (value < Min)
            {
                Min = value;
            }
            if (value > Max)
            {
                Max = value;
            }
            Filled++;
        }

        public PeakPoint Flush()
        {
            var point = new PeakPoint(Min, Max);
            Min = float.MaxValue;
            Max = float.MinValue;
            Filled = 0;
            return point;
        }
    }

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
        var bucket = new Bucket();

        while (source.ReadFrames(buffer) is var read && read > 0)
        {
            for (var frame = 0; frame < read; frame++)
            {
                bucket.AccumulateFrame(buffer, frame * channels, channels);
                if (bucket.Filled == framesPerBucket)
                {
                    points.Add(bucket.Flush());
                }
            }
        }

        if (bucket.Filled > 0)
        {
            points.Add(bucket.Flush());
        }

        return new WaveformPeaks(trackId, pointsPerSecond, sampleRate, [.. points]);
    }
}