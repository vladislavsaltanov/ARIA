namespace Aria.App.Services;

using Aria.Audio;

public sealed class AudioOutputService
{
    public const string SystemDefaultId = "";

    private readonly IAudioOutputLister _lister;
    private readonly AppSettingsStore _settingsStore;

    public AudioOutputService(IAudioOutputLister lister, AppSettingsStore settingsStore)
    {
        _lister = lister;
        _settingsStore = settingsStore;
    }

    public IReadOnlyList<OutputDevice> Devices => throw new NotImplementedException();

    public string SelectedId => throw new NotImplementedException();

    public string Status => throw new NotImplementedException();

    public void Refresh() => throw new NotImplementedException();

    public bool Select(string id) => throw new NotImplementedException();
}
