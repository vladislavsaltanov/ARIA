namespace Aria.App.Services;

using System.Text.Json;

public sealed record AppSettings(bool UseFileName, string RowFormat)
{
    public static AppSettings Default { get; } = new(false, "{name}");
}

public sealed class AppSettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(path))
            {
                return AppSettings.Default;
            }
            var dto = JsonSerializer.Deserialize<AppSettingsDto>(File.ReadAllText(path), Options);
            if (dto is null)
            {
                return AppSettings.Default;
            }
            return new AppSettings(dto.UseFileName, string.IsNullOrWhiteSpace(dto.RowFormat) ? AppSettings.Default.RowFormat : dto.RowFormat);
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return AppSettings.Default;
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(path, JsonSerializer.Serialize(new AppSettingsDto(settings.UseFileName, settings.RowFormat), Options));
    }

    private sealed record AppSettingsDto(bool UseFileName, string RowFormat);
}

public static class RowFormatter
{
    public static string Format(string format, string position, string name, string fileName, string duration)
    {
        var template = string.IsNullOrWhiteSpace(format) ? AppSettings.Default.RowFormat : format;
        return template
            .Replace("{position}", position, StringComparison.Ordinal)
            .Replace("{name}", name, StringComparison.Ordinal)
            .Replace("{filename}", fileName, StringComparison.Ordinal)
            .Replace("{duration}", duration, StringComparison.Ordinal);
    }

    public static string DisplayName(AppSettings settings, string name, string fileName) =>
        settings.UseFileName ? fileName : name;
}
