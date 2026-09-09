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
