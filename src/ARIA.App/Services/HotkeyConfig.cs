namespace Aria.App.Services;

using System.Collections.Immutable;
using System.Text.Json;

public sealed record HotkeyBinding(string Gesture, string Action);

public sealed record HotkeyConfig(ImmutableArray<HotkeyBinding> Bindings)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static HotkeyConfig Default { get; } = new(
    [
        new HotkeyBinding("Space", "play"),
        new HotkeyBinding("Escape", "pause"),
        new HotkeyBinding("Ctrl+Shift+P", "panic"),
        new HotkeyBinding("Ctrl+N", "next"),
        new HotkeyBinding("Ctrl+R", "replay"),
        new HotkeyBinding("Ctrl+L", "lock"),
        new HotkeyBinding("Ctrl+T", "toggle-script"),
        new HotkeyBinding("Ctrl+Shift+C", "reset-clock"),
    ]);

    public static HotkeyConfig Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return Default;
            }
            var dto = JsonSerializer.Deserialize<HotkeyConfigDto>(File.ReadAllText(path), Options);
            if (dto?.Bindings is not { Count: > 0 })
            {
                return Default;
            }
            return new HotkeyConfig([.. dto.Bindings.Select(b => new HotkeyBinding(b.Gesture, b.Action))]);
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return Default;
        }
    }

    public void Save(string path)
    {
        var dto = new HotkeyConfigDto([.. Bindings.Select(b => new HotkeyBindingDto(b.Gesture, b.Action))]);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(path, JsonSerializer.Serialize(dto, Options));
    }
}

internal sealed record HotkeyConfigDto(List<HotkeyBindingDto> Bindings);

internal sealed record HotkeyBindingDto(string Gesture, string Action);
