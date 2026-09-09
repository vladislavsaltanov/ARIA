namespace Aria.Remote.Tests;

using System.Net;
using System.Net.WebSockets;
using Aria.Core.Runtime;

public sealed class RemoteHostTests : IAsyncLifetime
{
    private CommandBus _bus = null!;
    private RemoteHost _host = null!;

    public async Task InitializeAsync()
    {
        _bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Pumped);
        _host = new RemoteHost(_bus, new RemoteOptions("secret"));
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
