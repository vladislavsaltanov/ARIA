namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;

public sealed class PreviewCommandTests : IDisposable
{
    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void StartPreviewTrack_RoutesAudioToEngine()
    {
        var audio = new TrackAudioSettings(-3, 0.25, AudioEq.Flat);
        var track = TestShow.Track("one") with { Defaults = TestShow.Track("one").Defaults with { Audio = audio } };
        _harness.Submit(new MergeTracks([track]));

        _harness.Submit(new StartPreviewTrack(track.Id));

        var start = Assert.Single(_harness.Engine.PreviewStarts);
        Assert.Equal(track.FilePath, start.Source.FilePath);
        Assert.Equal(audio, start.Source.Audio);
        Assert.Equal(StreamBus.Preview, start.Options.Bus);
    }

    [Fact]
    public void StartPreviewTrack_UnknownTrack_Rejects()
    {
        var seq = _harness.Submit(new StartPreviewTrack(TrackId.New()));

        Assert.Equal("unknown-track", _harness.RejectionOf(seq)!.Reason);
        Assert.Empty(_harness.Engine.PreviewStarts);
    }

    [Fact]
    public void StopPreview_ForwardsToEngine()
    {
        _harness.Submit(new StopPreview());

        Assert.Equal(1, _harness.Engine.PreviewStops);
    }

    [Fact]
    public void SetPreviewGain_ForwardsToEngine()
    {
        _harness.Submit(new SetPreviewGain(-6));

        Assert.Equal([-6.0], _harness.Engine.PreviewGains);
    }

    [Fact]
    public void SetPreviewGain_OutOfRange_Rejects()
    {
        var high = _harness.Submit(new SetPreviewGain(100));
        var low = _harness.Submit(new SetPreviewGain(-81));

        Assert.Equal("gain-out-of-range", _harness.RejectionOf(high)!.Reason);
        Assert.Equal("gain-out-of-range", _harness.RejectionOf(low)!.Reason);
        Assert.Empty(_harness.Engine.PreviewGains);
    }

    [Fact]
    public void SetPreviewMuted_ForwardsToEngine()
    {
        _harness.Submit(new SetPreviewMuted(true));

        Assert.Equal([true], _harness.Engine.PreviewMutes);
    }

    [Fact]
    public void Panic_StopsPreview()
    {
        _harness.Submit(new Panic());

        Assert.Equal(1, _harness.Engine.PreviewStops);
    }

    [Fact]
    public void Stop_StopsPreview()
    {
        var track = TestShow.Track("one");
        var project = TestShow.Project("Main", TestShow.Entry(track));
        _harness.Submit(new LoadShow([track], [project], project.Id));
        _harness.Submit(new PlayTrack(track.Id));

        _harness.Submit(new Stop());

        Assert.Equal(1, _harness.Engine.PreviewStops);
    }

    [Fact]
    public void SetGlobalAudio_PushesToEngine()
    {
        var global = GlobalAudioSettings.Default with { Pan = 0.5 };

        _harness.Submit(new SetGlobalAudio(global));

        Assert.Equal([global], _harness.Engine.GlobalAudios);
    }

    [Fact]
    public void Play_PassesTrackAudioToStream()
    {
        var audio = new TrackAudioSettings(-6, 0.5, AudioEq.Flat);
        var track = TestShow.Track("one") with { Defaults = TestShow.Track("one").Defaults with { Audio = audio } };
        var project = TestShow.Project("Main", TestShow.Entry(track));
        _harness.Submit(new LoadShow([track], [project], project.Id));

        _harness.Submit(new PlayTrack(track.Id));

        Assert.Equal(audio, _harness.Engine.Last!.Source.Audio);
    }
}
