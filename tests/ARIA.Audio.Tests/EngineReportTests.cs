namespace Aria.Audio.Tests;

using System.Collections.Immutable;
using Aria.Core.Playback;

public sealed class EngineReportTests
{
    [Fact]
    public async Task Engine_Reports_OutputFrameIncreasesByBlock_WithVoiceTag()
    {
        using var engine = new AriaAudioEngine(new SyntheticSourceFactory(48000), sink: new PacedSink());
        var handle = engine.StartStream(new TrackSource("/audio/sine.flac", TimeSpan.Zero, null), new StreamOptions(StreamBus.Main, ImmutableArray<MarkerSpec>.Empty));
        engine.Transport(handle, TransportCommand.Play);
        var report = new MixBlockReport();
        var previous = new MixBlockReport();

        await Poll(() => ReadLatestPair(engine.Reports, previous, report) && report.Count == 1 && report.Voices[0].Active, "no active voice reported");

        Assert.Equal(512, report.OutputFrame - previous.OutputFrame);
        Assert.Equal(handle.Value, report.Voices[0].Tag);
    }

    [Fact]
    public async Task Engine_TestClick_ReportedInBlock()
    {
        using var engine = new AriaAudioEngine(new SyntheticSourceFactory(48000), sink: new PacedSink());
        engine.SetTestClick(true);
        var report = new MixBlockReport();

        await Poll(() => engine.Reports.LatestSequence > 0 && engine.Reports.TryRead(engine.Reports.LatestSequence, report) && report.TestClick, "test click not reported");
        Assert.True(engine.TestClickEnabled);
    }

    [Fact]
    public async Task Engine_TestClick_AutoOffOnActiveVoice()
    {
        using var engine = new AriaAudioEngine(new SyntheticSourceFactory(48000), sink: new PacedSink());
        engine.SetTestClick(true);

        var handle = engine.StartStream(new TrackSource("/audio/sine.flac", TimeSpan.Zero, null), new StreamOptions(StreamBus.Main, ImmutableArray<MarkerSpec>.Empty));
        engine.Transport(handle, TransportCommand.Play);

        await Poll(() => !engine.TestClickEnabled, "test click stayed on with an active voice");
    }

    [Fact]
    public async Task Engine_SinkSwap_RestartsOutputFrameWithNewGeneration()
    {
        using var engine = new AriaAudioEngine(new SyntheticSourceFactory(48000), sink: new PacedSink());
        var report = new MixBlockReport();
        await Poll(() => engine.Reports.LatestSequence > 20, "no reports");

        engine.ReplaceSink(new PacedSink());

        await Poll(() => engine.Reports.TryRead(engine.Reports.LatestSequence, report) && report.SinkGeneration == 1, "generation did not change");
        Assert.True(report.OutputFrame < 48000, $"output frame not restarted: {report.OutputFrame}");
    }

    private static bool ReadLatestPair(MixReportRing ring, MixBlockReport previous, MixBlockReport latest)
    {
        var sequence = ring.LatestSequence;
        return sequence > 1 && ring.TryRead(sequence - 1, previous) && ring.TryRead(sequence, latest);
    }

    private static async Task Poll(Func<bool> condition, string message)
    {
        var deadline = Environment.TickCount64 + 5000;
        while (!condition())
        {
            Assert.True(Environment.TickCount64 < deadline, message);
            await Task.Delay(5);
        }
    }

    private sealed class PacedSink : IAudioSink
    {
        public int SampleRate => 48000;

        public int Channels => 2;

        public int Write(ReadOnlySpan<float> samples)
        {
            Thread.Sleep(1);
            return samples.Length;
        }

        public void Flush()
        {
        }
    }
}
