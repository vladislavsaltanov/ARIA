namespace Aria.Audio.Tests;

using Aria.Core.Playback;

public sealed class SessionGainTests
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

    [Fact]
    public void NamedOpen_AppearsInListSessions()
    {
        using var rig = new Rig();
        var session = rig.Engine.OpenPreviewSession("Monitor");
        var profile = Assert.Single(rig.Engine.ListSessions());
        Assert.Equal(session, profile.Session);
        Assert.Equal("Monitor", profile.Name);
        Assert.Equal(0.0, profile.BackingGainDb);
        Assert.True(profile.ClickMuted);
    }

    [Fact]
    public void RenameSession_UpdatesList()
    {
        using var rig = new Rig();
        var session = rig.Engine.OpenPreviewSession();
        rig.Engine.RenameSession(session, "Drums");
        var profile = Assert.Single(rig.Engine.ListSessions());
        Assert.Equal("Drums", profile.Name);
    }

    [Fact]
    public void CloseSession_RemovesFromList()
    {
        using var rig = new Rig();
        var session = rig.Engine.OpenPreviewSession("Temp");
        rig.Engine.ClosePreviewSession(session);
        Assert.Empty(rig.Engine.ListSessions());
        Assert.Null(rig.Engine.PreviewSessionTap(session));
    }

    [Fact]
    public async Task SetSessionClickGain_ScalesClick()
    {
        using var rig = new Rig();
        var loud = rig.Engine.OpenPreviewSession("loud");
        var quiet = rig.Engine.OpenPreviewSession("quiet");
        rig.Engine.SetClick(loud, new ClickSettings(120, 4, 0, 0));
        rig.Engine.SetClick(quiet, new ClickSettings(120, 4, 0, 0));
        rig.Engine.SetClickMuted(loud, false);
        rig.Engine.SetClickMuted(quiet, false);
        rig.Engine.SetSessionClickGain(quiet, -6.0);
        var loudTap = rig.Engine.PreviewSessionTap(loud);
        var quietTap = rig.Engine.PreviewSessionTap(quiet);
        Assert.NotNull(loudTap);
        Assert.NotNull(quietTap);
        var loudReader = loudTap.Subscribe();
        var quietReader = quietTap.Subscribe();
        var buffer = new float[256 * Channels];
        await Poll(() => loudReader.Read(buffer) > 0 && quietReader.Read(buffer) > 0, "clicks never started");
        var loudPeak = 0.0;
        var quietPeak = 0.0;
        for (var i = 0; i < 200; i++)
        {
            loudPeak = Math.Max(loudPeak, Peak(buffer, loudReader.Read(buffer)));
            quietPeak = Math.Max(quietPeak, Peak(buffer, quietReader.Read(buffer)));
        }
        Assert.True(loudPeak > 0, "loud click silent");
        Assert.InRange(quietPeak / loudPeak, 0.4, 0.6);
    }

    [Fact]
    public async Task SetSessionBackingGain_ScalesTrackAudio()
    {
        using var rig = new Rig();
        var loud = rig.Engine.OpenPreviewSession("loud");
        var quiet = rig.Engine.OpenPreviewSession("quiet");
        rig.Engine.StartSessionTrack(loud, new TrackSource("/audio/sine.flac", TimeSpan.Zero, null));
        rig.Engine.StartSessionTrack(quiet, new TrackSource("/audio/sine.flac", TimeSpan.Zero, null));
        rig.Engine.SetSessionBackingGain(quiet, -6.0);
        var loudTap = rig.Engine.PreviewSessionTap(loud);
        var quietTap = rig.Engine.PreviewSessionTap(quiet);
        Assert.NotNull(loudTap);
        Assert.NotNull(quietTap);
        var loudReader = loudTap.Subscribe();
        var quietReader = quietTap.Subscribe();
        var buffer = new float[256 * Channels];
        await Poll(() => loudReader.Read(buffer) > 0 && quietReader.Read(buffer) > 0, "sessions never started");
        var loudPeak = 0.0;
        var quietPeak = 0.0;
        for (var i = 0; i < 50; i++)
        {
            loudPeak = Math.Max(loudPeak, Peak(buffer, loudReader.Read(buffer)));
            quietPeak = Math.Max(quietPeak, Peak(buffer, quietReader.Read(buffer)));
        }
        Assert.True(loudPeak > 0, "loud session silent");
        var ratio = quietPeak / loudPeak;
        Assert.InRange(ratio, 0.4, 0.6);
    }
}
