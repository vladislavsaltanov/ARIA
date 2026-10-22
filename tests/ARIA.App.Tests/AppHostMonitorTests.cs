namespace Aria.App.Tests;

using Aria.App;
using Aria.Audio;
using Aria.Remote;

public sealed class AppHostMonitorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aria-hostmon-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private AppHost NewHost() => new(
        _directory,
        new RemoteOptions("monitor-secret"),
        () => new NullSink(48000, 2));

    [Fact]
    public async Task Start_WithRemote_ServesMonitoring()
    {
        await using var host = NewHost();
        await host.StartAsync();

        Assert.NotNull(host.Remote?.Monitor);
        Assert.Empty(host.Listeners);
        using var http = new HttpClient();
        using var response = await http.GetAsync(new Uri(host.Remote!.HttpEndpoint, "monitor"));
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task HallTrim_ClampsAndSurvivesRestart()
    {
        await using (var first = NewHost())
        {
            await first.StartAsync();
            first.HallTrimMs = 500;
            Assert.Equal(200, first.HallTrimMs);
            first.HallTrimMs = 35;
        }

        await using var second = NewHost();
        await second.StartAsync();

        Assert.Equal(35, second.HallTrimMs);
    }

    [Fact]
    public async Task TestClick_TogglesEngine()
    {
        await using var host = NewHost();
        await host.StartAsync();

        host.TestClickEnabled = true;
        Assert.True(host.TestClickEnabled);
        host.TestClickEnabled = false;
        Assert.False(host.TestClickEnabled);
    }
}
