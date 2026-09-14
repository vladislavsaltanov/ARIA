namespace Aria.App.ViewModels;

using System.Threading;
using Aria.App.Services;
using Aria.App.ViewModels.Settings;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;
using Aria.Core.State;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly ICommandBus _bus;
    private readonly ClientId _client = new("desktop-settings");
    private readonly AppSettingsStore _settingsStore;
    private readonly Action<AppSettings>? _rowSettingsApplied;
    private readonly IDisposable _subscription;
    private readonly SynchronizationContext? _sync;
    private long _seq;

    [ObservableProperty]
    private bool useFileName;

    [ObservableProperty]
    private string rowFormat = AppSettings.Default.RowFormat;

    [ObservableProperty]
    private string rowSettingsStatus = string.Empty;

    public HotkeysSectionVm Hotkeys { get; }

    public EngineSectionVm Engine { get; }

    public PlaybackSectionVm Playback { get; }

    public SettingsViewModel(
        ICommandBus bus,
        HotkeyService hotkeys,
        string hotkeysPath,
        AppSettingsStore settingsStore,
        Action<AppSettings>? rowSettingsApplied = null,
        SynchronizationContext? sync = null)
    {
        _bus = bus;
        _settingsStore = settingsStore;
        _rowSettingsApplied = rowSettingsApplied;
        _sync = sync;
        Hotkeys = new HotkeysSectionVm(hotkeys, hotkeysPath);
        Engine = new EngineSectionVm(Submit, SnapshotSettings, SaveSettings);
        _subscription = bus.Subscribe(Apply);
        var settings = settingsStore.Load();
        UseFileName = settings.UseFileName;
        RowFormat = settings.RowFormat;
        Playback = new PlaybackSectionVm(Submit, SnapshotSettings, SaveSettings, settings.DefaultEndAction);
        Engine.ApplyMixer(bus.Snapshot().Mixer);
        Engine.ApplySmoothing(settings.Smoothing);
        if (bus.Snapshot().Mixer.Smoothing != settings.Smoothing)
        {
            Submit(new SetSmoothing(settings.Smoothing));
        }
        OnPropertyChanged(nameof(Playback.DefaultEndActionIndex));
        Submit(new SetDefaultEndAction(settings.DefaultEndAction));
    }

    [RelayCommand]
    private void ResetClock() => Submit(new ResetShowClock());

    [RelayCommand]
    private void SaveRowSettings()
    {
        var settings = new AppSettings(UseFileName, string.IsNullOrWhiteSpace(RowFormat) ? AppSettings.Default.RowFormat : RowFormat, Engine.CurrentSmoothing(), Playback.CurrentEndAction);
        RowFormat = settings.RowFormat;
        _settingsStore.Save(settings);
        _rowSettingsApplied?.Invoke(settings);
        RowSettingsStatus = "формат строк сохранён";
    }

    public void Dispose() => _subscription.Dispose();

    private void Submit(Command command) => _bus.Submit(_client, Interlocked.Increment(ref _seq), command);

    public AppSettings SnapshotSettings() => new(UseFileName, RowFormat, Engine.CurrentSmoothing(), Playback.CurrentEndAction);

    public void SaveSettings(AppSettings settings) => _settingsStore.Save(settings);

    private void Apply(StateEvent e)
    {
        if (e is MixerDelta delta)
        {
            Post(() => Engine.ApplyMixer(delta.State));
        }
    }

    private void Post(Action work)
    {
        if (_sync is { } sync)
        {
            sync.Post(_ => work(), null);
        }
        else
        {
            work();
        }
    }

}


