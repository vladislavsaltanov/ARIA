namespace Aria.Remote.Tests;

using System.Net;
using Aria.Core.Runtime;

public sealed class MonitorPageTests : IAsyncLifetime
{
    private CommandBus _bus = null!;
    private RemoteHost _host = null!;
    private readonly HttpClient _http = new();

    public async Task InitializeAsync()
    {
        _bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Pumped);
        _host = new RemoteHost(_bus, new RemoteOptions("secret", TestPorts.Next()));
        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _host.DisposeAsync();
        _bus.Dispose();
    }

    [Fact]
    public async Task MonitorPage_Served()
    {
        var response = await _http.GetAsync(new Uri(_host.HttpEndpoint, "monitor"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("text/html", response.Content.Headers.ContentType!.ToString());
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("type=\"module\"", html);
        Assert.Contains("monitor/main.mjs", html);
    }

    [Fact]
    public async Task MonitorModule_ServedAsJavaScript()
    {
        var response = await _http.GetAsync(new Uri(_host.HttpEndpoint, "monitor/clock.mjs"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("export class ClockSync", await response.Content.ReadAsStringAsync());
    }
}
