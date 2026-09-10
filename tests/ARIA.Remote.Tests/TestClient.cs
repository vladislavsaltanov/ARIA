namespace Aria.Remote.Tests;

using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

public sealed class TestClient : IDisposable
{
    private ClientWebSocket _socket = new();
    private readonly BlockingCollection<string> _frames = [];
    private readonly CancellationTokenSource _readerCts = new();
    private Task? _reader;

    public ClientWebSocket Socket => _socket;

    public async Task SendAsync(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    public async Task ConnectAsync(Uri websocketEndpoint, string token)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var socket = new ClientWebSocket();
            try
            {
                await socket.ConnectAsync(new Uri($"{websocketEndpoint}?token={Uri.EscapeDataString(token)}"), CancellationToken.None);
            }
            catch (Exception e) when (attempt < 3 && e is WebSocketException or IOException)
            {
                socket.Dispose();
                continue;
            }
            _socket.Dispose();
            _socket = socket;
            while (_frames.TryTake(out _))
            {
            }
            _reader = Task.Run(ReaderLoop);
            if (await ReceivedAnyFrameAsync(TimeSpan.FromSeconds(2)))
            {
                return;
            }
            _socket.Dispose();
        }
    }

    private async Task<bool> ReceivedAnyFrameAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (_frames.Count > 0)
            {
                return true;
            }
            await Task.Delay(20);
        }
        return false;
    }

    private async Task ReaderLoop()
    {
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        try
        {
            while (!_readerCts.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                var result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _readerCts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
                message.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    _frames.Add(Encoding.UTF8.GetString(message.ToArray()));
                    message.SetLength(0);
                }
            }
        }
        catch (Exception e) when (e is OperationCanceledException or WebSocketException or IOException or ObjectDisposedException)
        {
        }
    }

    public async Task<JsonElement> WaitForAsync(Func<JsonElement, bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            while (_frames.TryTake(out var frame))
            {
                var element = JsonDocument.Parse(frame).RootElement;
                if (predicate(element))
                {
                    return element;
                }
            }
            await Task.Delay(20);
        }
        throw new TimeoutException("frame not received");
    }

    public async Task DrainUntilQuietAsync(TimeSpan quietWindow)
    {
        var quietFor = TimeSpan.Zero;
        while (quietFor < quietWindow)
        {
            if (_frames.TryTake(out _))
            {
                quietFor = TimeSpan.Zero;
            }
            else
            {
                await Task.Delay(25);
                quietFor += TimeSpan.FromMilliseconds(25);
            }
        }
    }

    public async Task<bool> WaitSilenceAsync(TimeSpan duration)
    {
        var deadline = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < deadline)
        {
            if (_frames.TryTake(out _))
            {
                return false;
            }
            await Task.Delay(25);
        }
        return true;
    }

    public void Dispose()
    {
        _readerCts.Cancel();
        try
        {
            _socket.Dispose();
        }
        catch (Exception)
        {
        }
        _readerCts.Dispose();
    }
}
