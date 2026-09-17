namespace Aria.App.Tests;

using Aria.App.Services;
using Aria.App.ViewModels.Settings;
using Aria.Core.Runtime;

public sealed class LogSectionVmTests
{
    [Theory]
    [InlineData(LogLevel.Debug, 0)]
    [InlineData(LogLevel.Info, 1)]
    [InlineData(LogLevel.Warn, 2)]
    [InlineData(LogLevel.Error, 3)]
    public void Index_MapsLevelBothWays(LogLevel level, int index)
    {
        var vm = CreateVm(level);

        Assert.Equal(index, vm.LogLevelIndex);
    }

    [Fact]
    public void SetIndex_SavesLevel_AndAppliesLive()
    {
        var saved = new List<AppSettings>();
        var applied = new List<LogLevel>();
        var vm = new LogSectionVm(() => AppSettings.Default, saved.Add, LogLevel.Info, "/tmp/aria.log", applied.Add);

        vm.LogLevelIndex = 3;

        Assert.Equal(LogLevel.Error, Assert.Single(saved).LogLevel);
        Assert.Equal(LogLevel.Error, Assert.Single(applied));
    }

    [Fact]
    public void OpenFolder_BrokenPath_DoesNotThrow()
    {
        var vm = CreateVm(LogLevel.Info, "");

        var exception = Record.Exception(() => vm.OpenFolder());

        Assert.Null(exception);
    }

    private static LogSectionVm CreateVm(LogLevel level, string path = "/tmp/aria.log") =>
        new(() => AppSettings.Default, _ => { }, level, path);
}
