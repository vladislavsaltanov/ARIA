namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.State;

public sealed class PlaybackTests
{
    [Fact]
    public void LoadShow_PopulatesState()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1), TestShow.Entry(t1));

        h.Submit(new LoadShow([t1], [p], p.Id));

        var snap = h.Snapshot;
        Assert.Equal(TransportStatus.Stopped, snap.Transport.Status);
        Assert.Equal(p.Id, snap.Show.ActiveId);
        Assert.Single(snap.Show.Playlists);
    }

    [Fact]
    public void LoadShow_WhilePlaying_IsRejected()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new Play());

        var seq = h.Submit(new LoadShow([t1], [p], p.Id));

        Assert.Equal("show-load-requires-stopped", h.RejectionOf(seq)?.Reason);
    }

    [Fact]
    public void Play_WithEmptyShow_IsRejected()
    {
        using var h = new Harness();

        var seq = h.Submit(new Play());

        Assert.Equal("nothing-to-play", h.RejectionOf(seq)?.Reason);
        Assert.Equal(TransportStatus.Stopped, h.Transport.Status);
    }

    [Fact]
    public void Play_StartsFirstEntry_AndReportsNext()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1), TestShow.Entry(t2));
        h.Submit(new LoadShow([t1, t2], [p], p.Id));

        h.Submit(new Play());

        var stream = Assert.Single(h.Engine.Created);
        Assert.Equal(t1.FilePath, stream.Source.FilePath);
        Assert.Contains(TransportCommand.Play, stream.Transports);
        Assert.Equal(TransportStatus.Playing, h.Transport.Status);
        Assert.Equal(t1.Id, h.Transport.Current!.TrackId);
        Assert.Equal(t1.DefaultName, h.Transport.Current.DisplayName);
        Assert.Equal(t2.Id, h.Transport.Next!.TrackId);
    }

    [Fact]
    public void EndAction_Advance_StartsNextEntry_IgnoresStaleEvents()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1), TestShow.Entry(t2));
        h.Submit(new LoadShow([t1, t2], [p], p.Id));
        h.Submit(new Play());
        var first = h.Engine.Created[0];

        h.Engine.End(first.Handle, StreamEndReason.Completed);

        Assert.Equal(2, h.Engine.Created.Count);
        Assert.Equal(t2.FilePath, h.Engine.Last!.Source.FilePath);
        Assert.Equal(t2.Id, h.Transport.Current!.TrackId);
        Assert.Null(h.Transport.Next);

        h.Engine.End(first.Handle, StreamEndReason.Completed);

        Assert.Equal(2, h.Engine.Created.Count);
    }

    [Fact]
    public void EndAction_Pause_HoldsBoundary_PlayAdvances()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one", EndAction.Pause);
        var t2 = TestShow.Track("two");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1), TestShow.Entry(t2));
        h.Submit(new LoadShow([t1, t2], [p], p.Id));
        h.Submit(new Play());

        h.Engine.End(h.Engine.Created[0].Handle, StreamEndReason.Completed);

        Assert.Equal(TransportStatus.Paused, h.Transport.Status);
        Assert.Equal(t1.Id, h.Transport.Current!.TrackId);

        h.Submit(new Play());

        Assert.Equal(TransportStatus.Playing, h.Transport.Status);
        Assert.Equal(t2.Id, h.Transport.Current!.TrackId);
    }

    [Fact]
    public void EndAction_Replay_RestartsSameEntry()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one", EndAction.Replay);
        var p = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new Play());

        h.Engine.End(h.Engine.Created[0].Handle, StreamEndReason.Completed);

        Assert.Equal(2, h.Engine.Created.Count);
        Assert.Equal(t1.FilePath, h.Engine.Last!.Source.FilePath);
        Assert.Equal(TransportStatus.Playing, h.Transport.Status);
        Assert.Equal(t1.Id, h.Transport.Current!.TrackId);
    }

    [Fact]
    public void EndAction_Stop_HoldsCurrent_PlayReplaysIt()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one", EndAction.Stop);
        var p = TestShow.Playlist("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new Play());

        h.Engine.End(h.Engine.Created[0].Handle, StreamEndReason.Completed);

        Assert.Equal(TransportStatus.Stopped, h.Transport.Status);
        Assert.Equal(t1.Id, h.Transport.Current!.TrackId);

        h.Submit(new Play());

        Assert.Equal(2, h.Engine.Created.Count);
        Assert.Equal(t1.Id, h.Transport.Current!.TrackId);
        Assert.Equal(TransportStatus.Playing, h.Transport.Status);
    }

    [Fact]
    public void Queue_PlaysFirst_ThenPlaylistContinuesFromCursor()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        var t3 = TestShow.Track("three");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1), TestShow.Entry(t2), TestShow.Entry(t3));
        h.Submit(new LoadShow([t1, t2, t3], [p], p.Id));

        var e3 = p.Entries[2];
        h.Submit(new EnqueueEntry(e3.Id));
        h.Submit(new Play());

        Assert.Equal(t3.Id, h.Transport.Current!.TrackId);
        Assert.Equal(t1.Id, h.Transport.Next!.TrackId);

        h.Engine.End(h.Engine.Created[0].Handle, StreamEndReason.Completed);
        Assert.Equal(t1.Id, h.Transport.Current!.TrackId);

        h.Engine.End(h.Engine.Created[1].Handle, StreamEndReason.Completed);
        Assert.Equal(t2.Id, h.Transport.Current!.TrackId);

        h.Engine.End(h.Engine.Created[2].Handle, StreamEndReason.Completed);
        Assert.Equal(t3.Id, h.Transport.Current!.TrackId);
    }

    [Fact]
    public void Next_ReleasesCurrentWithFadeAndStartsNext()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one", fadeOut: new Fade(TimeSpan.FromSeconds(2), FadeCurve.SCurve));
        var t2 = TestShow.Track("two");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1), TestShow.Entry(t2));
        h.Submit(new LoadShow([t1, t2], [p], p.Id));
        h.Submit(new Play());
        var first = h.Engine.Created[0];

        h.Submit(new Next());

        Assert.Equal(2, h.Engine.Created.Count);
        Assert.Equal(t2.Id, h.Transport.Current!.TrackId);
        var fadeMix = first.Mixes.Last();
        Assert.True(fadeMix.Fade!.StopWhenDone);
        Assert.Equal(TimeSpan.FromSeconds(2), fadeMix.Fade!.Duration);

        h.Engine.End(first.Handle, StreamEndReason.FadeCompleted);
        Assert.Contains(first.Handle, h.Engine.Disposed);
        Assert.Equal(2, h.Engine.Created.Count);
    }

    [Fact]
    public void Panic_SilencesEngine_BlocksAutoAdvance_ManualRecovery()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1), TestShow.Entry(t2));
        h.Submit(new LoadShow([t1, t2], [p], p.Id));
        h.Submit(new Play());
        var first = h.Engine.Created[0];

        h.Submit(new Panic());

        var panic = Assert.Single(h.Engine.Panics);
        Assert.Equal(TimeSpan.FromMilliseconds(100), panic.FadeDuration);
        Assert.Equal(TransportStatus.Panicked, h.Transport.Status);
        Assert.Equal(t1.Id, h.Transport.Current!.TrackId);

        h.Engine.End(first.Handle, StreamEndReason.Panic);
        Assert.Contains(first.Handle, h.Engine.Disposed);
        Assert.Equal(TransportStatus.Panicked, h.Transport.Status);
        Assert.Equal(t1.Id, h.Transport.Current!.TrackId);

        h.Submit(new Play());

        Assert.Equal(TransportStatus.Playing, h.Transport.Status);
        Assert.Equal(t1.Id, h.Transport.Current!.TrackId);
        Assert.Equal(2, h.Engine.Created.Count);
        Assert.Equal(t1.FilePath, h.Engine.Last!.Source.FilePath);
    }

    [Fact]
    public void DuplicateSeq_IsDropped()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p1 = TestShow.Playlist("First", TestShow.Entry(t1));
        var p2 = TestShow.Playlist("Second", TestShow.Entry(t1));

        h.Bus.Submit(Harness.Client, 1, new LoadShow([t1], [p1], p1.Id));
        h.Bus.Submit(Harness.Client, 1, new LoadShow([t1], [p2], p2.Id));

        Assert.Equal(p1.Id, h.Snapshot.Show.ActiveId);
        Assert.Single(h.Snapshot.Show.Playlists);
    }

    [Fact]
    public void SetMasterGain_ValidatesRange()
    {
        using var h = new Harness();

        var bad = h.Submit(new SetMasterGain(50));
        Assert.Equal("gain-out-of-range", h.RejectionOf(bad)?.Reason);
        Assert.Empty(h.Engine.MasterGains);

        h.Submit(new SetMasterGain(-6));
        Assert.Contains(-6, h.Engine.MasterGains);
        Assert.Equal(-6, h.Snapshot.Mixer.MasterGainDb);
    }

    [Fact]
    public void FaultedStream_StopsWithoutAdvance()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("broken");
        var t2 = TestShow.Track("good");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1), TestShow.Entry(t2));
        h.Submit(new LoadShow([t1, t2], [p], p.Id));
        h.Submit(new Play());

        h.Engine.Fault(h.Engine.Created[0].Handle);

        Assert.Single(h.Engine.Created);
        Assert.Null(h.Transport.Current);
        Assert.Equal(TransportStatus.Stopped, h.Transport.Status);
        Assert.Contains(t1.Id, h.Transport.Faulted);
    }

    [Fact]
    public void JumpTo_StartsEntry_AndAdvancesAfterIt()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        var t3 = TestShow.Track("three");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1), TestShow.Entry(t2), TestShow.Entry(t3));
        h.Submit(new LoadShow([t1, t2, t3], [p], p.Id));
        h.Submit(new Play());

        h.Submit(new JumpTo(p.Entries[2].Id));

        Assert.Equal(t3.Id, h.Transport.Current!.TrackId);
        Assert.Null(h.Transport.Next);

        h.Engine.End(h.Engine.Created[1].Handle, StreamEndReason.Completed);

        Assert.Equal(TransportStatus.Stopped, h.Transport.Status);
        Assert.Null(h.Transport.Current);
    }

    [Fact]
    public void MarkerStop_TriggersEndAction_AndMarkerReachesEngine()
    {
        using var h = new Harness();
        var marker = new Marker("storm-end", TimeSpan.FromSeconds(90), MarkerAction.Stop);
        var t1 = TestShow.Track("storm", markers: [marker]);
        var t2 = TestShow.Track("calm");
        var p = TestShow.Playlist("Main", TestShow.Entry(t1), TestShow.Entry(t2));
        h.Submit(new LoadShow([t1, t2], [p], p.Id));
        h.Submit(new Play());
        var first = h.Engine.Created[0];

        var spec = Assert.Single(first.Options.Markers);
        Assert.Equal("storm-end", spec.Name);

        h.Engine.End(first.Handle, StreamEndReason.StoppedByMarker);

        Assert.Equal(t2.Id, h.Transport.Current!.TrackId);
    }

    [Fact]
    public void EntryOverrides_AppliedToPlayback()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("original", gainDb: -3);
        var overrides = new PlaylistOverrides(
            Name: "Буря, акт 2",
            Color: "amber",
            GainDb: -6,
            EndAction: EndAction.Pause,
            CueIn: TimeSpan.FromSeconds(10));
        var p = TestShow.Playlist("Main", TestShow.Entry(t1, overrides));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new Play());

        var current = h.Transport.Current!;
        Assert.Equal("Буря, акт 2", current.DisplayName);
        Assert.Equal("amber", current.Color);
        Assert.Equal(EndAction.Pause, current.EndAction);
        Assert.Equal(TimeSpan.FromSeconds(10), current.CueIn);
        Assert.Equal(TimeSpan.FromSeconds(10), h.Engine.Last!.Source.CueIn);

        var mix = h.Engine.Last!.Mixes.Single();
        Assert.Equal(-6, mix.GainDb);
    }
}
