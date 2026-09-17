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
    private string _previewStatus = string.Empty;

    public AudioOutputService(IAudioOutputLister lister, AppSettingsStore settingsStore)
    {
        _lister = lister;
        _settingsStore = settingsStore;
        Refresh();
        _status = SelectedName(SelectedId);
        _previewStatus = SelectedName(SelectedPreviewId);
    }

    public event Action? Changed;

    public IReadOnlyList<OutputDevice> Devices => _devices;

    public string SelectedId => _settingsStore.Load().OutputDeviceId;

    public string SelectedPreviewId => _settingsStore.Load().PreviewOutputDeviceId;

    public string Status => _status;

    public string PreviewStatus => _previewStatus;

    public void Refresh()
    {
        _devices = [new OutputDevice(SystemDefaultId, SystemName, true), .. _lister.ListPlaybackDevices()];
        if (SelectedId != SystemDefaultId && _devices.All(d => d.Id != SelectedId))
        {
            PersistMain(SystemDefaultId, string.Empty);
            _status = SelectedName(SelectedId);
        }
        if (SelectedPreviewId != SystemDefaultId && _devices.All(d => d.Id != SelectedPreviewId))
        {
            PersistPreview(SystemDefaultId, string.Empty);
            _previewStatus = SelectedName(SelectedPreviewId);
        }
        Changed?.Invoke();
    }

    public bool Select(string id) => SelectCore(id, PersistMain, v => _status = v);

    public bool SelectPreview(string id) => SelectCore(id, PersistPreview, v => _previewStatus = v);

    public void NotifyDeviceFault(string message)
    {
        _status = message;
        Changed?.Invoke();
    }

    public void NotifyPreviewFault(string message)
    {
        _previewStatus = message;
        Changed?.Invoke();
    }

    private bool SelectCore(string id, Action<string, string> persist, Action<string> notice)
    {
        var device = _devices.FirstOrDefault(d => d.Id == id);
        if (device is null)
        {
            return false;
        }
        persist(device.Id, device.Id == SystemDefaultId ? string.Empty : device.Name);
        notice(SelectedName(device.Id));
        Changed?.Invoke();
        return true;
    }

    private string SelectedName(string id) =>
        _devices.FirstOrDefault(d => d.Id == id)?.Name ?? SystemName;

    private void PersistMain(string id, string name)
    {
        var settings = _settingsStore.Load();
        _settingsStore.Save(settings with { OutputDeviceId = id, OutputDeviceName = name });
    }

    private void PersistPreview(string id, string name)
    {
        var settings = _settingsStore.Load();
        _settingsStore.Save(settings with { PreviewOutputDeviceId = id, PreviewOutputDeviceName = name });
    }
}
