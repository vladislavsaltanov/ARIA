namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.State;

public sealed class LufsNormalizeTests
{
    private static void EnableGlobal(Harness h) =>
        h.Submit(new SetGlobalAudio(GlobalAudioSettings.Default with { NormalizeEnabled = true }));

    [Fact]
    public void StoresMeasurementAndEnables()
    {
        using var h = new Harness();
        h.Engine.ScanLufs = _ => -10.0;
        var track = TestShow.Track("a");
        var playlist = TestShow.Playlist("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [playlist], playlist.Id));
        EnableGlobal(h);

        var seq = h.Submit(new NormalizeTrack(track.Id));

        Assert.Null(h.RejectionOf(seq));
        Assert.NotEmpty(h.Events.OfType<ShowDelta>());
        Assert.Equal([track.FilePath], h.Engine.ScannedPaths);
    }

    [Fact]
    public void DisabledGlobal_Rejects()
    {
        using var h = new Harness();
        var track = TestShow.Track("a");
        var playlist = TestShow.Playlist("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [playlist], playlist.Id));

        var seq = h.Submit(new NormalizeTrack(track.Id));

        Assert.Equal("normalize-disabled", h.RejectionOf(seq)!.Reason);
        Assert.Empty(h.Engine.ScannedPaths);
    }

    [Fact]
    public void UnknownTrack_Rejects()
    {
        using var h = new Harness();
        EnableGlobal(h);

        var seq = h.Submit(new NormalizeTrack(TrackId.New()));

        Assert.Equal("unknown-track", h.RejectionOf(seq)!.Reason);
    }

    [Fact]
    public void ScanFailure_Rejects()
    {
        using var h = new Harness();
        h.Engine.ScanLufs = _ => double.NaN;
        var track = TestShow.Track("a");
        var playlist = TestShow.Playlist("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [playlist], playlist.Id));
        EnableGlobal(h);

        var seq = h.Submit(new NormalizeTrack(track.Id));

        Assert.Equal("lufs-scan-failed", h.RejectionOf(seq)!.Reason);
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
    public void ZeroOffset_KeepsGain()
    {
        Assert.Equal(-3.0, LufsNormalize.AdjustGain(-3, -16.0, -16.0), 9);
    }
}
