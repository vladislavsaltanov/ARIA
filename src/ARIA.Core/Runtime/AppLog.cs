namespace Aria.Core.Runtime;

using System.Text.Json;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
}

public interface IAppLog
{
    void Write(LogLevel level, string message, IReadOnlyDictionary<string, string>? data = null);
}

public static class AppLogExtensions
{
    public static void Debug(this IAppLog log, string message, IReadOnlyDictionary<string, string>? data = null) =>
        log.Write(LogLevel.Debug, message, data);

    public static void Info(this IAppLog log, string message, IReadOnlyDictionary<string, string>? data = null) =>
        log.Write(LogLevel.Info, message, data);

    public static void Warn(this IAppLog log, string message, IReadOnlyDictionary<string, string>? data = null) =>
        log.Write(LogLevel.Warn, message, data);

    public static void Error(this IAppLog log, string message, IReadOnlyDictionary<string, string>? data = null) =>
        log.Write(LogLevel.Error, message, data);
}

public sealed class NullAppLog : IAppLog
{
    public static readonly NullAppLog Instance = new();

    private NullAppLog()
    {
    }

    public void Write(LogLevel level, string message, IReadOnlyDictionary<string, string>? data = null)
    {
    }
}

public sealed class FileAppLog : IAppLog, IDisposable
{
    private const long DefaultMaxBytes = 5 * 1024 * 1024;
    private readonly string _path;
    private LogLevel _minLevel;
    private readonly long _maxBytes;
    private readonly object _gate = new();
    private StreamWriter? _writer;
    private bool _disposed;

    public FileAppLog(string path, LogLevel minLevel = LogLevel.Info, long maxBytes = DefaultMaxBytes)
    {
        _path = path;
        _minLevel = minLevel;
        _maxBytes = maxBytes <= 0 ? DefaultMaxBytes : maxBytes;
    }

    public void Write(LogLevel level, string message, IReadOnlyDictionary<string, string>? data = null)
    {
        if (level < _minLevel)
        {
            return;
        }
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            try
            {
                EnsureWriter();
                if (_writer is null)
                {
                    return;
                }
                _writer.WriteLine(Render(level, message, data));
                _writer.Flush();
            }
            catch (Exception)
            {
                CloseWriter();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            CloseWriter();
        }
    }

    public void SetMinLevel(LogLevel level)
    {
        lock (_gate)
        {
            _minLevel = level;
        }
    }

    private void EnsureWriter()
    {
        if (_writer is null)
        {
            if (Path.GetDirectoryName(_path) is { } directory)
            {
                Directory.CreateDirectory(directory);
            }
            RotateIfNeeded();
            _writer = new StreamWriter(
                new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read));
            return;
        }
        if (_writer.BaseStream.Length >= _maxBytes)
        {
            CloseWriter();
            RotateIfNeeded();
            _writer = new StreamWriter(
                new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read));
        }
    }

    private void RotateIfNeeded()
    {
        CloseWriter();
        var info = new FileInfo(_path);
        if (!info.Exists || info.Length < _maxBytes)
        {
            return;
        }
        var backup = _path + ".1";
        if (File.Exists(backup))
        {
            File.Delete(backup);
        }
        File.Move(_path, backup);
    }

    private void CloseWriter()
    {
        _writer?.Dispose();
        _writer = null;
    }

    private static string Render(LogLevel level, string message, IReadOnlyDictionary<string, string>? data)
    {
        var entry = new Dictionary<string, object>
        {
            ["ts"] = DateTimeOffset.UtcNow.ToString("o"),
            ["level"] = level.ToString().ToLowerInvariant(),
            ["msg"] = message,
        };
        if (data is { Count: > 0 })
        {
            entry["data"] = data;
        }
        return JsonSerializer.Serialize(entry);
    }
}

public static class AppLogConfig
{
    public static LogLevel ReadMinLevel(string? raw) =>
        raw?.ToLowerInvariant() switch
        {
            "debug" => LogLevel.Debug,
            "warn" => LogLevel.Warn,
            "error" => LogLevel.Error,
            _ => LogLevel.Info,
        };
}
