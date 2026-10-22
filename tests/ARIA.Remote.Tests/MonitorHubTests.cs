namespace Aria.Remote.Tests;

using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.Runtime;
using Aria.Remote.Monitor;

public sealed class MonitorHubTests : IAsyncLifetime
{
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

    private async Task<string> TokenAsync(string password)
    {
        var response = await _http.PostAsJsonAsync(new Uri(_host.HttpEndpoint, "auth"), new { identifier = _pair.Identifier, password });
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("token").GetString()!;
    }

    private async Task<(TestClient Client, string Id)> ConnectAsync(string? password = null)
    {
        var client = new TestClient();
        _clients.Add(client);
        await client.ConnectAsync(MonitorSocket, await TokenAsync(password ?? _pair.ListenPassword));
        var welcome = await client.WaitForAsync(f => Type(f) == "welcome", TimeSpan.FromSeconds(5));
        return (client, welcome.GetProperty("listener").GetString()!);
    }

    private static string? Type(JsonElement frame) =>
        frame.TryGetProperty("type", out var type) ? type.GetString() : null;

    private async Task PollAsync(Func<bool> condition, string message)
    {
        var deadline = Environment.TickCount64 + 5000;
        while (!condition())
        {
            Assert.True(Environment.TickCount64 < deadline, message);
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task Connect_SendsWelcome()
    {
        var client = new TestClient();
        _clients.Add(client);
        await client.ConnectAsync(MonitorSocket, await TokenAsync(_pair.ListenPassword));

        var welcome = await client.WaitForAsync(f => Type(f) == "welcome", TimeSpan.FromSeconds(5));

        Assert.StartsWith("L", welcome.GetProperty("listener").GetString());
        Assert.Equal("listen", welcome.GetProperty("role").GetString());
        Assert.Equal(48000, welcome.GetProperty("sampleRate").GetInt32());
    }

    [Fact]
    public async Task Ping_ReturnsPong_T1LeT2_EchoT0()
    {
        var (client, _) = await ConnectAsync();

        await client.SendAsync("{\"type\":\"ping\",\"t0\":123.5}");
        var pong = await client.WaitForAsync(f => Type(f) == "pong", TimeSpan.FromSeconds(5));

        Assert.Equal(123.5, pong.GetProperty("t0").GetDouble());
        var t1 = pong.GetProperty("t1").GetInt64();
        var t2 = pong.GetProperty("t2").GetInt64();
        Assert.True(t1 > 0);
        Assert.True(t1 <= t2, $"t1 {t1} > t2 {t2}");
    }

    [Fact]
    public async Task Hello_SetsNameAndLevels()
    {
        var (client, id) = await ConnectAsync();

        await client.SendAsync("{\"type\":\"hello\",\"name\":\"Барабаны\",\"levels\":{\"backingDb\":-6,\"backingOn\":true,\"clickDb\":-3,\"clickOn\":false}}");

        await PollAsync(() => _host.Monitor!.Listeners.Any(l => l.Id == id && l.Name == "Барабаны"), "name not stored");
        var profile = _host.Monitor!.Listeners.Single(l => l.Id == id);
        Assert.Equal(new ListenerLevels(-6, true, -3, false), profile.Levels);
        Assert.Equal(RemoteRole.Listen, profile.Role);
    }

    [Fact]
    public async Task ClientLevels_UpdateRegistry()
    {
        var (client, id) = await ConnectAsync();

        await client.SendAsync("{\"type\":\"levels\",\"backingDb\":-12,\"backingOn\":false,\"clickDb\":2,\"clickOn\":true}");

        await PollAsync(() => _host.Monitor!.Listeners.Single(l => l.Id == id).Levels == new ListenerLevels(-12, false, 2, true), "levels not stored");
    }

    [Fact]
    public async Task Stats_StoredInRegistry()
    {
        var (client, id) = await ConnectAsync();

        await client.SendAsync("{\"type\":\"stats\",\"offsetUs\":1834,\"uncertaintyUs\":900,\"rttUs\":6100,\"outputLatencyMs\":21.3,\"trimMs\":4,\"state\":\"locked\"}");

        await PollAsync(() => _host.Monitor!.Listeners.Single(l => l.Id == id).Stats == new ListenerStats(1834, 900, 6100, 21.3, 4, "locked"), "stats not stored");
    }

    [Fact]
    public async Task SetLevels_PushesToClient()
    {
        var (client, id) = await ConnectAsync();

        _host.Monitor!.SetLevels(id, new ListenerLevels(-10, true, 2, false));
        var levels = await client.WaitForAsync(f => Type(f) == "levels", TimeSpan.FromSeconds(5));

        Assert.Equal(-10, levels.GetProperty("backingDb").GetDouble());
        Assert.False(levels.GetProperty("clickOn").GetBoolean());
    }

    [Fact]
    public async Task Kick_SendsKickedAndCloses()
    {
        var (client, id) = await ConnectAsync();

        _host.Monitor!.Kick(id);

        await client.WaitForAsync(f => Type(f) == "kicked", TimeSpan.FromSeconds(5));
        await PollAsync(() => _host.Monitor!.Listeners.All(l => l.Id != id), "kicked listener still registered");
    }

    [Fact]
    public async Task RevokeListenTokens_SendsRevokedForListenOnly()
    {
        var (listen, listenId) = await ConnectAsync();
        var (_, controlId) = await ConnectAsync(_pair.Password);

        _host.RevokeListenTokens();

        await listen.WaitForAsync(f => Type(f) == "revoked", TimeSpan.FromSeconds(5));
        await PollAsync(() => _host.Monitor!.Listeners.All(l => l.Id != listenId), "revoked listener still registered");
        Assert.Contains(_host.Monitor!.Listeners, l => l.Id == controlId);
    }

    [Fact]
    public async Task SeventeenthListener_Rejected()
    {
        for (var index = 0; index < MonitorHub.MaxListeners; index++)
        {
            await ConnectAsync();
        }
        using var extra = new ClientWebSocket();
        await extra.ConnectAsync(new Uri($"{MonitorSocket}?token={await TokenAsync(_pair.ListenPassword)}"), CancellationToken.None);

        var buffer = new byte[1024];
        var result = await extra.ReceiveAsync(buffer, new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);

        Assert.Equal(WebSocketMessageType.Close, result.MessageType);
        Assert.Equal((WebSocketCloseStatus)1013, result.CloseStatus);
        Assert.Equal("too-many-listeners", result.CloseStatusDescription);
    }

    [Fact]
    public async Task MonitorSocket_WithoutToken_Unauthorized()
    {
        var response = await _http.GetAsync(new Uri(_host.HttpEndpoint, "monitor/ws"));

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Kick_SlowClient_StillReceivesKickedThenGracefulClose()
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"{MonitorSocket}?token={await TokenAsync(_pair.ListenPassword)}"), CancellationToken.None);
        var buffer = new byte[4096];
        var welcome = await socket.ReceiveAsync(buffer, CancellationToken.None);
        using var welcomeBody = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, welcome.Count));
        var id = welcomeBody.RootElement.GetProperty("listener").GetString()!;

        _host.Monitor!.Kick(id);
        await Task.Delay(700);

        var frames = new List<string>();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
            if (result.MessageType == WebSocketMessageType.Text)
            {
                frames.Add(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
        }
        while (result.MessageType != WebSocketMessageType.Close);

        Assert.Contains(frames, frame => Type(JsonDocument.Parse(frame).RootElement) is "kicked");
        Assert.Equal(WebSocketCloseStatus.NormalClosure, result.CloseStatus);
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
    }
}
