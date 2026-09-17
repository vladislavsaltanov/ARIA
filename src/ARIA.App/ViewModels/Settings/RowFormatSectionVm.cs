namespace Aria.App.ViewModels.Settings;

using Aria.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public sealed partial class RowFormatSectionVm : ObservableObject
{
    private readonly Func<AppSettings> _snapshot;
    private readonly Action<AppSettings> _save;
    private readonly Action<AppSettings>? _applied;

    [ObservableProperty]
    private bool useFileName;

    [ObservableProperty]
    private string rowFormat = AppSettings.Default.RowFormat;

    [ObservableProperty]
    private string rowSettingsStatus = string.Empty;

    public RowFormatSectionVm(
        Func<AppSettings> snapshot,
        Action<AppSettings> save,
        Action<AppSettings>? applied,
        bool useFileName,
        string rowFormat)
    {
        _snapshot = snapshot;
        _save = save;
        _applied = applied;
        UseFileName = useFileName;
        RowFormat = rowFormat;
    }

    [RelayCommand]
    private void SaveRowSettings()
    {
        var current = _snapshot();
        var settings = new AppSettings(
            UseFileName,
            string.IsNullOrWhiteSpace(RowFormat) ? AppSettings.Default.RowFormat : RowFormat,
            current.Smoothing,
            current.DefaultEndAction,
            current.OutputDeviceId,
            current.OutputDeviceName,
            current.PreviewOutputDeviceId,
            current.PreviewOutputDeviceName,
            current.MeterSmoothing);
        RowFormat = settings.RowFormat;
        _save(settings);
        _applied?.Invoke(settings);
        RowSettingsStatus = "формат строк сохранён";
    }
}
