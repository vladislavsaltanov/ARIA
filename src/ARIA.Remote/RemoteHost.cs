namespace Aria.Remote;

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Aria.Audio;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.Runtime;
using Aria.Core.State;
using Aria.Remote.Monitor;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

public sealed record RemoteOptions(string AuthToken, int Port = 0, IRemoteCredentials? Credentials = null, IPAddress? BindAddress = null);

internal sealed record AuthRequest(string? Identifier, string? Password);

public sealed class RemoteHost : IAsyncDisposable
{
    private const int DedupeWindow = 256;
    private const int FailedAuthDelayMs = 300;
    private const int MaxMessageBytes = 1024 * 1024;
    private const int OutboundCapacity = 2048;

    private readonly ICommandBus _bus;
    private readonly RemoteOptions _options;
    private readonly PlaybackMonitor? _monitor;
    private readonly MeterMonitor? _meters;
    private readonly PreviewTap? _previewTap;
    private readonly IAudioEngine? _engine;
    private readonly MonitorFeed? _monitorFeed;
    private MonitorHub? _monitorHub;
    private MonitorPublisher? _monitorPublisher;
    private PcmCache? _pcmCache;
    private readonly ConcurrentDictionary<Guid, Connection> _connections = [];
    private readonly ConcurrentDictionary<string, RemoteRole> _sessionTokens = [];
    private readonly CancellationTokenSource _shutdown = new();
    private IDisposable? _subscription;
    private Task? _positionTask;
    private WebApplication? _app;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new JsonStringEnumConverter(),
            new TrackIdConverter(),
            new EntryIdConverter(),
            new ProjectIdConverter(),
            new ScriptIdConverter(),
            new ScriptLineIdConverter(),
        },
    };

    public RemoteHost(ICommandBus bus, RemoteOptions options, PlaybackMonitor? monitor = null, MeterMonitor? meters = null, PreviewTap? previewTap = null, IAudioEngine? engine = null, MonitorFeed? monitorFeed = null)
    {
        _monitorFeed = monitorFeed;
        _bus = bus;
        _options = options;
        _monitor = monitor;
        _meters = meters;
        _previewTap = previewTap;
        _engine = engine;
    }

    public Uri HttpEndpoint { get; private set; } = new("http://127.0.0.1:0/");

    public MonitorHub? Monitor => _monitorHub;

    internal ImmutableHashSet<TrackId> AllowedMonitorTracks => _monitorPublisher?.Builder.AllowedTracks ?? [];

    public Uri WebsocketEndpoint { get; private set; } = new("ws://127.0.0.1:0/ws");

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var bindAddress = _options.BindAddress ?? IPAddress.Loopback;
        if (!IPAddress.IsLoopback(bindAddress) && _options.Credentials is null)
        {
            throw new InvalidOperationException("non-loopback bind requires remote credentials");
        }
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(options => options.Listen(bindAddress, _options.Port));

        var app = builder.Build();
        app.UseWebSockets();
        app.MapGet("/health", () => Results.Text("ok"));
        app.MapPost("/auth", AuthenticateAsync);
        app.MapGet("/ws", (HttpContext context) => HandleWebSocket(context));
        app.MapGet("/preview", PreviewAsync);
        app.MapGet("/monitor/ws", (HttpContext context) => HandleMonitorSocket(context));
        app.MapGet("/monitor/pcm", (HttpContext context) => HandlePcm(context));
        app.MapGet("/monitor", () => RemoteStaticFiles.Serve("monitor.html"));
        app.MapGet("/", RemoteStaticFiles.ServeIndex);
        app.MapGet("/{**path}", RemoteStaticFiles.ServeAsset);

        await app.StartAsync(cancellationToken);
        _app = app;

        var address = app.Urls.First();
        var port = new Uri(address).Port;
        HttpEndpoint = new Uri($"http://127.0.0.1:{port}/");
        WebsocketEndpoint = new Uri($"ws://127.0.0.1:{port}/ws");

        if (_monitorFeed is { } feed)
        {
            _monitorHub = new MonitorHub(JsonOptions);
            var cache = new PcmCache(feed.Decoders, feed.Sources);
            _pcmCache = cache;
            _monitorPublisher = new MonitorPublisher(feed, _monitorHub, track => feed.Sources.Find(track) is { } source && File.Exists(source.FilePath) && !cache.IsUndecodable(track));
            _monitorPublisher.SetTransport(_bus.Snapshot().Transport);
            _monitorPublisher.Start();
        }
        _subscription = _bus.Subscribe(OnStateEvent);
        if (_monitor is not null || _meters is not null)
        {
            _positionTask = Task.Run(() => LatestValueLoopAsync(_monitor, _meters, _shutdown.Token));
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await DisposeAsyncCore().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private async Task<IResult> AuthenticateAsync(HttpRequest request)
    {
        var credentials = _options.Credentials;
        if (credentials is null)
        {
            return Results.NotFound();
        }
        AuthRequest? body;
        try
        {
            body = await request.ReadFromJsonAsync<AuthRequest>(JsonOptions, CancellationToken.None);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or NotSupportedException)
        {
            body = null;
        }
        if (body is null || credentials.Verify(body.Identifier ?? string.Empty, body.Password ?? string.Empty) is not { } role)
        {
            await Task.Delay(FailedAuthDelayMs);
            return Results.Json(new { error = "invalid-credentials" }, statusCode: StatusCodes.Status401Unauthorized);
        }
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        _sessionTokens[token] = role;
        return Results.Ok(new { token, role = role == RemoteRole.Listen ? "listen" : "control" });
    }

    internal bool TryGetRole(string? token, out RemoteRole role)
    {
        role = RemoteRole.Control;
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }
        if (_options.Credentials is null)
        {
            return token == _options.AuthToken;
        }
        return _sessionTokens.TryGetValue(token, out role);
    }

    public void RevokeListenTokens()
    {
        foreach (var (token, role) in _sessionTokens)
        {
            if (role == RemoteRole.Listen)
            {
                _sessionTokens.TryRemove(token, out _);
            }
        }
        _monitorHub?.Revoke(RemoteRole.Listen);
    }

    private async Task HandlePcm(HttpContext context)
    {
        if (!TryGetRole(context.Request.Query["token"], out var role))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        if (_pcmCache is not { } cache || _monitorFeed is not { } feed)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        await PcmEndpoint.HandleAsync(context, role, cache, feed.Sources, () => AllowedMonitorTracks, _shutdown.Token);
    }

    private async Task HandleMonitorSocket(HttpContext context)
    {
        if (!TryGetRole(context.Request.Query["token"], out var role))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        if (_monitorHub is not { } hub)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        await hub.HandleAsync(socket, role, _shutdown.Token);
    }

    private int ControlAccessStatus(string? token)
    {
        if (!TryGetRole(token, out var role))
        {
            return StatusCodes.Status401Unauthorized;
        }
        return role == RemoteRole.Control ? StatusCodes.Status200OK : StatusCodes.Status403Forbidden;
    }

    private async Task PreviewAsync(HttpContext context)
    {
        if (ControlAccessStatus(context.Request.Query["token"]) is var access && access != StatusCodes.Status200OK)
        {
            context.Response.StatusCode = access;
            return;
        }
        await new PreviewStream(_previewTap).ExecuteAsync(context);
    }

    private async Task HandleWebSocket(HttpContext context)
    {
        if (ControlAccessStatus(context.Request.Query["token"]) is var access && access != StatusCodes.Status200OK)
        {
            context.Response.StatusCode = access;
            return;
        }
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        await RunConnection(socket);
    }

    private async Task RunConnection(WebSocket socket)
    {
        var connection = new Connection(socket);
        string snapshotFrame;
        try
        {
            snapshotFrame = SnapshotFrame();
        }
        catch (Exception)
        {
            await socket.CloseAsync(WebSocketCloseStatus.InternalServerError, null, CancellationToken.None);
            return;
        }
        Enqueue(connection, snapshotFrame);
        _connections[connection.Id] = connection;
        var sender = Task.Run(() => SendLoopAsync(connection));
        try
        {
            var buffer = new byte[16 * 1024];
            using var message = new MemoryStream();
            while (socket.State == WebSocketState.Open && !_shutdown.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), connection.Lifetime.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                    break;
                }
                if (message.Length + result.Count > MaxMessageBytes)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, null, CancellationToken.None);
                    break;
                }
                message.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    HandleFrame(connection, Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
                    message.SetLength(0);
                }
            }
        }
        catch (Exception e) when (e is OperationCanceledException or WebSocketException or IOException)
        {
        }
        finally
        {
            _connections.TryRemove(connection.Id, out _);
            connection.Lifetime.Cancel();
            connection.Outbound.Writer.TryComplete();
            try
            {
                await sender;
            }
            catch (Exception e) when (e is OperationCanceledException or WebSocketException or IOException)
            {
            }
        }
    }

    private async Task SendLoopAsync(Connection connection)
    {
        try
        {
            await foreach (var frame in connection.Outbound.Reader.ReadAllAsync(connection.Lifetime.Token))
            {
                var bytes = Encoding.UTF8.GetBytes(frame);
                await connection.Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, connection.Lifetime.Token);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or WebSocketException or IOException)
        {
        }
    }

    private void HandleFrame(Connection connection, string json)
    {
        if (!CommandCodec.TryParse(json, out var client, out var seq, out var command))
        {
            TrySend(connection, new { Event = "error" });
            return;
        }
        var clientId = new ClientId(client);
        connection.Client ??= clientId;
        if (!connection.Seen.Add(seq))
        {
            return;
        }
        if (seq <= 0)
        {
            TrySend(connection, new { Event = "rejected", Seq = seq, Reason = "bad-seq" });
            return;
        }
        if (command is null)
        {
            TrySend(connection, new { Event = "rejected", Seq = seq, Reason = "unknown-command" });
            return;
        }
        _bus.Submit(clientId, seq, command);
        TrySend(connection, new { Event = "ack", Seq = seq });
    }

    private void OnStateEvent(StateEvent e)
    {
        if (e is TransportDelta transport)
        {
            _monitorPublisher?.SetTransport(transport.State);
        }
        if (_connections.IsEmpty)
        {
            return;
        }
        switch (e)
        {
            case Rejected rejected:
                TrySendTo(rejected.Client, new { Event = "rejected", Seq = rejected.Seq, Reason = rejected.Reason });
                break;
            case ShowDelta delta:
                Broadcast(DeltaFrame("show", delta.Version, delta.State));
                break;
            case TransportDelta delta:
                Broadcast(DeltaFrame("transport", delta.Version, delta.State));
                break;
            case QueueDelta delta:
                Broadcast(DeltaFrame("queue", delta.Version, delta.State));
                break;
            case MixerDelta delta:
                Broadcast(DeltaFrame("mixer", delta.Version, delta.State));
                break;
        }
    }

    private string SnapshotFrame()
    {
        var snapshot = _bus.Snapshot();
        return JsonSerializer.Serialize(new
        {
            Event = "snapshot",
            Show = new { Version = snapshot.ShowVersion, State = snapshot.Show },
            Transport = new { Version = snapshot.TransportVersion, State = snapshot.Transport },
            Queue = new { Version = snapshot.QueueVersion, State = snapshot.Queue },
            Mixer = new { Version = snapshot.MixerVersion, State = snapshot.Mixer },
        }, JsonOptions);
    }

    private async Task LatestValueLoopAsync(PlaybackMonitor? monitor, MeterMonitor? meters, CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                if (_connections.IsEmpty)
                {
                    continue;
                }
                try
                {
                    if (monitor?.Latest is { } latest)
                    {
                        Broadcast(PositionFrame(latest));
                    }
                    if (meters?.Latest is { } lufs)
                    {
                        Broadcast(LufsFrame(lufs));
                    }
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static string PositionFrame(PositionSnapshot snapshot) =>
        JsonSerializer.Serialize(new
        {
            Event = "position",
            Deck = snapshot.Deck,
            FilePositionMs = Milliseconds(snapshot.FilePosition),
            RemainingMs = Milliseconds(snapshot.Remaining),
        }, JsonOptions);

    private static string LufsFrame(LufsSnapshot snapshot) =>
        JsonSerializer.Serialize(new
        {
            Event = "lufs",
            MomentaryLufs = snapshot.MomentaryLufs,
        }, JsonOptions);

    private static long Milliseconds(TimeSpan value) => (long)Math.Round(value.TotalMilliseconds);

    private static string DeltaFrame(string partition, int version, object state) =>
        JsonSerializer.Serialize(new { Event = "delta", Partition = partition, Version = version, State = state }, JsonOptions);

    private void Broadcast(string frame)
    {
        foreach (var connection in _connections.Values)
        {
            Enqueue(connection, frame);
        }
    }

    private void TrySendTo(ClientId client, object frame)
    {
        var payload = JsonSerializer.Serialize(frame, JsonOptions);
        foreach (var connection in _connections.Values)
        {
            if (connection.Client == client)
            {
                Enqueue(connection, payload);
            }
        }
    }

    private static void TrySend(Connection connection, object frame) =>
        Enqueue(connection, JsonSerializer.Serialize(frame, JsonOptions));

    private static void Enqueue(Connection connection, string frame)
    {
        if (!connection.Outbound.Writer.TryWrite(frame))
        {
            connection.Lifetime.Cancel();
        }
    }

    private async Task DisposeAsyncCore()
    {
        if (_shutdown.IsCancellationRequested)
        {
            return;
        }
        _shutdown.Cancel();
        _subscription?.Dispose();
        _monitorPublisher?.Dispose();
        _monitorHub?.CloseAll();
        _pcmCache?.Dispose();
        if (_positionTask is { } position)
        {
            try
            {
                await position.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
        foreach (var connection in _connections.Values)
        {
            connection.Lifetime.Cancel();
            connection.Outbound.Writer.TryComplete();
        }
        if (_app is not null)
        {
            await _app.StopAsync().ConfigureAwait(false);
            await _app.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class Connection
    {
        public Connection(WebSocket socket)
        {
            Socket = socket;
        }

        public Guid Id { get; } = Guid.NewGuid();

        public WebSocket Socket { get; }

        public Channel<string> Outbound { get; } = Channel.CreateBounded<string>(new BoundedChannelOptions(OutboundCapacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        });

        public ClientId? Client { get; set; }

        public RingSet Seen { get; } = new(DedupeWindow);

        public CancellationTokenSource Lifetime { get; } = new();
    }

    private sealed class RingSet(int capacity)
    {
        private readonly Queue<long> _order = [];
        private readonly HashSet<long> _set = [];

        public bool Add(long value)
        {
            if (!_set.Add(value))
            {
                return false;
            }
            _order.Enqueue(value);
            if (_order.Count > capacity)
            {
                _set.Remove(_order.Dequeue());
            }
            return true;
        }
    }
}
