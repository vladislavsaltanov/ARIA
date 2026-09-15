namespace Aria.Audio.Tests;

public sealed class ScanLufsTests
{
    private const int SampleRate = 48000;

    [Fact]
    public void Sine_MeasuresNearKnownValue()
    {
        using var engine = new AriaAudioEngine(new StubFactory(path => path == "sine" ? new SineSource(2, SampleRate, 440.0, 0.5) : null));

        var lufs = engine.ScanTrackLufs("sine");

        Assert.InRange(lufs, -8.2, -5.2);
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
        using var engine = new AriaAudioEngine(new StubFactory(_ => new SilenceSource(2, SampleRate)));

        Assert.True(double.IsNaN(engine.ScanTrackLufs("quiet")));
    }

    [Fact]
    public void ThrowingFactory_ReturnsNaN()
    {
        using var engine = new AriaAudioEngine(new StubFactory(_ => throw new InvalidOperationException("disk")));

        Assert.True(double.IsNaN(engine.ScanTrackLufs("boom")));
    }

    private sealed class StubFactory(Func<string, ISampleSource?> open) : ISourceFactory
    {
        public ISampleSource? Open(string filePath, TimeSpan cueIn, TimeSpan? cueOut) => open(filePath);
    }
}
