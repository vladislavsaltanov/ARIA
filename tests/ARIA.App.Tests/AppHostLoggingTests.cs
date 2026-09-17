namespace Aria.App.Tests;

using System.Text.Json;
using Aria.App;
using Aria.App.Services;
using Aria.Audio;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;
using Aria.Remote;

public sealed class AppHostLoggingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aria-hostlog-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task Start_ByDefault_CreatesNoLogFile()
    {
        await using var host = await StartHostAsync();
        host.Submit(new CreateProject("Quiet"));
        await Task.Delay(300);

        Assert.False(File.Exists(LogPath()));
    }

    [Fact]
    public async Task Start_WritesHostStarted_ToDataDirLog()
    {
        EnableLogging();
        await using var host = await StartHostAsync();

        await PollAsync(() => HasLine("host.started"));

        Assert.Equal(_directory, FindLine("host.started").GetProperty("data").GetProperty("dir").GetString());
    }

    [Fact]
    public async Task Submit_LogsCommandType_AndSkipsClockTicks()
    {
        EnableLogging();
        await using var host = await StartHostAsync();
        host.Submit(new CreateProject("Logged"));
        host.Submit(new TickShowClock());
        await PollAsync(() => HasData("command", "type", "CreateProject"));
        var seq = FindData("command", "type", "CreateProject").GetProperty("data").GetProperty("seq").GetString();
        Assert.False(string.IsNullOrEmpty(seq));
        await Task.Delay(300);

        Assert.False(HasData("command", "type", "TickShowClock"));
    }

    [Fact]
    public async Task RejectedCommand_LogsWarningWithReason()
    {
        EnableLogging();
        await using var host = await StartHostAsync();
        host.Submit(new SetLocked(true));
        await PollAsync(() => host.Bus.Snapshot().Show.Locked);
        host.Submit(new Play());

        await PollAsync(() => HasLine("command.rejected"));
        var line = FindLine("command.rejected");
        Assert.Equal("locked", line.GetProperty("data").GetProperty("reason").GetString());
        Assert.Equal("warn", line.GetProperty("level").GetString());
        var playSeq = FindData("command", "type", "Play").GetProperty("data").GetProperty("seq").GetString();
        Assert.Equal(playSeq, line.GetProperty("data").GetProperty("seq").GetString());
    }

    [Fact]
    public async Task ImportOverrideNull_LogsImportFailedWithPath()
    {
        EnableLogging();
        await using var host = await StartHostAsync();
        host.ImportOverride = _ => null;
        var broken = Path.Combine(_directory, "a.flac");
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(broken, "not audio");

        var report = await host.ImportTracksAsync([broken]);

        Assert.Single(report.Failed);
        await PollAsync(() => HasData("import.failed", "path", broken));
    }

    [Fact]
    public async Task RelinkMissingFile_LogsRelinkFailed()
    {
        EnableLogging();
        await using var host = await StartHostAsync();

        var relinked = await host.RelinkTrackAsync(TrackId.New(), "/nonexistent/x.flac");

        Assert.False(relinked);
        await PollAsync(() => HasData("relink.failed", "path", "/nonexistent/x.flac"));
    }

    [Fact]
    public async Task Start_AppliesStoredLogLevel()
    {
        Directory.CreateDirectory(_directory);
        new AppSettingsStore(Path.Combine(_directory, "settings.json")).Save(AppSettings.Default with { LogLevel = LogLevel.Error, LogEnabled = true });
        await using var host = await StartHostAsync();
        host.Submit(new CreateProject("Quiet"));
        await Task.Delay(300);

        Assert.False(HasData("command", "type", "CreateProject"));
    }

    [Fact]
    public async Task RemoteStart_LogsEndpoint_WithoutToken()
    {
        EnableLogging();
        await using var host = new AppHost(
            _directory,
            new RemoteOptions("test-token-secret"),
            () => new NullSink(8000, 2),
            () => new NullSourceFactory());
        await host.StartAsync();

        await PollAsync(() => HasLine("remote.started"));
        Assert.DoesNotContain("test-token-secret", File.ReadAllText(LogPath()));
    }

    [Fact]
    public async Task SetLogLevel_AppliesLive()
    {
        EnableLogging();
        await using var host = await StartHostAsync();
        host.SetLogLevel(LogLevel.Error);
        host.Submit(new CreateProject("Quiet"));
        await Task.Delay(300);

        Assert.False(HasData("command", "type", "CreateProject"));
    }

    [Fact]
    public async Task SetLogEnabled_TogglesLive()
    {
        await using var host = await StartHostAsync();
        host.SetLogEnabled(true);
        host.Submit(new CreateProject("Logged"));
        await PollAsync(() => HasData("command", "type", "Logged"));
        host.SetLogEnabled(false);
        host.Submit(new CreateProject("Quiet"));
        await Task.Delay(300);

        Assert.Single(ReadEntries(LogPath()).Where(e => e.GetProperty("msg").GetString() == "command"));
    }

    [Fact]
    public void NotifyDeviceFault_LogsWarning()
    {
        var logPath = Path.Combine(_directory, "fault.log");
        using var log = new FileAppLog(logPath);
        var service = new AudioOutputService(new EmptyLister(), new AppSettingsStore(Path.Combine(_directory, "settings.json")), log);

        service.NotifyDeviceFault("boom");

        Assert.True(HasLineIn(logPath, "output.device_fault"));
    }

    private async Task<AppHost> StartHostAsync()
    {
        var host = new AppHost(_directory, null, () => new NullSink(8000, 2), () => new NullSourceFactory());
        await host.StartAsync();
        return host;
    }

    private void EnableLogging() =>
        new AppSettingsStore(Path.Combine(_directory, "settings.json")).Save(AppSettings.Default with { LogEnabled = true });

    private string LogPath() => Path.Combine(_directory, "logs", "aria.log");

    private bool HasLine(string message) =>
        File.Exists(LogPath()) && HasLineIn(LogPath(), message);

    private static bool HasLineIn(string path, string message) =>
        File.Exists(path) && ReadEntries(path).Any(e => e.GetProperty("msg").GetString() == message);

    private bool HasData(string message, string key, string value) =>
        File.Exists(LogPath()) && ReadEntries(LogPath())
            .Any(e => e.GetProperty("msg").GetString() == message
                && e.TryGetProperty("data", out var data)
                && data.TryGetProperty(key, out var field)
                && field.GetString() == value);

    private JsonElement FindLine(string message) =>
        ReadEntries(LogPath()).First(e => e.GetProperty("msg").GetString() == message);

    private JsonElement FindData(string message, string key, string value) =>
        ReadEntries(LogPath()).First(e => e.GetProperty("msg").GetString() == message
            && e.TryGetProperty("data", out var data)
            && data.TryGetProperty(key, out var field)
            && field.GetString() == value);

    private static IEnumerable<JsonElement> ReadEntries(string path) =>
        File.ReadAllLines(path)
            .Where(l => l.Length > 0)
            .Select(l => JsonDocument.Parse(l).RootElement.Clone());

    private static async Task PollAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }
            await Task.Delay(25);
        }
        throw new TimeoutException("condition not met");
    }

    private sealed class NullSourceFactory : ISourceFactory
    {
        public ISampleSource? Open(string filePath, TimeSpan cueIn, TimeSpan? cueOut) => null;
    }

    private sealed class EmptyLister : IAudioOutputLister
    {
        public IReadOnlyList<OutputDevice> ListPlaybackDevices() => [];
    }
}
