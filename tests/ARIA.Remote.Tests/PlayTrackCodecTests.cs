namespace Aria.Remote.Tests;

using System.Text.Json;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;
using Aria.Core.State;

public sealed class PlayTrackCodecTests : IAsyncLifetime
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
    public async Task PlayTrack_StartsTrackImmediately()
    {
        var track = new Track(TrackId.New(), "/audio/live.flac", "трек (лайв)", TimeSpan.FromMinutes(3), new TrackDefaults());
        var playlist = new Playlist(PlaylistId.New(), "Main", [new PlaylistEntry(EntryId.New(), track.Id, null)]);
        _bus.Submit(new ClientId("setup"), 1, new LoadShow([track], [playlist], playlist.Id));
        using var client = Connected();

        await client.SendAsync("{\"client\":\"pult-1\",\"seq\":1,\"command\":{\"type\":\"play_track\",\"track\":\"" + track.Id.Value + "\"}}");

        await client.WaitForAsync(e => e.GetProperty("event").GetString() == "ack", TimeSpan.FromSeconds(5));
        var delta = await client.WaitForAsync(
            e => e.GetProperty("event").GetString() == "delta" && e.GetProperty("partition").GetString() == "transport",
            TimeSpan.FromSeconds(5));
        Assert.Equal(track.Id.Value.ToString(), delta.GetProperty("state").GetProperty("current").GetProperty("trackId").GetString());
        Assert.Equal(track.Id, _bus.Snapshot().Transport.Current!.TrackId);
    }

    [Fact]
    public async Task PlayTrack_UnknownTrack_IsRejected()
    {
        using var client = Connected();

        await client.SendAsync("{\"client\":\"pult-1\",\"seq\":1,\"command\":{\"type\":\"play_track\",\"track\":\"" + Guid.NewGuid() + "\"}}");

        await client.WaitForAsync(e => e.GetProperty("event").GetString() == "ack", TimeSpan.FromSeconds(5));
        var rejected = await client.WaitForAsync(e => e.GetProperty("event").GetString() == "rejected", TimeSpan.FromSeconds(5));
        Assert.Equal("unknown-track", rejected.GetProperty("reason").GetString());
    }
}
