namespace Aria.App.Services;

using Aria.Core.Commands;

public static class HotkeyCommands
{
    public static Command? ToCommand(string action, bool locked) => action switch
    {
        "play" => new Play(),
        "pause" => new Pause(),
        "stop" => new Stop(),
        "next" => new Next(),
        "replay" => new Replay(),
        "panic" => new Panic(),
        "lock" => new SetLocked(!locked),
        "reset-clock" => new ResetShowClock(),
        _ => null,
    };
}
