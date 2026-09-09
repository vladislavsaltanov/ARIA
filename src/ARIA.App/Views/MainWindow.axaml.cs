namespace Aria.App.Views;

using System.Text;
using Aria.App.Services;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

public partial class MainWindow : Window
{
    private readonly HotkeyService? _hotkeys;

    public MainWindow() : this(null)
    {
    }

    public MainWindow(HotkeyService? hotkeys)
    {
        InitializeComponent();
        _hotkeys = hotkeys;
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_hotkeys is null)
        {
            return;
        }
        if (_hotkeys.TryHandle(BuildGesture(e.KeyModifiers, GestureKey(e.Key))))
        {
            e.Handled = true;
        }
    }

    private static string GestureKey(Key key)
    {
        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((char)('0' + (int)(key - Key.D0))).ToString();
        }
        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return ((char)('0' + (int)(key - Key.NumPad0))).ToString();
        }
        return key switch
        {
            Key.Space => "space",
            Key.Escape => "escape",
            _ => key.ToString().ToLowerInvariant(),
        };
    }

    private static string BuildGesture(KeyModifiers modifiers, string key)
    {
        var builder = new StringBuilder();
        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            builder.Append("ctrl+");
        }
        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            builder.Append("alt+");
        }
        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            builder.Append("shift+");
        }
        if (modifiers.HasFlag(KeyModifiers.Meta))
        {
            builder.Append("meta+");
        }
        return builder.Append(key).ToString();
    }
}
