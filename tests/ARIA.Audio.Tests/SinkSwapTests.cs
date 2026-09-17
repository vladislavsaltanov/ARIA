namespace Aria.Audio.Tests;

using Aria.Core.Playback;

public sealed class SinkSwapTests
{
    [Fact]
    public async Task ReplaceSink_RoutesAudioToNewSink_AndDisposesOld()
    {
        var first = new WatchSink(new CapturingSink(8000, 2));
        using var engine = new AriaAudioEngine(
            new SyntheticSourceFactory(8000, 2), null, 8000, 2, 128,
            sink: first, meters: null, previewSink: new NullSink(8000, 2));
        var handle = engine.StartStream(new TrackSource("/audio/sine.flac", TimeSpan.Zero, null), new StreamOptions(StreamBus.Main, []));
        engine.Transport(handle, TransportCommand.Play);
        await Poll(() => Peak(first.Inner) > 0.1, "old sink never received audio");

        var second = new CapturingSink(8000, 2);
        engine.ReplaceSink(second);

        await Poll(() => second.TotalFrames > 0, "new sink starved after swap");
        await Poll(() => first.Disposed, "old sink never released");
    }

    [Fact]
    public async Task ReplacePreviewSink_RoutesAudioToNewSink_AndDisposesOld()
    {
        var first = new WatchSink(new CapturingSink(8000, 2));
        using var engine = new AriaAudioEngine(
            new SyntheticSourceFactory(8000, 2), null, 8000, 2, 128,
            sink: new NullSink(8000, 2), meters: null, previewSink: first);
        engine.StartPreview(new TrackSource("/audio/sine.flac", TimeSpan.Zero, null), new StreamOptions(StreamBus.Preview, []));

        await Poll(() => Peak(first.Inner) > 0.1, "old preview sink never received audio");

        var second = new CapturingSink(8000, 2);
        engine.ReplacePreviewSink(second);

        await Poll(() => second.TotalFrames > 0, "new preview sink starved after swap");
        await Poll(() => first.Disposed, "old preview sink never released");
    }

    private static async Task Poll(Func<bool> condition, string message)
    {
        var deadline = Environment.TickCount64 + 5_000;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException(message);
            }
            await Task.Delay(10);
        }
    }

    private static float Peak(CapturingSink sink)
    {
        var peak = 0f;
        foreach (var block in sink.Blocks)
        {
            foreach (var sample in block)
            {
                var magnitude = Math.Abs(sample);
                if (magnitude > peak)
                {
                    peak = magnitude;
                }
            }
        }
        return peak;
    }

    private sealed class WatchSink : IAudioSink, IDisposable
    {
        public WatchSink(CapturingSink inner)
        {
            Inner = inner;
        }

        public CapturingSink Inner { get; }

        public bool Disposed { get; private set; }

        public int SampleRate => Inner.SampleRate;

        public int Channels => Inner.Channels;

        public int Write(ReadOnlySpan<float> samples) => Inner.Write(samples);

        public void Flush() => Inner.Flush();

        public void Dispose() => Disposed = true;
    }
}
