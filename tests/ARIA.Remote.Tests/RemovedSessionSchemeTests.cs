namespace Aria.Remote.Tests;

using System.Net;
using Aria.Core.Runtime;

public sealed class RemovedSessionSchemeTests
{
    [Theory]
    [InlineData("set_click_settings")]
    [InlineData("start_session_track")]
    [InlineData("close_session")]
    [InlineData("rename_session")]
    [InlineData("set_session_backing_gain")]
    [InlineData("set_session_click_gain")]
    [InlineData("set_click_muted")]
    public void RemovedSessionCommands_AreUnknown(string type)
    {
        var json = "{\"client\":\"c\",\"seq\":1,\"command\":{\"type\":\"" + type + "\",\"session\":7,\"name\":\"x\",\"gain_db\":0,\"track\":\"5f0c7a3e-0000-4000-8000-000000000001\",\"muted\":true}}";

        CommandCodec.TryParse(json, out _, out _, out var command);

        Assert.Null(command);
    }

    [Fact]
    public async Task PreviewOpen_IsGone()
    {
        var engine = new StubEngine();
        using var bus = new CommandBus(new ShowController(engine), BusMode.Pumped);
        var host = new RemoteHost(bus, new RemoteOptions("secret", TestPorts.Next()), engine: engine);
        await host.StartAsync();
        try
        {
            using var http = new HttpClient();
            using var response = await http.PostAsync(new Uri(host.HttpEndpoint, "preview/open?token=secret"), new StringContent(string.Empty));

            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }
        finally
        {
            await host.DisposeAsync();
        }
    }
}
