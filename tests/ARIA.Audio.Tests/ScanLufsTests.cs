namespace Aria.Audio.Tests;

public sealed class ScanLufsTests
{
    private const int SampleRate = 48000;

    [Fact]
    public void Sine_MeasuresNearKnownValue()
    {
        using var engine = new AriaAudioEngine(new StubFactory(path => path == "sine" ? Take(new SineSource(2, SampleRate, 440.0, 0.5), 10 * SampleRate) : null));

        var lufs = engine.ScanTrackLufs("sine");

        Assert.InRange(lufs, -8.2, -5.2);
    }

    [Fact]
    public void SilencePadding_DoesNotMoveMeasurement()
    {
        using var engine = new AriaAudioEngine(new StubFactory(path => path == "mixed"
            ? Concat(new SilenceSource(2, SampleRate), 2 * SampleRate, new SineSource(2, SampleRate, 440.0, 0.5), 8 * SampleRate)
            : null));

        var lufs = engine.ScanTrackLufs("mixed");

        Assert.InRange(lufs, -8.2, -5.2);
    }

    [Fact]
    public void ShorterThanOneBlock_ReturnsNaN()
    {
        using var engine = new AriaAudioEngine(new StubFactory(path => path == "blip" ? Take(new SineSource(2, SampleRate, 440.0, 0.5), SampleRate / 5) : null));

        Assert.True(double.IsNaN(engine.ScanTrackLufs("blip")));
    }

    [Fact]
    public void MissingFile_ReturnsNaN()
    {
        using var engine = new AriaAudioEngine(new StubFactory(_ => null));

        Assert.True(double.IsNaN(engine.ScanTrackLufs("nope")));
    }

    [Fact]
    public void Silence_ReturnsNaN()
    {
        using var engine = new AriaAudioEngine(new StubFactory(_ => Take(new SilenceSource(2, SampleRate), 2 * SampleRate)));

        Assert.True(double.IsNaN(engine.ScanTrackLufs("quiet")));
    }

    [Fact]
    public void ThrowingFactory_ReturnsNaN()
    {
        using var engine = new AriaAudioEngine(new StubFactory(_ => throw new InvalidOperationException("disk")));

        Assert.True(double.IsNaN(engine.ScanTrackLufs("boom")));
    }

    private static ISampleSource Take(ISampleSource inner, long maxFrames) => new TakeSource(inner, maxFrames);

    private static ISampleSource Concat(ISampleSource first, long firstFrames, ISampleSource second, long secondFrames) =>
        new ConcatSource([(first, firstFrames), (second, secondFrames)]);

    private sealed class StubFactory(Func<string, ISampleSource?> open) : ISourceFactory
    {
        public ISampleSource? Open(string filePath, TimeSpan cueIn, TimeSpan? cueOut) => open(filePath);
    }

    private sealed class TakeSource(ISampleSource inner, long maxFrames) : ISampleSource
    {
        private long _remaining = maxFrames;

        public int Channels => inner.Channels;

        public int SampleRate => inner.SampleRate;

        public int ReadFrames(Span<float> destination)
        {
            if (_remaining <= 0)
            {
                return 0;
            }
            var frames = Math.Min(destination.Length / Channels, (int)Math.Min(_remaining, int.MaxValue));
            var read = inner.ReadFrames(destination[..(frames * Channels)]);
            _remaining -= read;
            return read;
        }

        public void Seek(long frameIndex) => inner.Seek(frameIndex);
    }

    private sealed class ConcatSource((ISampleSource Source, long Frames)[] parts) : ISampleSource
    {
        private int _part;

        private long _remaining = parts[0].Frames;

        public int Channels => parts[0].Source.Channels;

        public int SampleRate => parts[0].Source.SampleRate;

        public int ReadFrames(Span<float> destination)
        {
            var total = 0;
            while (total * Channels < destination.Length && _part < parts.Length)
            {
                if (_remaining <= 0)
                {
                    _part++;
                    if (_part < parts.Length)
                    {
                        _remaining = parts[_part].Frames;
                    }
                    continue;
                }
                var want = Math.Min((destination.Length - total * Channels) / Channels, (int)Math.Min(_remaining, int.MaxValue));
                var read = parts[_part].Source.ReadFrames(destination.Slice(total * Channels, want * Channels));
                if (read == 0)
                {
                    _part++;
                    if (_part < parts.Length)
                    {
                        _remaining = parts[_part].Frames;
                    }
                    continue;
                }
                total += read;
                _remaining -= read;
            }
            return total;
        }

        public void Seek(long frameIndex) => throw new NotSupportedException();
    }
}
