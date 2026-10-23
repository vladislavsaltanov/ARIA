namespace Aria.Remote.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aria.Core.Runtime;

public sealed class RemoteRoleTests : IAsyncLifetime
{
    private readonly string _authPath = Path.Combine(Path.GetTempPath(), $"aria-auth-{Guid.NewGuid():N}.json");
    private CommandBus _bus = null!;
    private RemoteHost _host = null!;
    private RemoteCredentials _pair = null!;
    private readonly HttpClient _http = new();

    public async Task InitializeAsync()
    {
        var store = new RemoteCredentialsStore(_authPath);
        _pair = store.Load();
        _bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Pumped);
        _host = new RemoteHost(_bus, new RemoteOptions("unused", TestPorts.Next(), store));
        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _host.DisposeAsync();
        _bus.Dispose();
        File.Delete(_authPath);
    }

    private async Task<(string Token, string Role)> AuthAsync(string password)
    {
        var response = await _http.PostAsJsonAsync(new Uri(_host.HttpEndpoint, "auth"), new { identifier = _pair.Identifier, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (body.RootElement.GetProperty("token").GetString()!, body.RootElement.GetProperty("role").GetString()!);
    }

    [Fact]
    public async Task Auth_ListenPair_ReturnsListenRole()
    {
        var (token, role) = await AuthAsync(_pair.ListenPassword);

        Assert.Equal("listen", role);
        Assert.True(_host.TryGetRole(token, out var stored));
        Assert.Equal(RemoteRole.Listen, stored);
    }

    [Fact]
    public async Task Auth_ControlPair_ReturnsControlRole()
    {
        var (_, role) = await AuthAsync(_pair.Password);

        Assert.Equal("control", role);
    }

    [Fact]
    public async Task ListenToken_Ws_Forbidden_ControlToken_PassesAuth()
    {
        var (listen, _) = await AuthAsync(_pair.ListenPassword);
        var (control, _) = await AuthAsync(_pair.Password);

        var forbidden = await _http.GetAsync(new Uri(_host.HttpEndpoint, $"ws?token={listen}"));
        var allowed = await _http.GetAsync(new Uri(_host.HttpEndpoint, $"ws?token={control}"));

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, allowed.StatusCode);
    }

    [Fact]
    public async Task ListenToken_Preview_Forbidden()
    {
        var (listen, _) = await AuthAsync(_pair.ListenPassword);

        var response = await _http.GetAsync(new Uri(_host.HttpEndpoint, $"preview?token={listen}"), HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RevokeListenTokens_ListenTokenRejected_ControlKept()
    {
        var (listen, _) = await AuthAsync(_pair.ListenPassword);
        var (control, _) = await AuthAsync(_pair.Password);

        _host.RevokeListenTokens();

        Assert.False(_host.TryGetRole(listen, out _));
        Assert.True(_host.TryGetRole(control, out var role));
        Assert.Equal(RemoteRole.Control, role);
        var response = await _http.GetAsync(new Uri(_host.HttpEndpoint, $"preview?token={listen}"), HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
