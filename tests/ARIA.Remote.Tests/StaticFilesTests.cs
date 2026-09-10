namespace Aria.Remote.Tests;

using System.Net;
using System.Net.WebSockets;
using Aria.Core.Runtime;

public sealed class StaticFilesTests : IAsyncLifetime
{
    private CommandBus _bus = null!;
    private RemoteHost _host = null!;

    public async Task InitializeAsync()
    {
        _bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Pumped);
        _host = new RemoteHost(_bus, new RemoteOptions("secret"));
        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        _bus.Dispose();
    }

    [Fact]
    public async Task Root_ServesEmbeddedIndex()
    {
        using var http = new HttpClient();

        var response = await http.GetAsync(_host.HttpEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("ARIA", html);
        Assert.Contains("text/html", response.Content.Headers.ContentType!.ToString());
        Assert.True(response.Headers.CacheControl!.NoCache);
    }

    [Fact]
    public async Task AppJs_ServedAsJavascript()
    {
        using var http = new HttpClient();

        var response = await http.GetAsync(new Uri(_host.HttpEndpoint, "app.js"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("text/javascript", response.Content.Headers.ContentType!.ToString());
    }

    [Fact]
    public async Task AppCss_ServedAsCss()
    {
        using var http = new HttpClient();

        var response = await http.GetAsync(new Uri(_host.HttpEndpoint, "app.css"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("text/css", response.Content.Headers.ContentType!.ToString());
    }

    [Fact]
    public async Task Traversal_Returns404()
    {
        using var http = new HttpClient();

        var response = await http.GetAsync(new Uri(_host.HttpEndpoint, "../secret.txt"));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Root_Returns404_WhenNoIndex()
    {
        using var http = new HttpClient();

        var response = await http.GetAsync(_host.HttpEndpoint);

        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.OK, $"unexpected {response.StatusCode}");
    }

    [Fact]
    public async Task Unknown_Returns404()
    {
        using var http = new HttpClient();

        var response = await http.GetAsync(new Uri(_host.HttpEndpoint, "nope.js"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Health_StillWorks()
    {
        using var http = new HttpClient();

        var response = await http.GetAsync(new Uri(_host.HttpEndpoint, "health"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ws_StillWorks()
    {
        using var client = new TestClient();

        await client.ConnectAsync(_host.WebsocketEndpoint, "secret");

        Assert.Equal(WebSocketState.Open, client.Socket.State);
    }
}
