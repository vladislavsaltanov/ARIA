namespace Aria.Remote.Monitor;

using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Aria.Audio;

public sealed class MonitorHub
{
    public const int MaxListeners = 16;

    private const int MaxMessageBytes = 64 * 1024;
    private const int OutboundCapacity = 256;
    private const int NameMaxLength = 64;
    private const WebSocketCloseStatus TryAgainLater = (WebSocketCloseStatus)1013;

    private static readonly TimeSpan CloseHandshakeTimeout = TimeSpan.FromSeconds(2);

    private readonly JsonSerializerOptions _json;
    private readonly ConcurrentDictionary<string, Connection> _connections = [];
    private int _counter;
    private int _active;
    private string? _latestTimeline;
    private string? _latestStatus;

    public MonitorHub(JsonSerializerOptions json)
    {
        _json = json;
    }

    public IReadOnlyList<ListenerProfile> Listeners =>
        [.. _connections.Values.Select(connection => Volatile.Read(ref connection.Profile)).OrderBy(profile => profile.ConnectedAt).ThenBy(profile => profile.Id, StringComparer.Ordinal)];

    internal string? LatestTimeline => Volatile.Read(ref _latestTimeline);

    internal string? LatestStatus => Volatile.Read(ref _latestStatus);

    public void SetLevels(string listenerId, ListenerLevels levels)
    {
        if (!_connections.TryGetValue(listenerId, out var connection))
        {
            return;
        }
        Volatile.Write(ref connection.Profile, Volatile.Read(ref connection.Profile) with { Levels = levels });
        Enqueue(connection, new TextFrame(Serialize(new { type = "levels", backingDb = levels.BackingDb, backingOn = levels.BackingOn, clickDb = levels.ClickDb, clickOn = levels.ClickOn })));
    }

    public void Kick(string listenerId)
    {
        if (_connections.TryGetValue(listenerId, out var connection))
        {
            CloseWith(connection, "kicked");
        }
    }

    public void Revoke(RemoteRole role)
    {
        foreach (var connection in _connections.Values.Where(connection => connection.Role == role))
        {
            CloseWith(connection, "revoked");
        }
    }

    internal void CloseAll()
    {
        foreach (var connection in _connections.Values)
        {
            Enqueue(connection, new CloseFrame("host-stopping"));
        }
    }

    internal void BroadcastTimeline(TimelineMessage message)
    {
        var json = Serialize(message);
        Volatile.Write(ref _latestTimeline, json);
        Broadcast(json);
    }

    internal void BroadcastStatus(bool output)
    {
        var json = Serialize(new { type = "status", output });
        Volatile.Write(ref _latestStatus, json);
        Broadcast(json);
    }

    internal async Task HandleAsync(WebSocket socket, RemoteRole role, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _active) > MaxListeners)
        {
            Interlocked.Decrement(ref _active);
            await socket.CloseAsync(TryAgainLater, "too-many-listeners", CancellationToken.None);
            return;
        }
        var id = "L" + Interlocked.Increment(ref _counter);
        var connection = new Connection(id, role, socket, new ListenerProfile(id, string.Empty, role, ListenerLevels.Default, null, DateTimeOffset.UtcNow), cancellationToken);
        _connections[id] = connection;
        Enqueue(connection, new TextFrame(Serialize(new { type = "welcome", listener = id, role = role == RemoteRole.Listen ? "listen" : "control", sampleRate = 48000 })));
        if (LatestStatus is { } status)
        {
            Enqueue(connection, new TextFrame(status));
        }
        if (LatestTimeline is { } timeline)
        {
            Enqueue(connection, new TextFrame(timeline));
        }
        var sender = Task.Run(() => SendLoopAsync(connection));
        try
        {
            var buffer = new byte[4096];
            using var message = new MemoryStream();
            while (socket.State is WebSocketState.Open or WebSocketState.CloseSent && !connection.Lifetime.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), connection.Lifetime.Token);
                if (result.MessageType == WebSocketMessageType.Close || message.Length + result.Count > MaxMessageBytes)
                {
                    break;
                }
                message.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    var receivedUs = HostClock.NowUs();
                    HandleMessage(connection, Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length), receivedUs);
                    message.SetLength(0);
                }
            }
        }
        catch (Exception e) when (e is OperationCanceledException or WebSocketException or IOException)
        {
        }
        finally
        {
            _connections.TryRemove(id, out _);
            Interlocked.Decrement(ref _active);
            connection.Outbound.Writer.TryComplete();
            try
            {
                await sender;
            }
            catch (Exception e) when (e is OperationCanceledException or WebSocketException or IOException)
            {
            }
            connection.Lifetime.Cancel();
            connection.Lifetime.Dispose();
        }
    }

    private void HandleMessage(Connection connection, string text, long receivedUs)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "ping":
                    Enqueue(connection, new PongFrame(root.GetProperty("t0").GetDouble(), receivedUs));
                    break;
                case "hello":
                    var name = root.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String ? nameElement.GetString()!.Trim() : string.Empty;
                    var profile = Volatile.Read(ref connection.Profile) with { Name = name.Length > NameMaxLength ? name[..NameMaxLength] : name };
                    if (root.TryGetProperty("levels", out var levelsElement) && levelsElement.ValueKind == JsonValueKind.Object)
                    {
                        profile = profile with { Levels = ParseLevels(levelsElement) };
                    }
                    Volatile.Write(ref connection.Profile, profile);
                    break;
                case "levels":
                    Volatile.Write(ref connection.Profile, Volatile.Read(ref connection.Profile) with { Levels = ParseLevels(root) });
                    break;
                case "stats":
                    var stats = new ListenerStats(
                        root.GetProperty("offsetUs").GetInt64(),
                        root.GetProperty("uncertaintyUs").GetInt64(),
                        root.GetProperty("rttUs").GetInt64(),
                        root.GetProperty("outputLatencyMs").GetDouble(),
                        root.GetProperty("trimMs").GetDouble(),
                        root.GetProperty("state").GetString() ?? string.Empty);
                    Volatile.Write(ref connection.Profile, Volatile.Read(ref connection.Profile) with { Stats = stats });
                    break;
            }
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
        }
    }

    private static ListenerLevels ParseLevels(JsonElement element) =>
        new(
            element.GetProperty("backingDb").GetDouble(),
            element.GetProperty("backingOn").GetBoolean(),
            element.GetProperty("clickDb").GetDouble(),
            element.GetProperty("clickOn").GetBoolean());

    private async Task SendLoopAsync(Connection connection)
    {
        try
        {
            await foreach (var frame in connection.Outbound.Reader.ReadAllAsync(connection.Lifetime.Token))
            {
                switch (frame)
                {
                    case TextFrame text:
                        await SendTextAsync(connection, text.Json);
                        break;
                    case PongFrame pong:
                        var sentUs = HostClock.NowUs();
                        await SendTextAsync(connection, Serialize(new { type = "pong", t0 = pong.T0, t1 = pong.ReceivedUs, t2 = sentUs }));
                        break;
                    case CloseFrame close:
                        await connection.Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, close.Reason, CancellationToken.None);
                        connection.Lifetime.CancelAfter(CloseHandshakeTimeout);
                        return;
                }
            }
        }
        catch (Exception e) when (e is OperationCanceledException or WebSocketException or IOException or ObjectDisposedException)
        {
        }
    }

    private static Task SendTextAsync(Connection connection, string json) =>
        connection.Socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Text, true, connection.Lifetime.Token);

    private void CloseWith(Connection connection, string reason)
    {
        Enqueue(connection, new TextFrame(Serialize(new { type = reason })));
        Enqueue(connection, new CloseFrame(reason));
    }

    private void Broadcast(string json)
    {
        foreach (var connection in _connections.Values)
        {
            Enqueue(connection, new TextFrame(json));
        }
    }

    private static void Enqueue(Connection connection, Outbound frame) => connection.Outbound.Writer.TryWrite(frame);

    private string Serialize<T>(T value) => JsonSerializer.Serialize(value, _json);

    private abstract record Outbound;

    private sealed record TextFrame(string Json) : Outbound;

    private sealed record PongFrame(double T0, long ReceivedUs) : Outbound;

    private sealed record CloseFrame(string Reason) : Outbound;

    private sealed class Connection
    {
        public Connection(string id, RemoteRole role, WebSocket socket, ListenerProfile profile, CancellationToken hostLifetime)
        {
            Id = id;
            Role = role;
            Socket = socket;
            Profile = profile;
            Lifetime = CancellationTokenSource.CreateLinkedTokenSource(hostLifetime);
        }

        public string Id { get; }

        public RemoteRole Role { get; }

        public WebSocket Socket { get; }

        public ListenerProfile Profile;

        public CancellationTokenSource Lifetime { get; }

        public Channel<Outbound> Outbound { get; } = Channel.CreateBounded<Outbound>(new BoundedChannelOptions(OutboundCapacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
    }
}
