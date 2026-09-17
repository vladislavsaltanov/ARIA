namespace Aria.App.ViewModels.Settings;

using System.Diagnostics;
using Aria.App.Services;
using Aria.Core.Runtime;
using CommunityToolkit.Mvvm.ComponentModel;

public sealed partial class LogSectionVm : ObservableObject
{
    private readonly Func<AppSettings> _snapshot;
    private readonly Action<AppSettings> _save;
    private readonly Action<LogLevel>? _applied;
    private LogLevel _level;

    public LogSectionVm(Func<AppSettings> snapshot, Action<AppSettings> save, LogLevel initial, string logPath, Action<LogLevel>? applied = null)
    {
        _snapshot = snapshot;
        _save = save;
        _level = initial;
        LogPath = logPath;
        _applied = applied;
    }

    public string LogPath { get; }

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
                _save(_snapshot() with { LogLevel = level });
                _applied?.Invoke(level);
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
}
