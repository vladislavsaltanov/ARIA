namespace Aria.Core.Model;

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed record PlaylistFileFade(
    [property: JsonPropertyName("seconds")] double Seconds,
    [property: JsonPropertyName("curve")] string Curve = "linear");

public sealed record PlaylistFileTransition(
    [property: JsonPropertyName("kind")] string Kind = "cut",
    [property: JsonPropertyName("seconds")] double? Seconds = null);

public sealed record PlaylistFileEntry(
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("color")] string? Color = null,
    [property: JsonPropertyName("note")] string? Note = null,
    [property: JsonPropertyName("gainDb")] double? GainDb = null,
    [property: JsonPropertyName("end")] string? End = null,
    [property: JsonPropertyName("in")] PlaylistFileFade? In = null,
    [property: JsonPropertyName("out")] PlaylistFileFade? Out = null,
    [property: JsonPropertyName("cueIn")] double? CueIn = null,
    [property: JsonPropertyName("cueOut")] double? CueOut = null,
    [property: JsonPropertyName("transition")] PlaylistFileTransition? Transition = null);

public sealed record PlaylistExportEntry(
    string File,
    PlaylistOverrides? Overrides = null,
    PlaylistFileTransition? Transition = null);

public sealed record PlaylistFileDocument(
    [property: JsonPropertyName("format")] string Format,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("entries")] ImmutableArray<PlaylistFileEntry> Entries);

public sealed class PlaylistFormatException(string reason) : Exception(reason);

public static class PlaylistFormat
{
    public const string FormatId = "aria-playlist";

    public const int CurrentVersion = 1;

    public const string FileExtension = ".aria-playlist.json";

    private static readonly HashSet<string> TransitionKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "cut",
        "gap",
        "crossfade",
    };

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Export(string name, IEnumerable<PlaylistExportEntry> entries)
    {
        var document = new PlaylistFileDocument(
            FormatId,
            CurrentVersion,
            name,
            entries.Select(ToFileEntry).ToImmutableArray());
        return JsonSerializer.Serialize(document, Options);
    }

    public static PlaylistFileDocument Import(string json)
    {
        PlaylistFileDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<PlaylistFileDocument>(json, Options);
        }
        catch (JsonException e)
        {
            throw new PlaylistFormatException($"bad-json: {e.Message}");
        }
        if (document is null)
        {
            throw new PlaylistFormatException("bad-json: пустой документ");
        }
        if (!string.Equals(document.Format, FormatId, StringComparison.Ordinal))
        {
            throw new PlaylistFormatException($"bad-format: {document.Format}");
        }
        if (document.Version != CurrentVersion)
        {
            throw new PlaylistFormatException($"bad-version: {document.Version}");
        }
        if (string.IsNullOrWhiteSpace(document.Name))
        {
            throw new PlaylistFormatException("bad-name: пустое имя плейлиста");
        }
        if (document.Entries.IsDefaultOrEmpty)
        {
            throw new PlaylistFormatException("bad-entries: плейлист пуст");
        }
        foreach (var entry in document.Entries)
        {
            ValidateEntry(entry);
        }
        return document;
    }

    public static PlaylistOverrides? ToOverrides(PlaylistFileEntry entry)
    {
        if (entry.Name is null
            && entry.Color is null
            && entry.Note is null
            && entry.GainDb is null
            && entry.End is null
            && entry.In is null
            && entry.Out is null
            && entry.CueIn is null
            && entry.CueOut is null)
        {
            return null;
        }
        return new PlaylistOverrides(
            entry.Name,
            entry.Color,
            entry.Note,
            entry.GainDb,
            entry.End is null ? null : ParseEnd(entry.End),
            entry.In is null ? null : ParseFade(entry.In),
            entry.Out is null ? null : ParseFade(entry.Out),
            entry.CueIn is null ? null : TimeSpan.FromSeconds(entry.CueIn.Value),
            entry.CueOut is null ? null : TimeSpan.FromSeconds(entry.CueOut.Value));
    }

    private static PlaylistFileEntry ToFileEntry(PlaylistExportEntry entry) => new(
        entry.File,
        entry.Overrides?.Name,
        entry.Overrides?.Color,
        entry.Overrides?.Note,
        entry.Overrides?.GainDb,
        entry.Overrides?.EndAction?.ToString().ToLowerInvariant(),
        entry.Overrides?.In is null ? null : ToFileFade(entry.Overrides.In),
        entry.Overrides?.Out is null ? null : ToFileFade(entry.Overrides.Out),
        entry.Overrides?.CueIn?.TotalSeconds,
        entry.Overrides?.CueOut?.TotalSeconds,
        entry.Transition);

    private static PlaylistFileFade ToFileFade(Fade fade) =>
        new(fade.Duration.TotalSeconds, fade.Curve.ToString().ToLowerInvariant());

    private static void ValidateEntry(PlaylistFileEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.File))
        {
            throw new PlaylistFormatException("bad-entry: пустой путь к файлу");
        }
        if (entry.GainDb is < -80 or > 12)
        {
            throw new PlaylistFormatException($"bad-entry: gainDb вне диапазона: {entry.File}");
        }
        if (entry.End is not null)
        {
            ParseEnd(entry.End);
        }
        if (entry.In is not null)
        {
            ParseFade(entry.In);
        }
        if (entry.Out is not null)
        {
            ParseFade(entry.Out);
        }
        if (entry.CueIn is < 0 || entry.CueOut is < 0)
        {
            throw new PlaylistFormatException($"bad-entry: cue отрицательный: {entry.File}");
        }
        if (entry.Transition is not null && !TransitionKinds.Contains(entry.Transition.Kind))
        {
            throw new PlaylistFormatException($"bad-entry: неизвестный переход: {entry.Transition.Kind}");
        }
        if (entry.Transition?.Seconds is < 0)
        {
            throw new PlaylistFormatException($"bad-entry: переход отрицательный: {entry.File}");
        }
    }

    private static EndAction ParseEnd(string value)
    {
        if (Enum.TryParse<EndAction>(value, ignoreCase: true, out var end))
        {
            return end;
        }
        throw new PlaylistFormatException($"bad-entry: неизвестное end: {value}");
    }

    private static Fade ParseFade(PlaylistFileFade value)
    {
        if (value.Seconds < 0)
        {
            throw new PlaylistFormatException("bad-entry: fade отрицательный");
        }
        if (!Enum.TryParse<FadeCurve>(value.Curve, ignoreCase: true, out var curve))
        {
            throw new PlaylistFormatException($"bad-entry: неизвестная кривая: {value.Curve}");
        }
        return new Fade(TimeSpan.FromSeconds(value.Seconds), curve);
    }
}
