namespace Aria.Remote.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;
using Aria.Core.Runtime;

public sealed class RemoteHostTests : IAsyncLifetime
{
    private CommandBus _bus = null!;
    private RemoteHost _host = null!;

    public async Task InitializeAsync()
    {
        _bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Pumped);
        _host = new RemoteHost(_bus, new RemoteOptions("secret", TestPorts.Next()));
        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        _bus.Dispose();
    }

    private TestClient Connected()
    {
        var client = new TestClient();
        client.ConnectAsync(_host.WebsocketEndpoint, "secret").GetAwaiter().GetResult();
        return client;
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        using var http = new HttpClient();

        var response = await http.GetAsync(new Uri(_host.HttpEndpoint, "health"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task WebSocket_WithoutUpgrade_BadToken_Returns401()
    {
        using var http = new HttpClient();

        var response = await http.GetAsync(new Uri(_host.WebsocketEndpoint.ToString().Replace("ws://", "http://") + "?token=wrong"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WebSocket_WithValidToken_Connects()
    {
        using var client = new TestClient();

        await client.ConnectAsync(_host.WebsocketEndpoint, "secret");

        Assert.Equal(WebSocketState.Open, client.Socket.State);
    }

    [Fact]
    public async Task Command_GetsAck_AndShowDelta()
    {
        using var client = Connected();

        await client.SendAsync("""{"client":"pult-1","seq":1,"command":{"type":"create_playlist","name":"Main"}}""");

        var ack = await client.WaitForAsync(e => e.GetProperty("event").GetString() == "ack", TimeSpan.FromSeconds(5));
        Assert.Equal(1, ack.GetProperty("seq").GetInt64());
        var delta = await client.WaitForAsync(e => e.GetProperty("event").GetString() == "delta" && e.GetProperty("partition").GetString() == "show", TimeSpan.FromSeconds(5));
        Assert.Equal(1, delta.GetProperty("version").GetInt64());
        Assert.Single(_bus.Snapshot().Show.Playlists);
    }

    [Fact]
    public async Task InvalidCommand_AckThenRejected()
    {
        using var client = Connected();

        await client.SendAsync("""{"client":"pult-1","seq":1,"command":{"type":"set_master_gain","gain_db":50}}""");

        await client.WaitForAsync(e => e.GetProperty("event").GetString() == "ack", TimeSpan.FromSeconds(5));
        var rejected = await client.WaitForAsync(e => e.GetProperty("event").GetString() == "rejected", TimeSpan.FromSeconds(5));
        Assert.Equal("gain-out-of-range", rejected.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task SetMuted_TogglesMixerMuted()
    {
        using var client = Connected();

        await client.SendAsync("""{"client":"pult-1","seq":1,"command":{"type":"set_muted","muted":true}}""");

        await client.WaitForAsync(e => e.GetProperty("event").GetString() == "ack", TimeSpan.FromSeconds(5));
        var delta = await client.WaitForAsync(
            e => e.GetProperty("event").GetString() == "delta" && e.GetProperty("partition").GetString() == "mixer",
            TimeSpan.FromSeconds(5));
        Assert.True(delta.GetProperty("state").GetProperty("muted").GetBoolean());
        Assert.True(_bus.Snapshot().Mixer.Muted);
    }

    [Fact]
    public async Task DuplicateSeq_DeliveredOnce()
    {
        using var client = Connected();
        var frame = """{"client":"pult-1","seq":1,"command":{"type":"create_playlist","name":"Main"}}""";

        await client.SendAsync(frame);
        await client.WaitForAsync(e => e.GetProperty("event").GetString() == "ack", TimeSpan.FromSeconds(5));
        await client.DrainUntilQuietAsync(TimeSpan.FromMilliseconds(400));

        await client.SendAsync(frame);

        Assert.True(await client.WaitSilenceAsync(TimeSpan.FromMilliseconds(700)), "duplicate produced frames");
        Assert.Single(_bus.Snapshot().Show.Playlists);
        Assert.Equal(1, _bus.Snapshot().ShowVersion);
    }

    [Fact]
    public async Task UnknownType_IsRejected()
    {
        using var client = Connected();

        await client.SendAsync("""{"client":"pult-1","seq":1,"command":{"type":"load_show"}}""");

        var rejected = await client.WaitForAsync(e => e.GetProperty("event").GetString() == "rejected", TimeSpan.FromSeconds(5));
        Assert.Equal("unknown-command", rejected.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task MalformedJson_GetsError_ConnectionSurvives()
    {
        using var client = Connected();

        await client.SendAsync("{ this is not json");
        await client.WaitForAsync(e => e.GetProperty("event").GetString() == "error", TimeSpan.FromSeconds(5));

        await client.SendAsync("""{"client":"pult-1","seq":1,"command":{"type":"play"}}""");
        var ack = await client.WaitForAsync(e => e.GetProperty("event").GetString() == "ack", TimeSpan.FromSeconds(5));
        Assert.Equal(1, ack.GetProperty("seq").GetInt64());
    }

    [Fact]
    public async Task Deltas_Broadcast_Rejected_RoutedToOwner()
    {
        using var clientA = Connected();
        using var clientB = Connected();

        await clientA.SendAsync("""{"client":"pult-a","seq":1,"command":{"type":"create_playlist","name":"Main"}}""");
        await clientA.WaitForAsync(e => e.GetProperty("event").GetString() == "ack", TimeSpan.FromSeconds(5));
        await clientA.WaitForAsync(e => e.GetProperty("event").GetString() == "delta" && e.GetProperty("partition").GetString() == "show", TimeSpan.FromSeconds(5));
        await clientB.WaitForAsync(e => e.GetProperty("event").GetString() == "delta" && e.GetProperty("partition").GetString() == "show", TimeSpan.FromSeconds(5));
        await clientB.DrainUntilQuietAsync(TimeSpan.FromMilliseconds(400));

        await clientA.SendAsync("""{"client":"pult-a","seq":2,"command":{"type":"set_master_gain","gain_db":50}}""");
        var rejectedA = await clientA.WaitForAsync(e => e.GetProperty("event").GetString() == "rejected", TimeSpan.FromSeconds(5));
        Assert.Equal("gain-out-of-range", rejectedA.GetProperty("reason").GetString());
        Assert.True(await clientB.WaitSilenceAsync(TimeSpan.FromMilliseconds(700)), "rejected leaked to other client");
    }
}

public sealed class RemoteHostAuthTests : IAsyncLifetime
{
    private readonly StubCredentials _credentials = new("ARIA-STAGE", "pass-123");
    private CommandBus _bus = null!;
    private RemoteHost _host = null!;

    public async Task InitializeAsync()
    {
        _bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Pumped);
        _host = new RemoteHost(_bus, new RemoteOptions("legacy-token", TestPorts.Next(), Credentials: _credentials));
        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        _bus.Dispose();
    }

    private async Task<HttpResponseMessage> AuthAsync(string identifier, string password)
    {
        using var http = new HttpClient();
        var response = await http.PostAsJsonAsync(new Uri(_host.HttpEndpoint, "auth"), new { identifier, password });
        return response;
    }

    private async Task<string> AuthTokenAsync()
    {
        var response = await AuthAsync("ARIA-STAGE", "pass-123");
        var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        return body!.RootElement.GetProperty("token").GetString()!;
    }

    [Fact]
    public async Task Auth_RightPair_ReturnsToken()
    {
        var response = await AuthAsync("ARIA-STAGE", "pass-123");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(64, body!.RootElement.GetProperty("token").GetString()!.Length);
    }

    [Fact]
    public async Task Auth_WrongPassword_Returns401()
    {
        var response = await AuthAsync("ARIA-STAGE", "wrong");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Auth_WrongIdentifier_Returns401()
    {
        var response = await AuthAsync("ARIA-OTHER", "pass-123");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Auth_MalformedBody_Returns401()
    {
        using var http = new HttpClient();
        var response = await http.PostAsync(new Uri(_host.HttpEndpoint, "auth"), new StringContent("not json"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Auth_Failure_TakesAtLeastRateLimitDelay()
    {
        var started = Environment.TickCount64;
        await AuthAsync("ARIA-STAGE", "wrong");

        Assert.True(Environment.TickCount64 - started >= 250, "no rate-limit delay on failed auth");
    }

    [Fact]
    public async Task Ws_LegacyToken_Rejected_WhenCredentialsPresent()
    {
        using var client = new TestClient();

        await Assert.ThrowsAnyAsync<WebSocketException>(() => client.ConnectAsync(_host.WebsocketEndpoint, "legacy-token"));
    }

    [Fact]
    public async Task Ws_WithIssuedToken_Connects()
    {
        var token = await AuthTokenAsync();
        using var client = new TestClient();

        await client.ConnectAsync(_host.WebsocketEndpoint, token);

        Assert.Equal(WebSocketState.Open, client.Socket.State);
    }

    [Fact]
    public async Task Ws_WithUnknownToken_Rejected()
    {
        using var client = new TestClient();
        var unknown = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

        await Assert.ThrowsAnyAsync<WebSocketException>(() => client.ConnectAsync(_host.WebsocketEndpoint, unknown));
    }
}

public sealed class RemoteHostBindTests
{
    private static CommandBus Bus()
    {
        return new CommandBus(new ShowController(new StubEngine()), BusMode.Pumped);
    }

    [Fact]
    public async Task NonLoopback_WithoutCredentials_Throws()
    {
        using var bus = Bus();
        await using var host = new RemoteHost(bus, new RemoteOptions("secret", TestPorts.Next(), BindAddress: IPAddress.Any));

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
    }

    [Fact]
    public async Task NonLoopback_WithCredentials_Starts()
    {
        using var bus = Bus();
        await using var host = new RemoteHost(
            bus,
            new RemoteOptions("secret", TestPorts.Next(), BindAddress: IPAddress.Any, Credentials: new StubCredentials("ARIA-STAGE", "pass")));
        try
        {
            await host.StartAsync();
            using var http = new HttpClient();

            var response = await http.GetAsync(new Uri(host.HttpEndpoint, "health"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    [Fact]
    public async Task Auth_OverLoopback_WithCredentials_IssuesToken()
    {
        using var bus = Bus();
        await using var host = new RemoteHost(
            bus,
            new RemoteOptions("secret", TestPorts.Next(), BindAddress: IPAddress.Any, Credentials: new StubCredentials("ARIA-STAGE", "pass")));
        try
        {
            await host.StartAsync();
            using var http = new HttpClient();

            var response = await http.PostAsJsonAsync(new Uri(host.HttpEndpoint, "auth"), new { identifier = "ARIA-STAGE", password = "pass" });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await host.DisposeAsync();
        }
    }
}

public sealed class StubCredentials : IRemoteCredentials
{
    public StubCredentials(string identifier, string password)
    {
        Identifier = identifier;
        Password = password;
    }

    public string Identifier { get; }

    public string Password { get; private set; }

    public bool Verify(string identifier, string password)
    {
        return string.Equals(identifier, Identifier, StringComparison.Ordinal)
            && string.Equals(password, Password, StringComparison.Ordinal);
    }
}
