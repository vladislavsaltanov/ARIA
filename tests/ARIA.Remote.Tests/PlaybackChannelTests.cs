namespace Aria.Remote.Tests;

using System.Text.Json;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.Runtime;
using Aria.Core.State;

public sealed class PlaybackChannelTests : IAsyncLifetime
{
    private readonly PlaybackMonitor _monitor = new();
    private CommandBus _bus = null!;
    private RemoteHost _host = null!;

    public async Task InitializeAsync()
    {
        _bus = new CommandBus(new ShowController(new StubEngine(), _monitor), BusMode.Pumped);
        _host = new RemoteHost(_bus, new RemoteOptions("secret"), _monitor);
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

    private async Task LoadShowWithPlaylistAsync()
    {
        var trackId = TrackId.New();
        var playlist = new Playlist(
            PlaylistId.New(),
            "Main",
            [new PlaylistEntry(EntryId.New(), trackId)]);
        var track = new Track(trackId, "a.wav", "Track", TimeSpan.FromSeconds(10), new TrackDefaults());
        _bus.Submit(new ClientId("setup"), 1, new LoadShow([track], [playlist], playlist.Id));
        _bus.Submit(new ClientId("setup"), 2, new CreatePlaylist("Second"));
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (_bus.Snapshot().Show.Playlists.Length < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
        Assert.Equal(2, _bus.Snapshot().Show.Playlists.Length);
    }

    [Fact]
    public async Task SnapshotOnConnect()
    {
        await LoadShowWithPlaylistAsync();
        using var client = Connected();

        var snapshot = await client.WaitForAsync(
            e => e.GetProperty("event").GetString() == "snapshot",
            TimeSpan.FromSeconds(5));

        Assert.Equal(JsonValueKind.Number, snapshot.GetProperty("show").GetProperty("version").ValueKind);
        Assert.Equal(JsonValueKind.Number, snapshot.GetProperty("transport").GetProperty("version").ValueKind);
        Assert.Equal(JsonValueKind.Number, snapshot.GetProperty("queue").GetProperty("version").ValueKind);
        Assert.Equal(JsonValueKind.Number, snapshot.GetProperty("mixer").GetProperty("version").ValueKind);
        Assert.Equal(2, snapshot.GetProperty("show").GetProperty("state").GetProperty("playlists").GetArrayLength());
        Assert.Equal("Stopped", snapshot.GetProperty("transport").GetProperty("state").GetProperty("status").GetString());
    }

    [Fact]
    public async Task PositionFrames_FlowToClients()
    {
        using var client = Connected();
        var deck = new DeckContent(
            null,
            TrackId.New(),
            "Intro",
            null,
            EndAction.Advance,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(1));
        _monitor.Bind(new StreamHandle(1), deck);

        _monitor.Publish(new StreamHandle(1), TimeSpan.FromSeconds(2));

        var frame = await client.WaitForAsync(
            e => e.GetProperty("event").GetString() == "position",
            TimeSpan.FromSeconds(5));
        Assert.InRange(frame.GetProperty("filePositionMs").GetInt64(), 2999, 3001);
        Assert.InRange(frame.GetProperty("remainingMs").GetInt64(), 6999, 7001);
        Assert.Equal("Intro", frame.GetProperty("deck").GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Position_StopsAfterUnbind()
    {
        using var client = Connected();
        var deck = new DeckContent(
            null,
            TrackId.New(),
            "Intro",
            null,
            EndAction.Advance,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(1));
        _monitor.Bind(new StreamHandle(1), deck);

        _monitor.Publish(new StreamHandle(1), TimeSpan.FromSeconds(2));
        await client.WaitForAsync(
            e => e.GetProperty("event").GetString() == "position",
            TimeSpan.FromSeconds(5));

        _monitor.Unbind(new StreamHandle(1));
        await client.DrainUntilQuietAsync(TimeSpan.FromMilliseconds(400));

        Assert.True(await client.WaitSilenceAsync(TimeSpan.FromMilliseconds(600)), "position frames after unbind");
    }
}

public sealed class PositionChannelNullMonitorTests
{
    [Fact]
    public async Task NoPositionFrames_WhenMonitorNull()
    {
        var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Pumped);
        var host = new RemoteHost(bus, new RemoteOptions("secret"));
        await host.StartAsync();
        try
        {
            using var client = new TestClient();
            await client.ConnectAsync(host.WebsocketEndpoint, "secret");
            await client.DrainUntilQuietAsync(TimeSpan.FromMilliseconds(400));

            Assert.True(await client.WaitSilenceAsync(TimeSpan.FromMilliseconds(600)), "position frames without monitor");
        }
        finally
        {
            await host.DisposeAsync();
            bus.Dispose();
        }
    }
}
