namespace Aria.Core.Tests;

using System.Text.Json;
using Aria.Core.Runtime;

public sealed class AppLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aria-log-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [Fact]
    public void Write_Info_ProducesSingleParsableJsonLine()
    {
        using var log = OpenLog();
        log.Info("host.started", new Dictionary<string, string> { ["version"] = "0.2.2" });

        var lines = ReadLines();
        var single = Assert.Single(lines);
        using var document = JsonDocument.Parse(single);
        var root = document.RootElement;
        Assert.Equal("info", root.GetProperty("level").GetString());
        Assert.Equal("host.started", root.GetProperty("msg").GetString());
        Assert.Equal("0.2.2", root.GetProperty("data").GetProperty("version").GetString());
        Assert.True(root.TryGetProperty("ts", out _));
    }

    [Fact]
    public void Write_DebugBelowMinLevel_IsDropped()
    {
        using var log = OpenLog(LogLevel.Info);
        log.Debug("noisy.tick");

        Assert.Empty(ReadLines());
    }

    [Fact]
    public void Write_AfterDispose_DoesNotThrow()
    {
        var log = OpenLog();
        log.Dispose();

        var exception = Record.Exception(() => log.Error("late.fatal"));
        Assert.Null(exception);
    }

    [Fact]
    public void Write_FromManyThreads_KeepsEveryLineParsable()
    {
        const int threads = 8;
        const int perThread = 200;
        using var log = OpenLog();
        Parallel.For(0, threads, i => { for (var n = 0; n < perThread; n++) log.Info("race.write"); });

        var lines = ReadLines();
        Assert.Equal(threads * perThread, lines.Length);
        foreach (var line in lines)
        {
            using var document = JsonDocument.Parse(line);
            Assert.Equal("race.write", document.RootElement.GetProperty("msg").GetString());
        }
    }

    [Fact]
    public void Write_PastSizeCap_RotatesSingleBackup()
    {
        using var log = OpenLog(LogLevel.Info, maxBytes: 256);
        for (var i = 0; i < 50; i++) log.Info(new string('x', 64));

        Assert.True(File.Exists(Path.Combine(_directory, "aria.log.1")));
        var current = new FileInfo(Path.Combine(_directory, "aria.log"));
        Assert.True(current.Length <= 512);
        Assert.All(ReadAllLines(), line => JsonDocument.Parse(line).Dispose());
    }

    [Fact]
    public void Write_NonPositiveMaxBytes_DoesNotRotate()
    {
        using var log = OpenLog(LogLevel.Info, maxBytes: 0);
        for (var i = 0; i < 5; i++) log.Info("steady");

        Assert.Equal(5, ReadLines().Length);
        Assert.False(File.Exists(Path.Combine(_directory, "aria.log.1")));
    }

    [Fact]
    public void Write_UndersizedPath_DoesNotThrow()
    {
        using var log = new FileAppLog("");

        var exception = Record.Exception(() => log.Error("nowhere"));
        Assert.Null(exception);
    }

    [Fact]
    public void NullLog_AcceptsWritesWithoutFile()
    {
        var exception = Record.Exception(() => NullAppLog.Instance.Error("dropped"));
        Assert.Null(exception);
        Assert.False(Directory.Exists(_directory));
    }

    [Theory]
    [InlineData(null, LogLevel.Info)]
    [InlineData("", LogLevel.Info)]
    [InlineData("debug", LogLevel.Debug)]
    [InlineData("DEBUG", LogLevel.Debug)]
    [InlineData("warn", LogLevel.Warn)]
    [InlineData("error", LogLevel.Error)]
    [InlineData("verbose", LogLevel.Info)]
    public void ReadMinLevel_MapsNameToLevel(string? raw, LogLevel expected) =>
        Assert.Equal(expected, AppLogConfig.ReadMinLevel(raw));

    private FileAppLog OpenLog(LogLevel minLevel = LogLevel.Info, long maxBytes = 5 * 1024 * 1024) =>
        new(Path.Combine(_directory, "aria.log"), minLevel, maxBytes);

    private string[] ReadLines() => ReadAllLines().ToArray();

    private IEnumerable<string> ReadAllLines()
    {
        var path = Path.Combine(_directory, "aria.log");
        return !File.Exists(path)
            ? []
            : File.ReadAllLines(path).Where(l => l.Length > 0);
    }
}
