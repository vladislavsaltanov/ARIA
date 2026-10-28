namespace Aria.Remote.Tests;

using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Aria.Audio;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.Runtime;
using Aria.Core.State;
using Aria.Remote.Monitor;

public sealed class MonitorContractTests : IAsyncLifetime
{
    private static readonly long T0 = Stopwatch.Frequency * 100;
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(5);

    private readonly string _authPath = Path.Combine(Path.GetTempPath(), $"aria-auth-{Guid.NewGuid():N}.json");
    private readonly HttpClient _http = new();
    private readonly List<TestClient> _clients = [];
    private CommandBus _bus = null!;
    private RemoteHost _host = null!;
    private RemoteCredentials _pair = null!;

    public async Task InitializeAsync()
    {
        var store = new RemoteCredentialsStore(_authPath);
        _pair = store.Load();
        _bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Pumped);
        _host = new RemoteHost(_bus, new RemoteOptions("unused", TestPorts.Next(), store), monitorFeed: MonitorFeeds.Create(new FakeStamps(), new PlaybackMonitor(), []));
        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }
        _http.Dispose();
        await _host.DisposeAsync();
        _bus.Dispose();
        File.Delete(_authPath);
    }

    private Uri MonitorSocket => new($"ws://127.0.0.1:{_host.HttpEndpoint.Port}/monitor/ws");

    [Fact]
    public async Task Welcome_MatchesFixture()
    {
        var (_, welcome) = await ConnectAsync();

        AssertMatchesFixture("welcome", welcome);
    }

    [Fact]
    public async Task Pong_MatchesFixture()
    {
        var (client, _) = await ConnectAsync();

        await client.SendAsync("{\"type\":\"ping\",\"t0\":123.5}");
        var pong = await client.WaitForAsync(f => Type(f) == "pong", FrameTimeout);

        AssertMatchesFixture("pong", pong);
    }

    [Fact]
    public async Task Timeline_MatchesFixture()
    {
        var (client, _) = await ConnectAsync();

        _host.Monitor!.BroadcastTimeline(BuildTimeline(testClick: false));
        var timeline = await client.WaitForAsync(f => Type(f) == "timeline" && f.GetProperty("voices").GetArrayLength() == 2 && f.GetProperty("test").ValueKind == JsonValueKind.Null, FrameTimeout);

        AssertMatchesFixture("timeline", timeline);
    }

    [Fact]
    public async Task TimelineWithTestClick_MatchesFixture()
    {
        var (client, _) = await ConnectAsync();

        _host.Monitor!.BroadcastTimeline(BuildTimeline(testClick: true));
        var timeline = await client.WaitForAsync(f => Type(f) == "timeline" && f.GetProperty("test").ValueKind == JsonValueKind.Object, FrameTimeout);

        AssertMatchesFixture("timeline-test", timeline);
    }

    [Fact]
    public async Task Status_MatchesFixture()
    {
        var (client, _) = await ConnectAsync();

        _host.Monitor!.BroadcastStatus(true);
        var status = await client.WaitForAsync(f => Type(f) == "status", FrameTimeout);

        AssertMatchesFixture("status", status);
    }

    [Fact]
    public async Task Levels_MatchesFixture()
    {
        var (client, welcome) = await ConnectAsync();

        _host.Monitor!.SetLevels(welcome.GetProperty("listener").GetString()!, new ListenerLevels(-6, true, -3, false));
        var levels = await client.WaitForAsync(f => Type(f) == "levels", FrameTimeout);

        AssertMatchesFixture("levels", levels);
    }

    [Fact]
    public async Task Kicked_MatchesFixture()
    {
        var (client, welcome) = await ConnectAsync();

        _host.Monitor!.Kick(welcome.GetProperty("listener").GetString()!);
        var kicked = await client.WaitForAsync(f => Type(f) == "kicked", FrameTimeout);

        AssertMatchesFixture("kicked", kicked);
    }

    [Fact]
    public async Task Revoked_MatchesFixture()
    {
        var (client, _) = await ConnectAsync();

        _host.RevokeListenTokens();
        var revoked = await client.WaitForAsync(f => Type(f) == "revoked", FrameTimeout);

        AssertMatchesFixture("revoked", revoked);
    }

    [Fact]
    public void Comparison_ReportsPathOfEveryDifference()
    {
        using var fixture = JsonDocument.Parse("{\"a\":1,\"b\":{\"c\":null,\"d\":[1,2]},\"e\":\"x\"}");
        using var host = JsonDocument.Parse("{\"a\":\"1\",\"b\":{\"c\":{},\"d\":[1]},\"f\":true}");

        var differences = ShapeDifferences(fixture.RootElement, host.RootElement, "$");

        Assert.Equal(
            new[]
            {
                "$.a: fixture Number, host String",
                "$.b.c: fixture Null, host Object",
                "$.b.d: fixture has 2 items, host has 1",
                "$.e: present in fixture, missing in host",
                "$.f: present in host, missing in fixture",
            },
            differences);
    }

    [Fact]
    public void Comparison_TreatsTrueAndFalseAsSameShape()
    {
        using var fixture = JsonDocument.Parse("{\"output\":true}");
        using var host = JsonDocument.Parse("{\"output\":false}");

        Assert.Empty(ShapeDifferences(fixture.RootElement, host.RootElement, "$"));
    }

    private static string? Type(JsonElement frame) =>
        frame.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String ? type.GetString() : null;

    private async Task<string> TokenAsync()
    {
        var response = await _http.PostAsJsonAsync(new Uri(_host.HttpEndpoint, "auth"), new { identifier = _pair.Identifier, password = _pair.ListenPassword });
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("token").GetString()!;
    }

    private async Task<(TestClient Client, JsonElement Welcome)> ConnectAsync()
    {
        var client = new TestClient();
        _clients.Add(client);
        await client.ConnectAsync(MonitorSocket, await TokenAsync());
        var welcome = await client.WaitForAsync(f => Type(f) == "welcome", FrameTimeout);
        return (client, welcome);
    }

    private static void AssertMatchesFixture(string name, JsonElement hostFrame)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "monitor", $"{name}.json");
        Assert.True(File.Exists(path), $"fixture {name}.json is missing: expected at {path}");
        using var fixture = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var differences = ShapeDifferences(fixture.RootElement, hostFrame, "$");
        if (Type(fixture.RootElement) != Type(hostFrame))
        {
            differences.Add($"$.type: fixture '{Type(fixture.RootElement)}', host '{Type(hostFrame)}'");
        }
        Assert.True(
            differences.Count == 0,
            $"fixture {name}.json differs from the frame sent by the host:{Environment.NewLine}{string.Join(Environment.NewLine, differences)}{Environment.NewLine}host frame: {hostFrame.GetRawText()}");
    }

    private static List<string> ShapeDifferences(JsonElement fixture, JsonElement host, string path)
    {
        var differences = new List<string>();
        if (KindOf(fixture) != KindOf(host))
        {
            differences.Add($"{path}: fixture {KindOf(fixture)}, host {KindOf(host)}");
            return differences;
        }
        switch (fixture.ValueKind)
        {
            case JsonValueKind.Object:
                var fixtureNames = fixture.EnumerateObject().Select(property => property.Name).ToList();
                var hostNames = host.EnumerateObject().Select(property => property.Name).ToList();
                foreach (var name in fixtureNames.Union(hostNames).Order(StringComparer.Ordinal))
                {
                    if (!hostNames.Contains(name))
                    {
                        differences.Add($"{path}.{name}: present in fixture, missing in host");
                    }
                    else if (!fixtureNames.Contains(name))
                    {
                        differences.Add($"{path}.{name}: present in host, missing in fixture");
                    }
                    else
                    {
                        differences.AddRange(ShapeDifferences(fixture.GetProperty(name), host.GetProperty(name), $"{path}.{name}"));
                    }
                }
                break;
            case JsonValueKind.Array:
                if (fixture.GetArrayLength() != host.GetArrayLength())
                {
                    differences.Add($"{path}: fixture has {fixture.GetArrayLength()} items, host has {host.GetArrayLength()}");
                    break;
                }
                for (var index = 0; index < fixture.GetArrayLength(); index++)
                {
                    differences.AddRange(ShapeDifferences(fixture[index], host[index], $"{path}[{index}]"));
                }
                break;
        }
        return differences;
    }

    private static JsonValueKind KindOf(JsonElement element) =>
        element.ValueKind == JsonValueKind.False ? JsonValueKind.True : element.ValueKind;

    private static TimelineMessage BuildTimeline(bool testClick)
    {
        var song = new Track(TrackId.New(), "/audio/song.wav", "Song", TimeSpan.FromMinutes(3), new TrackDefaults(Grid: new BeatGrid(120, 4, TimeSpan.Zero)));
        var guide = new Track(TrackId.New(), "/audio/guide.wav", "Guide", TimeSpan.FromMinutes(3), new TrackDefaults());
        var upcoming = new Track(TrackId.New(), "/audio/next.wav", "Next song", TimeSpan.FromMinutes(3), new TrackDefaults());
        var bindings = new Dictionary<int, DeckContent>
        {
            [1] = Deck(song, TimeSpan.FromSeconds(2)),
            [2] = Deck(song, TimeSpan.Zero, guide.Id),
        };
        var builder = new MonitorTimelineBuilder(handle => bindings.GetValueOrDefault(handle.Value), new TrackListSources(() => [song, guide, upcoming]), _ => true);
        var report = new MixBlockReport { OutputFrame = 96000, Count = 2, TestClick = testClick };
        report.Voices[0] = new VoiceReport
        {
            Tag = 1,
            SourceFrame = 0,
            Active = true,
            EndSourceFrame = -1,
            GainDb = -3,
            FadeFrom = 0,
            FadeTo = 1,
            FadeProgress = 1,
            FadeCurve = FadeCurve.Linear,
        };
        report.Voices[1] = new VoiceReport
        {
            Tag = 2,
            SourceFrame = 24000,
            Active = true,
            Ended = true,
            EndSourceFrame = 480000,
            StopWhenDone = true,
            Epoch = 1,
            FadeFrom = 1,
            FadeTo = 0,
            FadeProgress = 0.25,
            FadeFrames = 38400,
            FadeCurve = FadeCurve.Logarithmic,
        };
        builder.Apply(report);
        builder.SetTransport(new TransportState(TransportStatus.Playing, Deck(song, TimeSpan.Zero), Deck(upcoming, TimeSpan.FromSeconds(0.5)), []));
        return builder.Build(Clock(), T0Us + 2_000_000, force: true)!;
    }

    private static long T0Us => HostClock.TicksToUs(T0);

    private static OutputClock Clock()
    {
        var clock = new OutputClock(48000);
        clock.Observe(new OutputStamp(0, T0, 0, 0));
        clock.Observe(new OutputStamp(48000, T0 + Stopwatch.Frequency, 0, 0));
        return clock;
    }

    private static DeckContent Deck(Track track, TimeSpan cueIn, TrackId? backing = null) =>
        new(EntryId.New(), track.Id, track.DefaultName, null, EndAction.Stop, track.Duration, cueIn, null, track.Defaults.Grid, default, backing);
}
