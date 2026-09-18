namespace Aria.Remote.Tests;

using System.Net;
using System.Text.Json;
using Aria.Audio;
using Aria.Core.Playback;
using Aria.Core.Runtime;

public sealed class PreviewStreamTests : IAsyncLifetime
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

    private static async Task<byte[]> ReadExactlyAsync(Stream stream, int count, CancellationToken token)
    {
        var result = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(result.AsMemory(offset), token);
            Assert.True(read > 0, "stream ended before expected bytes arrived");
            offset += read;
        }
        return result;
    }

    [Fact]
    public async Task Preview_TwoClients_EachReceivesSameBytes()
    {
        var tap = new PreviewTap(48000, 2);
        var frames = 4800;
        var data = new float[frames * 2];
        Array.Fill(data, 0.25f);
        var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Pumped);
        var host = new RemoteHost(bus, new RemoteOptions("secret", TestPorts.Next()), previewTap: tap);
        await host.StartAsync();
        try
        {
            using var http = new HttpClient();
            var url = new Uri(host.HttpEndpoint, "preview?token=secret");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var first = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            using var second = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            Assert.Equal("audio/x-wav", first.Content.Headers.ContentType!.MediaType);
            var firstStream = await first.Content.ReadAsStreamAsync(cts.Token);
            var secondStream = await second.Content.ReadAsStreamAsync(cts.Token);
            var firstHeader = await ReadExactlyAsync(firstStream, 44, cts.Token);
            var secondHeader = await ReadExactlyAsync(secondStream, 44, cts.Token);
            Assert.Equal(firstHeader, secondHeader);
            Assert.Equal((byte)'R', firstHeader[0]);
            tap.Publish(data);
            var want = frames * 2 * 2;
            var a = await ReadExactlyAsync(firstStream, want, cts.Token);
            var b = await ReadExactlyAsync(secondStream, want, cts.Token);
            Assert.Equal(a, b);
            Assert.Equal(0x00, a[0]);
            Assert.Equal(0x20, a[1]);
        }
        finally
        {
            await host.DisposeAsync();
            bus.Dispose();
        }
    }

    [Fact]
    public async Task Preview_EmptyTap_SendsHeaderImmediately()
    {
        var tap = new PreviewTap(48000, 2);
        var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Pumped);
        var host = new RemoteHost(bus, new RemoteOptions("secret", TestPorts.Next()), previewTap: tap);
        await host.StartAsync();
        try
        {
            using var http = new HttpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var response = await http.GetAsync(
                new Uri(host.HttpEndpoint, "preview?token=secret"),
                HttpCompletionOption.ResponseHeadersRead,
                cts.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Null(response.Content.Headers.ContentLength);
            var header = await ReadExactlyAsync(await response.Content.ReadAsStreamAsync(cts.Token), 44, cts.Token);
            Assert.Equal((byte)'W', header[8]);
        }
        finally
        {
            await host.DisposeAsync();
            bus.Dispose();
        }
    }

    [Fact]
    public async Task PreviewSession_Open_ReturnsId()
    {
        var engine = new StubEngine();
        var bus = new CommandBus(new ShowController(engine), BusMode.Pumped);
        var host = new RemoteHost(bus, new RemoteOptions("secret", TestPorts.Next()), engine: engine);
        await host.StartAsync();
        try
        {
            using var http = new HttpClient();
            using var response = await http.PostAsync(new Uri(host.HttpEndpoint, "preview/open?token=secret"), new StringContent(string.Empty));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var session = document.RootElement.GetProperty("session").GetInt32();
            Assert.True(session > 0, "session id not positive");
            Assert.Equal(1, engine.OpenSessions);
        }
        finally
        {
            await host.DisposeAsync();
            bus.Dispose();
        }
    }

    [Fact]
    public async Task PreviewSession_Stream_ServesSessionTap()
    {
        var engine = new StubEngine();
        var bus = new CommandBus(new ShowController(engine), BusMode.Pumped);
        var host = new RemoteHost(bus, new RemoteOptions("secret", TestPorts.Next()), engine: engine);
        await host.StartAsync();
        try
        {
            using var http = new HttpClient();
            using var opened = await http.PostAsync(new Uri(host.HttpEndpoint, "preview/open?token=secret"), new StringContent(string.Empty));
            using var openDocument = JsonDocument.Parse(await opened.Content.ReadAsStringAsync());
            var session = openDocument.RootElement.GetProperty("session").GetInt32();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var response = await http.GetAsync(
                new Uri(host.HttpEndpoint, $"preview?session={session}&token=secret"),
                HttpCompletionOption.ResponseHeadersRead,
                cts.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStreamAsync(cts.Token);
            var header = await ReadExactlyAsync(body, 44, cts.Token);
            Assert.Equal((byte)'R', header[0]);
            var tap = engine.PreviewSessionTap(new PreviewSessionHandle(session));
            Assert.NotNull(tap);
            var frames = 2400;
            var data = new float[frames * 2];
            Array.Fill(data, 0.5f);
            tap.Publish(data);
            var audio = await ReadExactlyAsync(body, frames * 2 * 2, cts.Token);
            Assert.Equal(0x4000, audio[0] | (audio[1] << 8));
        }
        finally
        {
            await host.DisposeAsync();
            bus.Dispose();
        }
    }

    [Fact]
    public async Task PreviewSession_Unknown_Returns404()
    {
        var engine = new StubEngine();
        var bus = new CommandBus(new ShowController(engine), BusMode.Pumped);
        var host = new RemoteHost(bus, new RemoteOptions("secret", TestPorts.Next()), engine: engine);
        await host.StartAsync();
        try
        {
            using var http = new HttpClient();
            using var response = await http.GetAsync(new Uri(host.HttpEndpoint, "preview?session=999&token=secret"));
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await host.DisposeAsync();
            bus.Dispose();
        }
    }

    [Fact]
    public async Task PreviewSession_Open_WithoutToken_Returns401()
    {
        var engine = new StubEngine();
        var bus = new CommandBus(new ShowController(engine), BusMode.Pumped);
        var host = new RemoteHost(bus, new RemoteOptions("secret", TestPorts.Next()), engine: engine);
        await host.StartAsync();
        try
        {
            using var http = new HttpClient();
            using var response = await http.PostAsync(new Uri(host.HttpEndpoint, "preview/open"), new StringContent(string.Empty));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            await host.DisposeAsync();
            bus.Dispose();
        }
    }

    [Fact]
    public async Task PreviewSession_Open_WithoutEngine_Returns404()
    {
        using var http = new HttpClient();
        using var response = await http.PostAsync(new Uri(_host.HttpEndpoint, "preview/open?token=secret"), new StringContent(string.Empty));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PreviewSession_Disconnect_ClosesSession()
    {
        var engine = new StubEngine();
        var bus = new CommandBus(new ShowController(engine), BusMode.Pumped);
        var host = new RemoteHost(bus, new RemoteOptions("secret", TestPorts.Next()), engine: engine);
        await host.StartAsync();
        try
        {
            using var http = new HttpClient();
            using var opened = await http.PostAsync(new Uri(host.HttpEndpoint, "preview/open?token=secret"), new StringContent(string.Empty));
            using var openDocument = JsonDocument.Parse(await opened.Content.ReadAsStringAsync());
            var session = openDocument.RootElement.GetProperty("session").GetInt32();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var response = await http.GetAsync(
                new Uri(host.HttpEndpoint, $"preview?session={session}&token=secret"),
                HttpCompletionOption.ResponseHeadersRead,
                cts.Token);
            var body = await response.Content.ReadAsStreamAsync(cts.Token);
            await ReadExactlyAsync(body, 44, cts.Token);
            cts.Cancel();
            var deadline = Environment.TickCount64 + 5000;
            while (engine.OpenSessions > 0)
            {
                if (Environment.TickCount64 > deadline)
                {
                    Assert.Fail("session not closed after disconnect");
                }
                await Task.Delay(25);
            }
        }
        finally
        {
            await host.DisposeAsync();
            bus.Dispose();
        }
    }
}
