namespace Aria.Remote.Monitor;

public sealed record ListenerLevels(double BackingDb, bool BackingOn, double ClickDb, bool ClickOn)
{
    public static ListenerLevels Default { get; } = new(0, true, 0, true);
}

public sealed record ListenerStats(long OffsetUs, long UncertaintyUs, long RttUs, double OutputLatencyMs, double TrimMs, string State);

public sealed record ListenerProfile(string Id, string Name, RemoteRole Role, ListenerLevels Levels, ListenerStats? Stats, DateTimeOffset ConnectedAt);
