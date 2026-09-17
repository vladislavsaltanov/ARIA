namespace Aria.App.ViewModels.Settings;

using System.Diagnostics;
using Aria.App.Services;
using Aria.Core.Runtime;
using CommunityToolkit.Mvvm.ComponentModel;

public sealed partial class LogSectionVm : ObservableObject
{
    private readonly Func<AppSettings> _snapshot;
    private readonly Action<AppSettings> _save;
    private readonly Action<AppSettings>? _applied;
    private LogLevel _level;
    private bool _enabled;

    public LogSectionVm(Func<AppSettings> snapshot, Action<AppSettings> save, LogLevel initial, bool enabled, string logPath, Action<AppSettings>? applied = null)
    {
        _snapshot = snapshot;
        _save = save;
        _level = initial;
        _enabled = enabled;
        LogPath = logPath;
        _applied = applied;
    }

    public string LogPath { get; }

    public bool LogEnabled
    {
        get => _enabled;
        set
        {
            if (SetProperty(ref _enabled, value))
            {
                Update(_snapshot() with { LogEnabled = value });
            }
        }
    }

    public int LogLevelIndex
    {
        get => _level switch
        {
            LogLevel.Debug => 0,
            LogLevel.Warn => 2,
            LogLevel.Error => 3,
            _ => 1,
        };
        set
        {
            var level = value switch
            {
                0 => LogLevel.Debug,
                2 => LogLevel.Warn,
                3 => LogLevel.Error,
                _ => LogLevel.Info,
            };
            if (SetProperty(ref _level, level))
            {
                Update(_snapshot() with { LogLevel = level });
            }
        }
    }

    public void OpenFolder()
    {
        try
        {
            var directory = Path.GetDirectoryName(LogPath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return;
            }
            Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
        }
        catch (Exception)
        {
        }
    }

    private void Update(AppSettings next)
    {
        _save(next);
        _applied?.Invoke(next);
    }
}
