namespace Aria.Remote.Tests;

using System.Text.Json;
using Aria.Audio;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.State;
using Aria.Remote.Monitor;

public sealed class MonitorPublisherTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public void Publisher_BroadcastsTimeline()
    {
        var track = new Track(TrackId.New(), "/audio/song.wav", "song", TimeSpan.FromMinutes(3), new TrackDefaults());
        var bindings = new PlaybackMonitor();
        bindings.Bind(new StreamHandle(1), new DeckContent(null, track.Id, "song", null, EndAction.Stop, track.Duration, TimeSpan.Zero));
        var ring = new MixReportRing();
        var hub = new MonitorHub(Json);
        using var publisher = new MonitorPublisher(MonitorFeeds.Create(new FakeStamps(), bindings, [track], ring), hub, _ => true);
        Thread.Sleep(30);
        publisher.Tick(HostClock.NowUs());
        Thread.Sleep(30);

        ring.Publish(4096, 0, [new VoiceReport { Tag = 1, SourceFrame = 100, Active = true, FadeTo = 1, FadeProgress = 1 }], false, false);
        publisher.Tick(HostClock.NowUs());

        Assert.NotNull(hub.LatestTimeline);
        using var timeline = JsonDocument.Parse(hub.LatestTimeline);
        Assert.Equal("timeline", timeline.RootElement.GetProperty("type").GetString());
        Assert.Equal(track.Id.Value, timeline.RootElement.GetProperty("voices")[0].GetProperty("track").GetGuid());
    }

    [Fact]
    public void Publisher_NoStamps2s_BroadcastsOutputFalse()
    {
        var stamps = new FakeStamps { Available = false };
        var hub = new MonitorHub(Json);
        using var publisher = new MonitorPublisher(MonitorFeeds.Create(stamps, new PlaybackMonitor(), []), hub, _ => true);
        var now = HostClock.NowUs();

        publisher.Tick(now);
        publisher.Tick(now + 2_100_000);

        Assert.NotNull(hub.LatestStatus);
        Assert.Contains("\"output\":false", hub.LatestStatus);

        stamps.Available = true;
        publisher.Tick(now + 2_200_000);
        Assert.Contains("\"output\":true", hub.LatestStatus);
    }

    [Fact]
    public void Publisher_ExceptionInBuild_LoopSurvives()
    {
        var faults = new List<Exception>();
        var throwing = true;
        var track = new Track(TrackId.New(), "/audio/song.wav", "song", TimeSpan.FromMinutes(3), new TrackDefaults());
        var ring = new MixReportRing();
        var hub = new MonitorHub(Json);
        var feed = MonitorFeeds.Create(new FakeStamps(), new PlaybackMonitor(), [track], ring);
        using var publisher = new MonitorPublisher(feed, hub, _ => throwing ? throw new InvalidOperationException("boom") : true, faults.Add);
        feed.Bindings.Bind(new StreamHandle(1), new DeckContent(null, track.Id, "song", null, EndAction.Stop, track.Duration, TimeSpan.Zero, Backing: track.Id));
        Thread.Sleep(30);
        publisher.Tick(HostClock.NowUs());
        Thread.Sleep(30);
        ring.Publish(4096, 0, [new VoiceReport { Tag = 1, Active = true, FadeTo = 1, FadeProgress = 1 }], false, false);

        publisher.Tick(HostClock.NowUs());
        Assert.NotEmpty(faults);

        throwing = false;
        ring.Publish(4608, 0, [new VoiceReport { Tag = 1, SourceFrame = 512, Active = true, FadeTo = 1, FadeProgress = 1 }], false, false);
        publisher.Tick(HostClock.NowUs());
        Assert.NotNull(hub.LatestTimeline);
    }
}
