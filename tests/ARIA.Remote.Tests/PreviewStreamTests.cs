namespace Aria.Remote.Tests;

using System.Net;
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
}
