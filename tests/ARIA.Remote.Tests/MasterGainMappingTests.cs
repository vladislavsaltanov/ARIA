namespace Aria.Remote.Tests;

using System.Net;
using Aria.Core.Runtime;

public sealed class MasterGainMappingTests : IAsyncLifetime
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

    [Fact]
    public async Task MasterSlider_Allows125()
    {
        using var http = new HttpClient();

        var response = await http.GetAsync(_host.HttpEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("max=\"125\"", html);
    }

    [Fact]
    public async Task GainMapping_UsesLogCurveTo125()
    {
        using var http = new HttpClient();

        var js = await http.GetStringAsync(new Uri(_host.HttpEndpoint, "app.js"));

        Assert.Contains("Math.min(125,", js);
        Assert.Contains("Math.min(12,", js);
    }

    [Fact]
    public async Task MentionClick_SendsPlayTrack()
    {
        using var http = new HttpClient();

        var js = await http.GetStringAsync(new Uri(_host.HttpEndpoint, "app.js"));

        Assert.Contains("play_track", js);
        Assert.DoesNotContain("enqueue_track", js);
    }
}
