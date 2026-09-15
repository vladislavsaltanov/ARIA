namespace Aria.Audio.Tests;

using Aria.Core.Playback;

public sealed class PreviewBusTests
{
    [Fact]
    public async Task PreviewOnly_RendersToPreviewSink_MainStaysSilent()
    {
        using var rig = new Rig();
        rig.Engine.StartPreview(new TrackSource("/audio/sine.flac", TimeSpan.Zero, null), PreviewOptions());

        await Poll(() => TotalPeak(rig.Preview) > 0.1, "preview sink never received audio");

        await Task.Delay(200);
        Assert.True(TotalPeak(rig.Preview) > 0.1, "preview sink has no audible signal");
        Assert.Equal(0f, TotalPeak(rig.Main));
    }

    [Fact]
    public async Task Preview_DoesNotDisturbMainPlayback()
    {
        using var rig = new Rig();
        var main = rig.Engine.StartStream(
            new TrackSource("/audio/sine.flac", TimeSpan.Zero, null),
            new StreamOptions(StreamBus.Main, []));
        rig.Engine.Transport(main, TransportCommand.Play);
        await Poll(() => TotalPeak(rig.Main) > 0.1, "main sink never received audio");

        rig.Engine.StartPreview(new TrackSource("/audio/sine.flac", TimeSpan.Zero, null), PreviewOptions());
        await Poll(() => TotalPeak(rig.Preview) > 0.1, "preview sink never received audio");

        await Task.Delay(200);
        Assert.InRange(TotalPeak(rig.Main), 0.4, 0.6);
    }

    [Fact]
    public async Task PreviewMuted_SilencesPreview()
    {
        using var rig = new Rig();
        rig.Engine.StartPreview(new TrackSource("/audio/sine.flac", TimeSpan.Zero, null), PreviewOptions());
        await Poll(() => TotalPeak(rig.Preview) > 0.1, "preview sink never received audio");

        rig.Engine.SetPreviewMuted(true);
        var fromFrame = rig.Preview.TotalFrames;
        await Poll(() => rig.Preview.TotalFrames > fromFrame + 512, "preview sink stalled after mute");
        Assert.Equal(0f, PeakFrom(rig.Preview, fromFrame));
    }

    [Fact]
    public async Task PreviewGain_Plus6dB_DoublesAmplitude()
    {
        using var rig = new Rig();
        rig.Engine.StartPreview(new TrackSource("/audio/sine.flac", TimeSpan.Zero, null), PreviewOptions());
        await Poll(() => TotalPeak(rig.Preview) > 0.1, "preview sink never received audio");
        await Task.Delay(200);
        Assert.InRange(TotalPeak(rig.Preview), 0.4, 0.6);

        rig.Engine.SetPreviewGain(6.0);
        await Poll(() => TotalPeak(rig.Preview) > 0.9, "preview gain +6dB did not reach output");
        Assert.InRange(TotalPeak(rig.Preview), 0.9, 1.1);
    }

    [Fact]
    public async Task StopPreview_SilencesPreview()
    {
        using var rig = new Rig();
        rig.Engine.StartPreview(new TrackSource("/audio/sine.flac", TimeSpan.Zero, null), PreviewOptions());
        await Poll(() => TotalPeak(rig.Preview) > 0.1, "preview sink never received audio");

        rig.Engine.StopPreview();
        var fromFrame = rig.Preview.TotalFrames;
        await Poll(() => rig.Preview.TotalFrames > fromFrame + 2048, "preview sink stalled after stop");
        Assert.Equal(0f, PeakFrom(rig.Preview, fromFrame + 1024));
    }

    [Fact]
    public async Task SecondStartPreview_ReplacesFirst()
    {
        using var rig = new Rig();
        rig.Engine.StartPreview(new TrackSource("/audio/sine.flac", TimeSpan.Zero, null), PreviewOptions());
        await Poll(() => TotalPeak(rig.Preview) > 0.1, "preview sink never received audio");

        rig.Engine.StartPreview(new TrackSource("/audio/sine.flac", TimeSpan.Zero, null), PreviewOptions());
        await Task.Delay(300);
        Assert.InRange(TotalPeak(rig.Preview), 0.4, 0.6);
    }

    [Fact]
    public async Task MissingFile_Preview_EmitsFaulted()
    {
        using var rig = new Rig();
        rig.Engine.StartPreview(new TrackSource("/audio/broken.flac", TimeSpan.Zero, null), PreviewOptions());

        var deadline = Environment.TickCount64 + 10_000;
        while (true)
        {
            lock (rig.Events)
            {
                if (rig.Events.Exists(e => e.Kind == StreamEventKind.Faulted))
                {
                    return;
                }
            }
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException("preview fault was never reported");
            }
            await Task.Delay(10);
        }
    }

    private static StreamOptions PreviewOptions() => new(StreamBus.Preview, []);

    private static async Task Poll(Func<bool> condition, string message)
    {
        var deadline = Environment.TickCount64 + 10_000;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException(message);
            }
            await Task.Delay(10);
        }
    }

    private static float TotalPeak(CapturingSink sink)
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

    private static float PeakFrom(CapturingSink sink, int fromFrame)
    {
        var peak = 0f;
        var cursor = 0;
        foreach (var block in sink.Blocks)
        {
            var frames = block.Length / sink.Channels;
            if (cursor + frames > fromFrame)
            {
                var skip = Math.Max(0, fromFrame - cursor);
                for (var index = skip * sink.Channels; index < block.Length; index++)
                {
                    var magnitude = Math.Abs(block[index]);
                    if (magnitude > peak)
                    {
                        peak = magnitude;
                    }
                }
            }
            cursor += frames;
        }
        return peak;
    }

    private sealed class Rig : IDisposable
    {
        public CapturingSink Main { get; } = new(8000, 2);

        public CapturingSink Preview { get; } = new(8000, 2);

        public AriaAudioEngine Engine { get; }

        public List<StreamEvent> Events { get; } = [];

        public Rig()
        {
            Engine = new AriaAudioEngine(
                new SyntheticSourceFactory(8000, 2),
                null,
                sampleRate: 8000,
                channels: 2,
                blockSizeFrames: 128,
                sink: Main,
                meters: null,
                previewSink: Preview);
            Engine.Events += e =>
            {
                lock (Events)
                {
                    Events.Add(e);
                }
            };
        }

        public void Dispose() => Engine.Dispose();
    }
}
