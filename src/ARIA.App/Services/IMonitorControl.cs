namespace Aria.App.Services;

using Aria.Remote.Monitor;

public interface IMonitorControl
{
    IReadOnlyList<ListenerProfile> Listeners { get; }

    double HallTrimMs { get; set; }

    double DeviceBufferMs { get; }

    bool TestClickEnabled { get; set; }

    void SetLevels(string id, ListenerLevels levels);

    void Kick(string id);
}
