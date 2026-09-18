namespace Aria.Audio.Tests;

using Aria.Core.Playback;

public sealed class PreviewSessionTests
{
    private const int SampleRate = 48000;
    private const int Channels = 2;

    private sealed class Rig : IDisposable
    {
        public AriaAudioEngine Engine { get; }

        public Rig()
        {
            Engine = new AriaAudioEngine(
                new SyntheticSourceFactory(SampleRate, Channels),
                null,
                SampleRate,
                Channels,
                256,
                new NullSink(SampleRate, Channels),
                null,
                new NullSink(SampleRate, Channels),
                new PreviewTap(SampleRate * 2, Channels));
        }

        public void Dispose() => Engine.Dispose();
    }

    private static async Task Poll(Func<bool> condition, string message)
    {
        var deadline = Environment.TickCount64 + 10000;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                Assert.Fail(message);
            }
            await Task.Delay(25);
        }
    }

    private static double Peak(float[] buffer, int count)
    {
        var peak = 0.0;
        for (var i = 0; i < count; i++)
        {
            peak = Math.Max(peak, Math.Abs(buffer[i]));
        }
        return peak;
    }

    private static StreamHandle StartSine(Rig rig)
    {
        var handle = rig.Engine.StartStream(
            new TrackSource("/audio/sine.flac", TimeSpan.Zero, null),
            new StreamOptions(StreamBus.Main, []));
        rig.Engine.Transport(handle, TransportCommand.Play);
        return handle;
    }

    [Fact]
    public async Task MutedSession_MirrorsMainAudio()
    {
        using var rig = new Rig();
        var session = rig.Engine.OpenPreviewSession();
        StartSine(rig);
        var tap = rig.Engine.PreviewSessionTap(session);
        Assert.NotNull(tap);
        var reader = tap.Subscribe();
        var buffer = new float[4096];
        var peak = 0.0;
        await Poll(() =>
        {
            peak = Math.Max(peak, Peak(buffer, reader.Read(buffer)));
            return peak > 0.1;
        }, "muted session never received main audio");
    }

    [Fact]
    public async Task ClickSession_AddsClickOverTrack()
    {
        using var rig = new Rig();
        var muted = rig.Engine.OpenPreviewSession();
        var clicked = rig.Engine.OpenPreviewSession();
        rig.Engine.SetClick(clicked, new ClickSettings(120, 4, 0, 0));
        rig.Engine.SetClickMuted(clicked, false);
        StartSine(rig);
        var mutedTap = rig.Engine.PreviewSessionTap(muted);
        var clickedTap = rig.Engine.PreviewSessionTap(clicked);
        Assert.NotNull(mutedTap);
        Assert.NotNull(clickedTap);
        var mutedReader = mutedTap.Subscribe();
        var clickedReader = clickedTap.Subscribe();
        var buffer = new float[8192];
        var mutedPeak = 0.0;
        var clickedPeak = 0.0;
        await Poll(() =>
        {
            mutedPeak = Math.Max(mutedPeak, Peak(buffer, mutedReader.Read(buffer)));
            clickedPeak = Math.Max(clickedPeak, Peak(buffer, clickedReader.Read(buffer)));
            return clickedPeak > 0.8;
        }, "clicked session never rose above track");
        Assert.True(mutedPeak < 0.7, "muted session leaked click, peak " + mutedPeak);
    }

    [Fact]
    public async Task ClickSilent_WhenMainStopped()
    {
        using var rig = new Rig();
        var session = rig.Engine.OpenPreviewSession();
        rig.Engine.SetClick(session, new ClickSettings(120, 4, 0, 0));
        rig.Engine.SetClickMuted(session, false);
        var handle = StartSine(rig);
        var tap = rig.Engine.PreviewSessionTap(session);
        Assert.NotNull(tap);
        var reader = tap.Subscribe();
        var buffer = new float[8192];
        var peak = 0.0;
        await Poll(() =>
        {
            peak = Math.Max(peak, Peak(buffer, reader.Read(buffer)));
            return peak > 0.8;
        }, "clicked session never sounded while playing");
        rig.Engine.Transport(handle, TransportCommand.Stop);
        await Task.Delay(300);
        int read;
        do
        {
            read = reader.Read(buffer);
        }
        while (read > 0);
        await Task.Delay(100);
        Assert.Equal(0, reader.Read(buffer));
    }

    [Fact]
    public async Task ClosedSession_StopsPumping()
    {
        using var rig = new Rig();
        var session = rig.Engine.OpenPreviewSession();
        StartSine(rig);
        var tap = rig.Engine.PreviewSessionTap(session);
        Assert.NotNull(tap);
        var reader = tap.Subscribe();
        var buffer = new float[4096];
        await Poll(() => reader.Read(buffer) > 0, "session never received audio");
        rig.Engine.ClosePreviewSession(session);
        await Task.Delay(300);
        int read;
        do
        {
            read = reader.Read(buffer);
        }
        while (read > 0);
        await Task.Delay(100);
        Assert.Equal(0, reader.Read(buffer));
        Assert.Null(rig.Engine.PreviewSessionTap(session));
    }
}
