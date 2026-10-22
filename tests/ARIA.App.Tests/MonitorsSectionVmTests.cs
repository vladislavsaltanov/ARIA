namespace Aria.App.Tests;

using Aria.App.Services;
using Aria.App.ViewModels.Settings;
using Aria.Remote;
using Aria.Remote.Monitor;

public sealed class MonitorsSectionVmTests
{
    private sealed class FakeControl : IMonitorControl
    {
        public List<ListenerProfile> Profiles { get; } = [];

        public List<(string Id, ListenerLevels Levels)> LevelCalls { get; } = [];

        public List<string> Kicked { get; } = [];

        public IReadOnlyList<ListenerProfile> Listeners => Profiles.ToArray();

        public double HallTrimMs { get; set; }

        public double DeviceBufferMs { get; set; } = 10.7;

        public bool TestClickEnabled { get; set; }

        public void SetLevels(string id, ListenerLevels levels) => LevelCalls.Add((id, levels));

        public void Kick(string id) => Kicked.Add(id);
    }

    private static ListenerProfile Profile(string id, string name, ListenerStats? stats = null, ListenerLevels? levels = null) =>
        new(id, name, RemoteRole.Listen, levels ?? ListenerLevels.Default, stats, DateTimeOffset.UnixEpoch);

    private static ListenerStats Stats(long uncertaintyUs, string state) => new(0, uncertaintyUs, 2000, 20, 0, state);

    [Fact]
    public void Refresh_AddsAndRemovesListeners()
    {
        var control = new FakeControl();
        control.Profiles.AddRange([Profile("a", "Drums"), Profile("b", "Keys")]);
        using var section = new MonitorsSectionVm(control);
        var drums = section.Listeners[0];

        control.Profiles.RemoveAt(1);
        control.Profiles[0] = control.Profiles[0] with { Name = "Drums2" };
        control.Profiles.Add(Profile("c", "Bass"));
        section.Refresh();

        Assert.Equal(["a", "c"], section.Listeners.Select(l => l.Id));
        Assert.Same(drums, section.Listeners[0]);
        Assert.Equal("Drums2", section.Listeners[0].Name);
    }

    [Fact]
    public void Levels_Change_CallsSetLevels()
    {
        var control = new FakeControl();
        control.Profiles.Add(Profile("a", "Drums", levels: new ListenerLevels(-3, false, -6, true)));
        using var section = new MonitorsSectionVm(control);
        var row = section.Listeners[0];

        row.BackingDb = -10;
        row.ClickDb = 40;

        Assert.Equal(
            [("a", new ListenerLevels(-10, false, -6, true)), ("a", new ListenerLevels(-10, false, 12, true))],
            control.LevelCalls);
    }

    [Fact]
    public void Refresh_TakesLevelsFromListener_WithoutEcho()
    {
        var control = new FakeControl();
        control.Profiles.Add(Profile("a", "Drums"));
        using var section = new MonitorsSectionVm(control);

        control.Profiles[0] = control.Profiles[0] with { Levels = new ListenerLevels(-12, true, -4, true) };
        section.Refresh();

        Assert.Equal(-12, section.Listeners[0].BackingDb);
        Assert.Equal(-4, section.Listeners[0].ClickDb);
        Assert.Empty(control.LevelCalls);
    }

    [Fact]
    public void Kick_CallsKick()
    {
        var control = new FakeControl();
        control.Profiles.Add(Profile("a", "Drums"));
        using var section = new MonitorsSectionVm(control);

        section.Listeners[0].KickCommand.Execute(null);

        Assert.Equal(["a"], control.Kicked);
    }

    [Fact]
    public void SyncText_FormatsUncertainty()
    {
        var control = new FakeControl();
        control.Profiles.AddRange([
            Profile("a", "Drums", Stats(900, "locked")),
            Profile("b", "Keys", Stats(900, "syncing")),
            Profile("c", "Bass"),
        ]);
        using var section = new MonitorsSectionVm(control);

        Assert.Equal(["±0,9 мс", "нет синхр.", "нет синхр."], section.Listeners.Select(l => l.SyncText));
    }

    [Fact]
    public void HallTrim_Clamps_AndApplies()
    {
        var control = new FakeControl { HallTrimMs = 15 };
        using var section = new MonitorsSectionVm(control);
        Assert.Equal(15, section.HallTrimMs);

        section.HallTrimMs = -80;
        Assert.Equal(-50, section.HallTrimMs);
        Assert.Equal(-50, control.HallTrimMs);

        section.HallTrimMs = 300;
        Assert.Equal(200, section.HallTrimMs);
        Assert.Equal(200, control.HallTrimMs);
    }

    [Fact]
    public void DeviceBufferText_ShowsEstimate()
    {
        var control = new FakeControl { DeviceBufferMs = 10.7 };
        using var section = new MonitorsSectionVm(control);

        Assert.Equal("буфер устройства: 11 мс", section.DeviceBufferText);
    }

    [Fact]
    public void TestClick_TogglesAndReflectsAutoOff()
    {
        var control = new FakeControl();
        using var section = new MonitorsSectionVm(control);

        section.TestClickEnabled = true;
        Assert.True(control.TestClickEnabled);

        control.TestClickEnabled = false;
        section.Refresh();

        Assert.False(section.TestClickEnabled);
    }
}
