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
        var applied = new List<AppSettings>();
        var vm = new LogSectionVm(() => AppSettings.Default, saved.Add, LogLevel.Info, false, "/tmp/aria.log", applied.Add);

        vm.LogLevelIndex = 3;

        Assert.Equal(LogLevel.Error, Assert.Single(saved).LogLevel);
        Assert.Equal(LogLevel.Error, Assert.Single(applied).LogLevel);
    }

    [Fact]
    public void SetEnabled_SavesAndApplies()
    {
        var saved = new List<AppSettings>();
        var applied = new List<AppSettings>();
        var vm = new LogSectionVm(() => AppSettings.Default, saved.Add, LogLevel.Info, false, "/tmp/aria.log", applied.Add);

        vm.LogEnabled = true;

        Assert.True(Assert.Single(saved).LogEnabled);
        Assert.True(Assert.Single(applied).LogEnabled);
    }

    [Fact]
    public void OpenFolder_BrokenPath_DoesNotThrow()
    {
        var vm = CreateVm(LogLevel.Info, false, "");

        var exception = Record.Exception(() => vm.OpenFolder());

        Assert.Null(exception);
    }

    private static LogSectionVm CreateVm(LogLevel level, bool enabled = false, string path = "/tmp/aria.log") =>
        new(() => AppSettings.Default, _ => { }, level, enabled, path);
}
