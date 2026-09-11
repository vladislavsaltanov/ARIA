namespace Aria.App.Views;

using System.Text;
using Aria.App.Services;
using Aria.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

public partial class MainWindow : Window
{
    private readonly HotkeyService? _hotkeys;

    public MainWindow() : this(null)
    {
    }

    public MainWindow(HotkeyService? hotkeys, PlaylistsViewModel? playlistsViewModel = null, RemotePanelViewModel? remoteViewModel = null, LibraryViewModel? libraryViewModel = null)
    {
        InitializeComponent();
        _hotkeys = hotkeys;
        if (playlistsViewModel is not null)
        {
            ShowTab.DataContext = playlistsViewModel;
        }
        if (remoteViewModel is not null)
        {
            RemoteTab.DataContext = remoteViewModel;
        }
        if (libraryViewModel is not null)
        {
            LibraryTab.DataContext = libraryViewModel;
        }
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        if (_hotkeys is not null)
        {
            TransportBar.ApplyGestures(_hotkeys);
            BuildHotkeyTable();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (HelpOverlay.IsVisible)
        {
            HelpOverlay.IsVisible = false;
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F1)
        {
            HelpOverlay.IsVisible = true;
            e.Handled = true;
            return;
        }
        if (_hotkeys is null)
        {
            return;
        }
        if (_hotkeys.TryHandle(BuildGesture(e.KeyModifiers, GestureKey(e.Key))))
        {
            e.Handled = true;
        }
    }

    private void OnHelpOverlayClick(object? sender, PointerPressedEventArgs e) => HelpOverlay.IsVisible = false;

    private void BuildHotkeyTable()
    {
        if (_hotkeys is null)
        {
            return;
        }
        string[] actions = ["play", "pause", "panic", "next", "replay", "lock", "toggle-script", "reset-clock"];
        for (var row = 0; row < actions.Length; row++)
        {
            var action = new TextBlock
            {
                Text = HotkeyLabels.Label(actions[row]),
                FontSize = 13,
                Foreground = Avalonia.Media.Brushes.Gainsboro,
                Margin = new Avalonia.Thickness(0, 9, 0, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            Grid.SetRow(action, row + 1);
            var gesture = new TextBlock
            {
                Text = _hotkeys.GestureFor(actions[row]),
                FontFamily = new FontFamily("Consolas, Menlo"),
                FontSize = 12,
                Foreground = Avalonia.Media.Brushes.DimGray,
                TextAlignment = TextAlignment.Right,
                Margin = new Avalonia.Thickness(0, 9, 0, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            Grid.SetRow(gesture, row + 1);
            Grid.SetColumn(gesture, 1);
            HotkeyTable.Children.Add(action);
            HotkeyTable.Children.Add(gesture);
        }
        for (var row = 0; row <= actions.Length; row++)
        {
            HotkeyTable.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
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
