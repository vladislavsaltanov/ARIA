namespace Aria.App.Services;

using System.Text.Json;
using Aria.Core.Model;
using Aria.Core.Runtime;

public sealed record MeterSmoothing(bool Enabled, int ReleaseMs)
{
    public static MeterSmoothing Default { get; } = new(true, 250);
}

public sealed record AppSettings(bool UseFileName, string RowFormat, Smoothing Smoothing, EndAction DefaultEndAction = EndAction.Advance, string OutputDeviceId = "", string OutputDeviceName = "", string PreviewOutputDeviceId = "", string PreviewOutputDeviceName = "", MeterSmoothing? MeterSmoothing = null, LogLevel LogLevel = LogLevel.Info)
{
    public MeterSmoothing EffectiveMeterSmoothing => MeterSmoothing ?? MeterSmoothing.Default;

    public static AppSettings Default { get; } = new(false, "{name}", Smoothing.Default, EndAction.Advance);
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
            return new AppSettings(
                dto.UseFileName,
                string.IsNullOrWhiteSpace(dto.RowFormat) ? AppSettings.Default.RowFormat : dto.RowFormat,
                dto.Smoothing?.ToModel() ?? Smoothing.Default,
                dto.DefaultEndAction ?? EndAction.Advance,
                dto.OutputDeviceId ?? string.Empty,
                dto.OutputDeviceName ?? string.Empty,
                dto.PreviewOutputDeviceId ?? string.Empty,
                dto.PreviewOutputDeviceName ?? string.Empty,
                dto.MeterSmoothing?.ToModel(),
                dto.LogLevel ?? LogLevel.Info);
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
        File.WriteAllText(path, JsonSerializer.Serialize(AppSettingsDto.FromModel(settings), Options));
    }

    private sealed record AppSettingsDto(
        bool UseFileName,
        string RowFormat,
        SmoothingDto? Smoothing,
        EndAction? DefaultEndAction,
        string? OutputDeviceId = null,
        string? OutputDeviceName = null,
        string? PreviewOutputDeviceId = null,
        string? PreviewOutputDeviceName = null,
        MeterSmoothingDto? MeterSmoothing = null,
        LogLevel? LogLevel = null)
    {
        public static AppSettingsDto FromModel(AppSettings settings) => new(
            settings.UseFileName,
            settings.RowFormat,
            SmoothingDto.FromModel(settings.Smoothing),
            settings.DefaultEndAction,
            settings.OutputDeviceId,
            settings.OutputDeviceName,
            settings.PreviewOutputDeviceId,
            settings.PreviewOutputDeviceName,
            settings.MeterSmoothing is null ? null : MeterSmoothingDto.FromModel(settings.MeterSmoothing),
            settings.LogLevel);

        public Smoothing ToModel()
        {
            if (Smoothing is null)
            {
                return Aria.Core.Model.Smoothing.Default;
            }
            return Smoothing.ToModel();
        }
    }

    private sealed record MeterSmoothingDto(bool Enabled, int ReleaseMs)
    {
        public static MeterSmoothingDto FromModel(MeterSmoothing smoothing) => new(smoothing.Enabled, smoothing.ReleaseMs);

        public MeterSmoothing ToModel()
        {
            var fallback = MeterSmoothing.Default;
            return new MeterSmoothing(Enabled, ReleaseMs is < 0 or > 2000 ? fallback.ReleaseMs : ReleaseMs);
        }
    }

    private sealed record SmoothingDto(
        bool Enabled,
        long ManualCrossfadeMs,
        long AutoCrossfadeMs,
        long StartFadeMs,
        long StopFadeMs,
        long? SeekFadeMs)
    {
        public static SmoothingDto FromModel(Smoothing smoothing) => new(
            smoothing.Enabled,
            (long)smoothing.ManualCrossfade.TotalMilliseconds,
            (long)smoothing.AutoCrossfade.TotalMilliseconds,
            (long)smoothing.StartFade.TotalMilliseconds,
            (long)smoothing.StopFade.TotalMilliseconds,
            (long)smoothing.SeekFade.TotalMilliseconds);

        public Smoothing ToModel()
        {
            var fallback = Aria.Core.Model.Smoothing.Default;
            return new Smoothing(
                Enabled,
                Clamp(ManualCrossfadeMs, fallback.ManualCrossfade),
                Clamp(AutoCrossfadeMs, fallback.AutoCrossfade),
                Clamp(StartFadeMs, fallback.StartFade),
                Clamp(StopFadeMs, fallback.StopFade),
                SeekFadeMs is { } seekMs ? Clamp(seekMs, fallback.SeekFade) : fallback.SeekFade);
        }

        private static TimeSpan Clamp(long ms, TimeSpan fallback) =>
            ms < 0 || ms > 5000 ? fallback : TimeSpan.FromMilliseconds(ms);
    }
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
