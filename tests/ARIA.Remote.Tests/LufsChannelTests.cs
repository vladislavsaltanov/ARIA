namespace Aria.Remote.Tests;

using Aria.Core.Playback;
using Aria.Core.Runtime;

public sealed class LufsChannelTests : IAsyncLifetime
{
    private readonly MeterMonitor _meters = new();
    private CommandBus _bus = null!;
    private RemoteHost _host = null!;

    public async Task InitializeAsync()
    {
        _bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Pumped);
        _host = new RemoteHost(_bus, new RemoteOptions("secret", TestPorts.Next()), meters: _meters);
        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        _bus.Dispose();
    }

    [Fact]
    public async Task LufsFrames_FlowToClients()
    {
        using var client = new TestClient();
        await client.ConnectAsync(_host.WebsocketEndpoint, "secret");

        _meters.Publish(-14.2);

        var frame = await client.WaitForAsync(e => e.GetProperty("event").GetString() == "lufs", TimeSpan.FromSeconds(5));
        Assert.InRange(frame.GetProperty("momentaryLufs").GetDouble(), -14.21, -14.19);
    }

    [Fact]
    public async Task NoLufsFrames_UntilPublished()
    {
        using var client = new TestClient();
        await client.ConnectAsync(_host.WebsocketEndpoint, "secret");
        await client.DrainUntilQuietAsync(TimeSpan.FromMilliseconds(400));

        Assert.True(await client.WaitSilenceAsync(TimeSpan.FromMilliseconds(600)), "lufs frames without publish");
    }
}
