namespace Aria.Audio.Tests;

using System.Collections.Immutable;
using Aria.Core.Model;

public sealed class SyntheticSourceFactory(int sampleRate, int channels = 2) : ISourceFactory
{
    public ISampleSource? Open(string filePath, TimeSpan cueIn, TimeSpan? cueOut)
        => Path.GetFileName(filePath) switch
        {
            "sine.flac" => new SineSource(channels, sampleRate, 440.0, 0.5),
            "broken.flac" => null,
            "finite.flac" => new FiniteSource(channels, sampleRate, 440.0, 0.5, (int)(0.5 * sampleRate)),
            _ => null,
        };

    private sealed class FiniteSource : ISampleSource
    {
        private readonly SineSource _inner;
        private readonly int _totalFrames;
        private int _remaining;

        public FiniteSource(int channels, int sampleRate, double frequency, double amplitude, int totalFrames)
        {
            _inner = new SineSource(channels, sampleRate, frequency, amplitude);
            _totalFrames = totalFrames;
            Channels = channels;
            SampleRate = sampleRate;
            _remaining = totalFrames;
        }

        public int Channels { get; }

        public int SampleRate { get; }

        public int ReadFrames(Span<float> destination)
        {
            var frames = Math.Min(_remaining, destination.Length / Channels);
            if (frames <= 0)
            {
                return 0;
            }
            var read = _inner.ReadFrames(destination.Slice(0, frames * Channels));
            _remaining -= read;
            return read;
        }

        public void Seek(long frameIndex)
        {
            var clamped = Math.Clamp(frameIndex, 0, _totalFrames);
            _inner.Seek(clamped);
            _remaining = _totalFrames - (int)clamped;
        }
    }
}

public static class TestTracks
{
    public static Track Track(string file, TimeSpan? duration = null, Fade? fadeOut = null)
        => new(
            TrackId.New(),
            $"/audio/{file}",
            file,
            duration ?? TimeSpan.FromMinutes(3),
            new TrackDefaults(Out: fadeOut));
}
