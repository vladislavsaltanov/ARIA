namespace Aria.Remote.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;

public sealed class SeekCodecTests : IAsyncLifetime
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
    public async Task SeekTo_SeeksCurrentTrack()
    {
        var track = new Track(TrackId.New(), "/audio/live.flac", "трек", TimeSpan.FromMinutes(3), new TrackDefaults());
        var playlist = new Playlist(PlaylistId.New(), "Main", [new PlaylistEntry(EntryId.New(), track.Id, null)]);
        _bus.Submit(new ClientId("setup"), 1, new LoadShow([track], [playlist], playlist.Id));
        _bus.Submit(new ClientId("setup"), 2, new PlayTrack(track.Id));
        using var client = Connected();

        await client.SendAsync("{\"client\":\"pult-1\",\"seq\":1,\"command\":{\"type\":\"seek_to\",\"position_ms\":30000}}");

        await client.WaitForAsync(e => e.GetProperty("event").GetString() == "ack", TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SeekTo_WithoutTrack_IsRejected()
    {
        using var client = Connected();

        await client.SendAsync("{\"client\":\"pult-1\",\"seq\":1,\"command\":{\"type\":\"seek_to\",\"position_ms\":30000}}");

        await client.WaitForAsync(e => e.GetProperty("event").GetString() == "ack", TimeSpan.FromSeconds(5));
        var rejected = await client.WaitForAsync(e => e.GetProperty("event").GetString() == "rejected", TimeSpan.FromSeconds(5));
        Assert.Equal("nothing-to-seek", rejected.GetProperty("reason").GetString());
    }
}
