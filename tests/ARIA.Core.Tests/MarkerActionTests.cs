namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.State;

public sealed class MarkerActionTests
{
    private static Marker Mark(string name, double seconds, MarkerAction action = MarkerAction.Pause) =>
        new(name, TimeSpan.FromSeconds(seconds), action);

    [Fact]
    public void ArmedMarkers_SkipsNone()
    {
        using var h = new Harness();
        var track = TestShow.Track("storm", markers: [Mark("вид", 10, MarkerAction.None), Mark("пауза", 20)]);
        var project = TestShow.Project("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [project], project.Id));
        h.Submit(new Play());

        var spec = Assert.Single(h.Engine.Last!.Options.Markers);
        Assert.Equal("пауза", spec.Name);
        Assert.Equal(MarkerAction.Pause, spec.Action);
    }

    [Fact]
    public void MarkerPause_PausesAndResumesFromMarker()
    {
        using var h = new Harness();
        var track = TestShow.Track("storm", markers: [Mark("привал", 30)]);
        var project = TestShow.Project("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [project], project.Id));
        h.Submit(new Play());
        var handle = h.Engine.Last!.Handle;

        h.Engine.End(handle, StreamEndReason.StoppedByMarker, TimeSpan.FromSeconds(30));

        Assert.Equal(TransportStatus.Paused, h.Transport.Status);
        Assert.Equal(track.Id, h.Transport.Current!.TrackId);

        h.Submit(new Play());

        Assert.Equal(TransportStatus.Playing, h.Transport.Status);
        Assert.Equal(TimeSpan.FromSeconds(30), h.Engine.Last!.Source.CueIn);
        Assert.Empty(h.Engine.Last!.Options.Markers);
    }

    [Fact]
    public void MarkerPause_LeavesPausedStreamAtMarker()
    {
        using var h = new Harness();
        var track = TestShow.Track("storm", markers: [Mark("привал", 30)]);
        var project = TestShow.Project("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [project], project.Id));
        h.Submit(new Play());

        h.Engine.End(h.Engine.Last!.Handle, StreamEndReason.StoppedByMarker, TimeSpan.FromSeconds(30));

        Assert.Equal(TransportStatus.Paused, h.Transport.Status);
        Assert.Equal(TimeSpan.FromSeconds(30), h.Engine.Last!.Source.CueIn);
        Assert.DoesNotContain(TransportCommand.Play, h.Engine.Last!.Transports);
    }

    [Fact]
    public void SeekTo_AfterMarkerPause_SeeksFromMarkerPosition()
    {
        using var h = new Harness();
        var track = TestShow.Track("storm", markers: [Mark("привал", 30)]);
        var project = TestShow.Project("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [project], project.Id));
        h.Submit(new Play());
        h.Engine.End(h.Engine.Last!.Handle, StreamEndReason.StoppedByMarker, TimeSpan.FromSeconds(30));

        h.Submit(new SeekTo(TimeSpan.FromSeconds(60)));

        Assert.Empty(h.Events.OfType<Rejected>());
        Assert.Equal(TimeSpan.FromSeconds(30), Assert.Single(h.Engine.Last!.Seeks));
    }

    [Fact]
    public void SeekTo_BeforeMarker_RestartsStreamAtTarget()
    {
        using var h = new Harness();
        var track = TestShow.Track("storm", markers: [Mark("привал", 30)]);
        var project = TestShow.Project("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [project], project.Id));
        h.Submit(new Play());
        h.Engine.End(h.Engine.Last!.Handle, StreamEndReason.StoppedByMarker, TimeSpan.FromSeconds(30));

        h.Submit(new SeekTo(TimeSpan.FromSeconds(10)));

        Assert.Empty(h.Events.OfType<Rejected>());
        Assert.Equal(TimeSpan.FromSeconds(10), h.Engine.Last!.Source.CueIn);
        Assert.Empty(h.Engine.Last!.Seeks);
    }

    [Fact]
    public void SeekTo_BeforeMarker_ThenPlay_PlaysFromTarget()
    {
        using var h = new Harness();
        var track = TestShow.Track("storm", markers: [Mark("привал", 30)]);
        var project = TestShow.Project("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [project], project.Id));
        h.Submit(new Play());
        h.Engine.End(h.Engine.Last!.Handle, StreamEndReason.StoppedByMarker, TimeSpan.FromSeconds(30));

        h.Submit(new SeekTo(TimeSpan.FromSeconds(10)));
        h.Submit(new Play());

        Assert.Equal(TransportStatus.Playing, h.Transport.Status);
        Assert.Equal(TimeSpan.FromSeconds(10), h.Engine.Last!.Source.CueIn);
        Assert.Contains(TransportCommand.Play, h.Engine.Last!.Transports);
    }

    [Fact]
    public void SeekTo_AfterEndActionPause_RestartsStreamAtTarget()
    {
        using var h = new Harness();
        var track = TestShow.Track("one", end: EndAction.Pause);
        var project = TestShow.Project("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [project], project.Id));
        h.Submit(new Play());
        h.Engine.End(h.Engine.Last!.Handle, StreamEndReason.Completed);

        h.Submit(new SeekTo(TimeSpan.FromSeconds(30)));

        Assert.Empty(h.Events.OfType<Rejected>());
        Assert.Equal(TimeSpan.FromSeconds(30), h.Engine.Last!.Source.CueIn);
    }

    [Fact]
    public void MarkerRepeatFromPrevious_LoopsSegment()
    {
        using var h = new Harness();
        var track = TestShow.Track("storm", markers: [Mark("a", 10), Mark("b", 20, MarkerAction.RepeatFromPrevious)]);
        var project = TestShow.Project("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [project], project.Id));
        h.Submit(new Play());

        h.Engine.End(h.Engine.Last!.Handle, StreamEndReason.StoppedByMarker, TimeSpan.FromSeconds(20));

        Assert.Equal(TransportStatus.Playing, h.Transport.Status);
        Assert.Equal(TimeSpan.FromSeconds(10), h.Engine.Last!.Source.CueIn);
        Assert.Equal("b", Assert.Single(h.Engine.Last!.Options.Markers).Name);

        h.Engine.End(h.Engine.Last!.Handle, StreamEndReason.StoppedByMarker, TimeSpan.FromSeconds(20));

        Assert.Equal(TransportStatus.Playing, h.Transport.Status);
        Assert.Equal(TimeSpan.FromSeconds(10), h.Engine.Last!.Source.CueIn);
    }

    [Fact]
    public void MarkerRepeatFromPrevious_SingleMarker_SeeksCueIn()
    {
        using var h = new Harness();
        var track = TestShow.Track("storm", markers: [Mark("b", 20, MarkerAction.RepeatFromPrevious)]);
        var project = TestShow.Project("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [project], project.Id));
        h.Submit(new Play());

        h.Engine.End(h.Engine.Last!.Handle, StreamEndReason.StoppedByMarker, TimeSpan.FromSeconds(20));

        Assert.Equal(TransportStatus.Playing, h.Transport.Status);
        Assert.Equal(TimeSpan.Zero, h.Engine.Last!.Source.CueIn);
        Assert.Equal("b", Assert.Single(h.Engine.Last!.Options.Markers).Name);
    }

    [Fact]
    public void MarkerRestart_LoopsFromCueIn()
    {
        using var h = new Harness();
        var track = TestShow.Track("storm", markers: [Mark("r", 20, MarkerAction.Restart)]);
        var project = TestShow.Project("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [project], project.Id));
        h.Submit(new Play());

        h.Engine.End(h.Engine.Last!.Handle, StreamEndReason.StoppedByMarker, TimeSpan.FromSeconds(20));

        Assert.Equal(TransportStatus.Playing, h.Transport.Status);
        Assert.Equal(TimeSpan.Zero, h.Engine.Last!.Source.CueIn);
        Assert.Equal("r", Assert.Single(h.Engine.Last!.Options.Markers).Name);
    }
}
