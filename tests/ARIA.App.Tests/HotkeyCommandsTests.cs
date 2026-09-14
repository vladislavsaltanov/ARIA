namespace Aria.App.Tests;

using Aria.App.Services;
using Aria.Core.Commands;
using Aria.Core.Runtime;
using Aria.Core.State;

public sealed class HotkeyCommandsTests
{
    [Theory]
    [InlineData("play", typeof(Play))]
    [InlineData("pause", typeof(Pause))]
    [InlineData("stop", typeof(Stop))]
    [InlineData("next", typeof(Next))]
    [InlineData("replay", typeof(Replay))]
    [InlineData("panic", typeof(Panic))]
    [InlineData("reset-clock", typeof(ResetShowClock))]
    public void ToCommand_MapsPlayerActions(string action, Type command)
    {
        Assert.Equal(command, HotkeyCommands.ToCommand(action, false)?.GetType());
    }

    [Theory]
    [InlineData("toggle-script")]
    [InlineData("no-such-action")]
    public void ToCommand_ReturnsNull_ForUiLocalAndUnknown(string action)
    {
        Assert.Null(HotkeyCommands.ToCommand(action, false));
    }

    [Fact]
    public void ToCommand_Lock_Toggles()
    {
        Assert.Equal(new SetLocked(true), HotkeyCommands.ToCommand("lock", false));
        Assert.Equal(new SetLocked(false), HotkeyCommands.ToCommand("lock", true));
    }

    [Fact]
    public void MappedPanic_ThroughBus_Panics()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var command = Assert.IsType<Panic>(HotkeyCommands.ToCommand("panic", false));

        bus.Submit(new ClientId("hotkey-panic"), 1, command);

        Assert.Equal(TransportStatus.Panicked, bus.Snapshot().Transport.Status);
    }

    [Fact]
    public void ServiceDispatch_ThroughMapper_LocksShow()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var service = new HotkeyService(HotkeyConfig.Default, action =>
        {
            if (HotkeyCommands.ToCommand(action, bus.Snapshot().Show.Locked) is { } command)
            {
                bus.Submit(new ClientId("hotkey-lock"), 1, command);
            }
        });

        Assert.True(service.TryHandle("ctrl+l"));

        Assert.True(bus.Snapshot().Show.Locked);
    }
}
