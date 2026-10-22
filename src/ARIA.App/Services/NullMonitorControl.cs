namespace Aria.App.Services;

using Aria.Remote.Monitor;

public sealed class NullMonitorControl : IMonitorControl
{
    public static NullMonitorControl Instance { get; } = new();

    public IReadOnlyList<ListenerProfile> Listeners => [];

    public double HallTrimMs { get; set; }

    public double DeviceBufferMs => 0;

    public bool TestClickEnabled { get; set; }

    public void SetLevels(string id, ListenerLevels levels)
    {
    }

    public void Kick(string id)
    {
    }
}
