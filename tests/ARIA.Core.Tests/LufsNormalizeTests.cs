namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.State;

public sealed class LufsNormalizeTests
{
    [Fact]
    public void AppliesOffsetTowardTarget()
    {
        using var h = new Harness();
        h.Engine.ScanLufs = _ => -10.0;
        var track = TestShow.Track("a");
        var playlist = TestShow.Playlist("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [playlist], playlist.Id));

        var seq = h.Submit(new NormalizeTrackToLufs(track.Id, -16.0));

        Assert.Null(h.RejectionOf(seq));
        Assert.NotEmpty(h.Events.OfType<ShowDelta>());
        Assert.Equal([track.FilePath], h.Engine.ScannedPaths);
        Assert.Equal(-6.0, LufsNormalize.AdjustGain(0, -10.0, -16.0), 9);
    }

    [Fact]
    public void AddsToExistingGain()
    {
        Assert.Equal(-9.0, LufsNormalize.AdjustGain(-3, -10.0, -16.0), 9);
    }

    [Fact]
    public void ClampsToBounds()
    {
        Assert.Equal(12.0, LufsNormalize.AdjustGain(10, -20.0, -4.0), 9);
        Assert.Equal(-60.0, LufsNormalize.AdjustGain(-50, -4.0, -30.0), 9);
    }

    [Fact]
    public void UnknownTrack_Rejects()
    {
        using var h = new Harness();

        var seq = h.Submit(new NormalizeTrackToLufs(TrackId.New(), -16.0));

        Assert.Equal("unknown-track", h.RejectionOf(seq)!.Reason);
    }

    [Fact]
    public void TargetOutOfRange_Rejects()
    {
        using var h = new Harness();
        var track = TestShow.Track("a");
        var playlist = TestShow.Playlist("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [playlist], playlist.Id));

        Assert.Equal("lufs-out-of-range", h.RejectionOf(h.Submit(new NormalizeTrackToLufs(track.Id, -40.0)))!.Reason);
        Assert.Equal("lufs-out-of-range", h.RejectionOf(h.Submit(new NormalizeTrackToLufs(track.Id, -5.0)))!.Reason);
    }

    [Fact]
    public void ScanFailure_Rejects()
    {
        using var h = new Harness();
        h.Engine.ScanLufs = _ => double.NaN;
        var track = TestShow.Track("a");
        var playlist = TestShow.Playlist("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [playlist], playlist.Id));

        var seq = h.Submit(new NormalizeTrackToLufs(track.Id, -16.0));

        Assert.Equal("lufs-scan-failed", h.RejectionOf(seq)!.Reason);
    }

    [Fact]
    public void BoundaryTargets_Accepted()
    {
        using var h = new Harness();
        h.Engine.ScanLufs = _ => -16.0;
        var track = TestShow.Track("a");
        var playlist = TestShow.Playlist("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [playlist], playlist.Id));

        Assert.Null(h.RejectionOf(h.Submit(new NormalizeTrackToLufs(track.Id, -36.0))));
        Assert.Null(h.RejectionOf(h.Submit(new NormalizeTrackToLufs(track.Id, -12.0))));
    }

    [Fact]
    public void ZeroOffset_KeepsGain()
    {
        Assert.Equal(-3.0, LufsNormalize.AdjustGain(-3, -16.0, -16.0), 9);
    }
}
