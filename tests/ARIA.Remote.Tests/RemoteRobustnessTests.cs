namespace Aria.Remote.Tests;

using System.Net.WebSockets;
using System.Text;
using Aria.Core.Runtime;

public sealed class RemoteRobustnessTests : IAsyncLifetime
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

    [Fact]
    public async Task NonPositiveSeq_IsRejected_AndConnectionSurvives()
    {
        using var client = new TestClient();
        await client.ConnectAsync(_host.WebsocketEndpoint, "secret");

        await client.SendAsync("""{"client":"pult-1","seq":0,"command":{"type":"create_playlist","name":"Zero"}}""");
        var rejected = await client.WaitForAsync(e => e.GetProperty("event").GetString() == "rejected", TimeSpan.FromSeconds(5));
        Assert.Equal("bad-seq", rejected.GetProperty("reason").GetString());

        await client.SendAsync("""{"client":"pult-1","seq":1,"command":{"type":"create_playlist","name":"One"}}""");
        await client.WaitForAsync(e => e.GetProperty("event").GetString() == "ack", TimeSpan.FromSeconds(5));
        Assert.Equal(WebSocketState.Open, client.Socket.State);
    }

    [Fact]
    public async Task FragmentedMessage_SplitInsideCyrillicChar_IsDecodedIntact()
    {
        using var client = new TestClient();
        await client.ConnectAsync(_host.WebsocketEndpoint, "secret");
        var bytes = Encoding.UTF8.GetBytes("""{"client":"pult-1","seq":1,"command":{"type":"create_playlist","name":"Сцена"}}""");
        var split = Encoding.UTF8.GetByteCount("""{"client":"pult-1","seq":1,"command":{"type":"create_playlist","name":"С""") + 1;

        await client.Socket.SendAsync(new ArraySegment<byte>(bytes, 0, split), WebSocketMessageType.Text, false, CancellationToken.None);
        await client.Socket.SendAsync(new ArraySegment<byte>(bytes, split, bytes.Length - split), WebSocketMessageType.Text, true, CancellationToken.None);

        await client.WaitForAsync(e => e.GetProperty("event").GetString() == "ack", TimeSpan.FromSeconds(5));
        var deadline = Environment.TickCount64 + 5_000;
        while (_bus.Snapshot().Show.Projects.IsEmpty && Environment.TickCount64 < deadline)
        {
            await Task.Delay(10);
        }
        Assert.Equal("Сцена", Assert.Single(_bus.Snapshot().Show.Projects).Name);
    }

    [Fact]
    public async Task OversizedMessage_ClosesConnection_WithoutCrashingHost()
    {
        using var client = new TestClient();
        await client.ConnectAsync(_host.WebsocketEndpoint, "secret");
        var chunk = new byte[64 * 1024];
        Array.Fill(chunk, (byte)'a');

        try
        {
            for (var i = 0; i < 40 && client.Socket.State == WebSocketState.Open; i++)
            {
                await client.Socket.SendAsync(new ArraySegment<byte>(chunk), WebSocketMessageType.Text, false, CancellationToken.None);
            }
        }
        catch (Exception e) when (e is WebSocketException or IOException)
        {
        }

        var deadline = Environment.TickCount64 + 5_000;
        while (client.Socket.State == WebSocketState.Open && Environment.TickCount64 < deadline)
        {
            await Task.Delay(20);
        }
        Assert.NotEqual(WebSocketState.Open, client.Socket.State);

        using var fresh = new TestClient();
        await fresh.ConnectAsync(_host.WebsocketEndpoint, "secret");
        Assert.Equal(WebSocketState.Open, fresh.Socket.State);
    }
}
