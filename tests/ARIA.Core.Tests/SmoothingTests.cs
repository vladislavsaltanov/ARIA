namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.State;

public sealed class SmoothingTests
{
    private static Smoothing Enabled(
        int manualMs = 400,
        int autoMs = 900,
        int startMs = 250,
        int stopMs = 300) => new(
            true,
            TimeSpan.FromMilliseconds(manualMs),
            TimeSpan.FromMilliseconds(autoMs),
            TimeSpan.FromMilliseconds(startMs),
            TimeSpan.FromMilliseconds(stopMs));

    [Fact]
    public void FreshBoot_SmoothingIsDefault()
    {
        using var h = new Harness();

        Assert.Equal(Smoothing.Default, h.Snapshot.Mixer.Smoothing);
    }

    [Fact]
    public void SetSmoothing_UpdatesMixer_AndPushesEngine()
    {
        using var h = new Harness();
        var smoothing = Enabled();

        h.Submit(new SetSmoothing(smoothing));

        Assert.Equal(smoothing, h.Snapshot.Mixer.Smoothing);
        Assert.Equal(smoothing, Assert.Single(h.Engine.Smoothings));
        var delta = h.Events.OfType<MixerDelta>().Last();
        Assert.Equal(smoothing, delta.State.Smoothing);
    }

    [Theory]
    [InlineData(-1, 900, 250, 300)]
    [InlineData(400, 6001, 250, 300)]
    [InlineData(400, 900, -5, 300)]
    [InlineData(400, 900, 250, 10000)]
    public void SetSmoothing_OutOfRange_IsRejected(int manualMs, int autoMs, int startMs, int stopMs)
    {
        using var h = new Harness();
        var smoothing = new Smoothing(
            true,
            TimeSpan.FromMilliseconds(manualMs),
            TimeSpan.FromMilliseconds(autoMs),
            TimeSpan.FromMilliseconds(startMs),
            TimeSpan.FromMilliseconds(stopMs));

        var seq = h.Submit(new SetSmoothing(smoothing));

        Assert.Equal("smoothing-out-of-range", h.RejectionOf(seq)!.Reason);
        Assert.Equal(Smoothing.Default, h.Snapshot.Mixer.Smoothing);
        Assert.Empty(h.Engine.Smoothings);
    }

    [Fact]
    public void JumpTo_WhilePlaying_OldVoiceGetsManualCrossfade_NewGetsStartFade()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1), TestShow.Entry(t2));
        h.Submit(new LoadShow([t1, t2], [p], p.Id));
        h.Submit(new Play());
        h.Submit(new SetSmoothing(Enabled(manualMs: 400, startMs: 250)));

        h.Submit(new JumpTo(p.Entries[1].Id));

        var old = h.Engine.Created[0];
        var manual = Assert.Single(old.Mixes, m => m.Fade is not null);
        Assert.Equal(TimeSpan.FromMilliseconds(400), manual.Fade!.Duration);
        Assert.True(manual.Fade!.StopWhenDone);
        var fresh = h.Engine.Last!;
        Assert.Equal(TimeSpan.FromMilliseconds(250), fresh.Mixes[0].Fade!.Duration);
        Assert.False(fresh.Mixes[0].Fade!.StopWhenDone);
    }

    [Fact]
    public void AutoAdvance_UsesAutoCrossfadeForNewStream()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1), TestShow.Entry(t2));
        h.Submit(new LoadShow([t1, t2], [p], p.Id));
        h.Submit(new Play());
        h.Submit(new SetSmoothing(Enabled(autoMs: 900)));

        h.Engine.End(h.Engine.Created[0].Handle, StreamEndReason.Completed);

        Assert.Equal(TimeSpan.FromMilliseconds(900), h.Engine.Last!.Mixes[0].Fade!.Duration);
    }

    [Fact]
    public void AutoAdvance_WithSmoothing_OldVoiceFadesOutWithAutoCrossfade()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1), TestShow.Entry(t2));
        h.Submit(new LoadShow([t1, t2], [p], p.Id));
        h.Submit(new Play());
        h.Submit(new SetSmoothing(Enabled(autoMs: 900)));

        h.Engine.End(h.Engine.Created[0].Handle, StreamEndReason.Completed);

        var old = h.Engine.Created[0];
        var fade = Assert.Single(old.Mixes, m => m.Fade is not null);
        Assert.Equal(TimeSpan.FromMilliseconds(900), fade.Fade!.Duration);
        Assert.True(fade.Fade!.StopWhenDone);
        Assert.DoesNotContain(old.Handle, h.Engine.Disposed);
        Assert.Equal(2, h.Engine.Created.Count);
    }

    [Fact]
    public void Disabled_UsesTrackFades()
    {
        using var h = new Harness();
        var fadeIn = new Fade(TimeSpan.FromMilliseconds(111), FadeCurve.Linear);
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two", fadeIn: fadeIn);
        var p = TestShow.Playlist("Main", TestShow.Entry(t1), TestShow.Entry(t2));
        h.Submit(new LoadShow([t1, t2], [p], p.Id));
        h.Submit(new Play());

        h.Engine.End(h.Engine.Created[0].Handle, StreamEndReason.Completed);

        Assert.Equal(TimeSpan.FromMilliseconds(111), h.Engine.Last!.Mixes[0].Fade!.Duration);
    }

    [Fact]
    public void Stop_WithSmoothing_RetiresVoiceWithStopFade()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new Play());
        h.Submit(new SetSmoothing(Enabled(stopMs: 300)));

        h.Submit(new Stop());

        var stream = h.Engine.Created[0];
        var fade = Assert.Single(stream.Mixes, m => m.Fade is not null);
        Assert.Equal(TimeSpan.FromMilliseconds(300), fade.Fade!.Duration);
        Assert.True(fade.Fade!.StopWhenDone);
        Assert.DoesNotContain(TransportCommand.Stop, stream.Transports);
        Assert.Equal(TransportStatus.Stopped, h.Transport.Status);
    }

    [Fact]
    public void Stop_Disabled_StopsInstantly()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new Play());

        h.Submit(new Stop());

        var stream = h.Engine.Created[0];
        Assert.Contains(TransportCommand.Stop, stream.Transports);
        Assert.DoesNotContain(stream.Mixes, m => m.Fade is not null);
    }

    [Fact]
    public void PreRoll_StartsNextBeforeOldEnds_WithAudibleOverlap()
    {
        var monitor = new PlaybackMonitor();
        using var h = new Harness(monitor);
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1), TestShow.Entry(t2));
        h.Submit(new LoadShow([t1, t2], [p], p.Id));
        h.Submit(new Play());
        h.Submit(new SetSmoothing(Enabled(autoMs: 900)));

        monitor.Publish(h.Engine.Created[0].Handle, t1.Duration - TimeSpan.FromMilliseconds(500));

        Assert.Equal(2, h.Engine.Created.Count);
        Assert.Empty(h.Engine.Disposed);
        var old = h.Engine.Created[0];
        var fadeOut = Assert.Single(old.Mixes, m => m.Fade is not null);
        Assert.Equal(TimeSpan.FromMilliseconds(900), fadeOut.Fade!.Duration);
        Assert.True(fadeOut.Fade!.StopWhenDone);
        var fresh = h.Engine.Last!;
        Assert.Equal(TimeSpan.FromMilliseconds(900), fresh.Mixes[0].Fade!.Duration);
        Assert.False(fresh.Mixes[0].Fade!.StopWhenDone);
        Assert.Equal(TransportStatus.Playing, h.Transport.Status);
        Assert.Equal(t2.Id, h.Transport.Current!.TrackId);
    }

    [Fact]
    public void PreRoll_FiresOnce()
    {
        var monitor = new PlaybackMonitor();
        using var h = new Harness(monitor);
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1), TestShow.Entry(t2));
        h.Submit(new LoadShow([t1, t2], [p], p.Id));
        h.Submit(new Play());
        h.Submit(new SetSmoothing(Enabled(autoMs: 900)));

        monitor.Publish(h.Engine.Created[0].Handle, t1.Duration - TimeSpan.FromMilliseconds(500));
        monitor.Publish(h.Engine.Created[0].Handle, t1.Duration - TimeSpan.FromMilliseconds(100));

        Assert.Equal(2, h.Engine.Created.Count);
    }

    [Fact]
    public void PreRoll_DisabledSmoothing_NoEarlyStart()
    {
        var monitor = new PlaybackMonitor();
        using var h = new Harness(monitor);
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1), TestShow.Entry(t2));
        h.Submit(new LoadShow([t1, t2], [p], p.Id));
        h.Submit(new Play());

        monitor.Publish(h.Engine.Created[0].Handle, t1.Duration - TimeSpan.FromMilliseconds(500));

        Assert.Single(h.Engine.Created);
    }

    [Fact]
    public void PreRoll_NoNext_NoEarlyStart()
    {
        var monitor = new PlaybackMonitor();
        using var h = new Harness(monitor);
        var t1 = TestShow.Track("one");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new Play());
        h.Submit(new SetSmoothing(Enabled(autoMs: 900)));

        monitor.Publish(h.Engine.Created[0].Handle, t1.Duration - TimeSpan.FromMilliseconds(500));

        Assert.Single(h.Engine.Created);
    }

    [Fact]
    public void PreRoll_EndActionStop_NoEarlyStart()
    {
        var monitor = new PlaybackMonitor();
        using var h = new Harness(monitor);
        var t1 = TestShow.Track("one", end: EndAction.Stop);
        var t2 = TestShow.Track("two");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1), TestShow.Entry(t2));
        h.Submit(new LoadShow([t1, t2], [p], p.Id));
        h.Submit(new Play());
        h.Submit(new SetSmoothing(Enabled(autoMs: 900)));

        monitor.Publish(h.Engine.Created[0].Handle, t1.Duration - TimeSpan.FromMilliseconds(500));

        Assert.Single(h.Engine.Created);
    }

    [Fact]
    public void PreRoll_SeekBack_Rearms()
    {
        var monitor = new PlaybackMonitor();
        using var h = new Harness(monitor);
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        var t3 = TestShow.Track("three");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1), TestShow.Entry(t2), TestShow.Entry(t3));
        h.Submit(new LoadShow([t1, t2, t3], [p], p.Id));
        h.Submit(new Play());
        h.Submit(new SetSmoothing(Enabled(autoMs: 900)));

        monitor.Publish(h.Engine.Created[0].Handle, t1.Duration - TimeSpan.FromMilliseconds(500));
        Assert.Equal(2, h.Engine.Created.Count);

        h.Submit(new SeekTo(TimeSpan.Zero));
        monitor.Publish(h.Engine.Created[1].Handle, t2.Duration - TimeSpan.FromMilliseconds(500));

        Assert.Equal(3, h.Engine.Created.Count);
        Assert.Equal(t3.Id, h.Transport.Current!.TrackId);
    }
}
