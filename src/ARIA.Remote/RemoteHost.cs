namespace Aria.Remote;

using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Aria.Audio;
using Aria.Core.Commands;
using Aria.Core.Playback;
using Aria.Core.Runtime;
using Aria.Core.State;
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

    private readonly ICommandBus _bus;
    private readonly RemoteOptions _options;
    private readonly PlaybackMonitor? _monitor;
    private readonly MeterMonitor? _meters;
    private readonly SampleRing? _previewTap;
    private readonly ConcurrentDictionary<Guid, Connection> _connections = [];
    private readonly ConcurrentDictionary<string, byte> _sessionTokens = [];
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
            new PlaylistIdConverter(),
            new ScriptIdConverter(),
            new ScriptLineIdConverter(),
        },
    };

    public RemoteHost(ICommandBus bus, RemoteOptions options, PlaybackMonitor? monitor = null, MeterMonitor? meters = null, SampleRing? previewTap = null)
    {
        _bus = bus;
        _options = options;
        _monitor = monitor;
        _meters = meters;
        _previewTap = previewTap;
    }

    public Uri HttpEndpoint { get; private set; } = new("http://127.0.0.1:0/");

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
        app.MapGet("/", RemoteStaticFiles.ServeIndex);
        app.MapGet("/{**path}", RemoteStaticFiles.ServeAsset);

        await app.StartAsync(cancellationToken);
        _app = app;

        var address = app.Urls.First();
        var port = new Uri(address).Port;
        HttpEndpoint = new Uri($"http://127.0.0.1:{port}/");
        WebsocketEndpoint = new Uri($"ws://127.0.0.1:{port}/ws");

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
        if (body is null || !credentials.Verify(body.Identifier ?? string.Empty, body.Password ?? string.Empty))
        {
            await Task.Delay(FailedAuthDelayMs);
            return Results.Json(new { error = "invalid-credentials" }, statusCode: StatusCodes.Status401Unauthorized);
        }
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        _sessionTokens[token] = 0;
        return Results.Ok(new { token });
    }

    private bool IsTokenValid(string? token) =>
        !string.IsNullOrEmpty(token)
        && (_options.Credentials is null
            ? token == _options.AuthToken
            : _sessionTokens.ContainsKey(token));

    private IResult PreviewAsync(HttpContext context)
    {
        if (!IsTokenValid(context.Request.Query["token"]))
        {
            return Results.Unauthorized();
        }
        return new PreviewCapture(_previewTap);
    }

    private async Task HandleWebSocket(HttpContext context)
    {
        if (!IsTokenValid(context.Request.Query["token"]))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
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
        connection.Outbound.Writer.TryWrite(snapshotFrame);
        _connections[connection.Id] = connection;
        var sender = Task.Run(() => SendLoopAsync(connection));
        try
        {
            var buffer = new byte[16 * 1024];
            var message = new StringBuilder();
            while (socket.State == WebSocketState.Open && !_shutdown.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), connection.Lifetime.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                    break;
                }
                message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (result.EndOfMessage)
                {
                    HandleFrame(connection, message.ToString());
                    message.Clear();
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
                if (monitor?.Latest is { } latest)
                {
                    Broadcast(PositionFrame(latest));
                }
                if (meters?.Latest is { } lufs)
                {
                    Broadcast(LufsFrame(lufs));
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
            connection.Outbound.Writer.TryWrite(frame);
        }
    }

    private void TrySendTo(ClientId client, object frame)
    {
        var payload = JsonSerializer.Serialize(frame, JsonOptions);
        foreach (var connection in _connections.Values)
        {
            if (connection.Client == client)
            {
                connection.Outbound.Writer.TryWrite(payload);
            }
        }
    }

    private void TrySend(Connection connection, object frame) =>
        connection.Outbound.Writer.TryWrite(JsonSerializer.Serialize(frame, JsonOptions));

    private async Task DisposeAsyncCore()
    {
        if (_shutdown.IsCancellationRequested)
        {
            return;
        }
        _shutdown.Cancel();
        _subscription?.Dispose();
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
            await _app.StopAsync();
            await _app.DisposeAsync();
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

        public Channel<string> Outbound { get; } = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });

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
