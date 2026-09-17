namespace Aria.App.Services;

using Aria.Audio;

public sealed class AudioOutputService
{
    public const string SystemDefaultId = "";

    private const string SystemName = "System";

    private readonly IAudioOutputLister _lister;
    private readonly AppSettingsStore _settingsStore;
    private IReadOnlyList<OutputDevice> _devices = [];
    private string _status = string.Empty;

    public AudioOutputService(IAudioOutputLister lister, AppSettingsStore settingsStore)
    {
        _lister = lister;
        _settingsStore = settingsStore;
        Refresh();
        _status = SelectedName;
    }

    public event Action? Changed;

    public IReadOnlyList<OutputDevice> Devices => _devices;

    public string SelectedId => _settingsStore.Load().OutputDeviceId;

    public string Status => _status;

    public void Refresh()
    {
        _devices = [new OutputDevice(SystemDefaultId, SystemName, true), .. _lister.ListPlaybackDevices()];
        if (SelectedId != SystemDefaultId && _devices.All(d => d.Id != SelectedId))
        {
            Persist(SystemDefaultId, string.Empty);
            _status = SelectedName;
        }
        Changed?.Invoke();
    }

    public bool Select(string id)
    {
        var device = _devices.FirstOrDefault(d => d.Id == id);
        if (device is null)
        {
            return false;
        }
        Persist(device.Id, device.Id == SystemDefaultId ? string.Empty : device.Name);
        _status = SelectedName;
        Changed?.Invoke();
        return true;
    }

    public void NotifyDeviceFault(string message)
    {
        _status = message;
        Changed?.Invoke();
    }

    private string SelectedName =>
        _devices.FirstOrDefault(d => d.Id == SelectedId)?.Name ?? SystemName;

    private void Persist(string id, string name)
    {
        var settings = _settingsStore.Load();
        _settingsStore.Save(settings with { OutputDeviceId = id, OutputDeviceName = name });
    }
}
