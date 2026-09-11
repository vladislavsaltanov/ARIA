namespace Aria.App.Services;

public sealed class HotkeyService
{
    private readonly Dictionary<string, string> _bindings;
    private readonly Action<string> _dispatch;

    public HotkeyService(HotkeyConfig config, Action<string> dispatch)
    {
        _dispatch = dispatch;
        _bindings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var binding in config.Bindings)
        {
            var gesture = Normalize(binding.Gesture);
            if (!_bindings.ContainsKey(gesture))
            {
                _bindings.Add(gesture, binding.Action);
            }
        }
    }

    public bool TryHandle(string gesture)
    {
        var normalized = Normalize(gesture);
        if (normalized.Length == 0 || !_bindings.TryGetValue(normalized, out var action))
        {
            return false;
        }
        _dispatch(action);
        return true;
    }

    public string GestureFor(string action)
    {
        foreach (var (gesture, bound) in _bindings)
        {
            if (bound == action)
            {
                return Display(gesture);
            }
        }
        return string.Empty;
    }

    private static string Display(string gesture)
    {
        var parts = gesture.Split('+');
        for (var i = 0; i < parts.Length; i++)
        {
            parts[i] = parts[i] switch
            {
                "ctrl" => "Ctrl",
                "alt" => "Alt",
                "shift" => "Shift",
                "meta" => "Meta",
                "space" => "Space",
                "escape" => "Esc",
                _ when parts[i].Length == 1 => parts[i].ToUpperInvariant(),
                _ => parts[i],
            };
        }
        return string.Join('+', parts);
    }

    private static string Normalize(string gesture)
    {
        var parts = gesture.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var modifiers = new List<string>(4);
        var keys = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            var lower = part.ToLowerInvariant();
            if (lower is "ctrl" or "alt" or "shift" or "meta" && !modifiers.Contains(lower))
            {
                modifiers.Add(lower);
            }
            else
            {
                keys.Add(lower);
            }
        }
        return string.Join('+', modifiers.OrderBy(m => m switch
        {
            "ctrl" => 0,
            "alt" => 1,
            "shift" => 2,
            _ => 3,
        }).Concat(keys));
    }
}
