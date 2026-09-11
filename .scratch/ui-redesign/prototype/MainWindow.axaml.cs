using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace Aria.Prototype;

public sealed record DragTrack(string Source, string Name, string Duration, StreamGeometry Wave);

public sealed record ScriptMention(string Name, bool Dangling);

public sealed record ScriptLineData(string Time, int Seconds, string Text, List<ScriptMention> Mentions, bool Current, bool Editing);

public sealed record KnownTrack(string Name, string Duration, string WaveKey);

public partial class MainWindow : Window
{
    private Border? _dragSource;
    private Point _pressPos;
    private bool _dragging;
    private Border? _hint;
    private int _scriptDemo;
    private List<ScriptLineData>? _scriptSeven;
    private List<ScriptLineData>? _scriptFifty;
    private readonly List<ScriptLineData> _scriptEmpty = [];
    private ScriptLineData? _suggestTarget;
    private const int ShowElapsedSeconds = 5025;

    private static readonly List<KnownTrack> KnownTracks =
    [
        new("Осенний дождь", "3:42", "wave_t1"),
        new("Night Drive", "4:15", "wave_t2"),
        new("Гул маяка", "2:58", "wave_t3"),
        new("Deep Current", "5:07", "wave_t4"),
        new("Полночь", "3:21", "wave_t5"),
        new("Slow Tide", "4:48", "wave_t6"),
        new("Стекло", "3:05", "wave_t7"),
        new("Amber Loop", "6:12", "wave_t8"),
        new("Тихий час", "2:34", "wave_t9"),
        new("Copper Sky", "4:56", "wave_t10"),
    ];

    public MainWindow()
    {
        InitializeComponent();
        UpdatePlayPauseTip(PlayPause.IsChecked == true);
        PlayPause.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty)
            {
                UpdatePlayPauseTip(PlayPause.IsChecked == true);
            }
        };
        Opened += (_, _) => SetWaveCursorFraction(0.4);
        AddHandler(KeyDownEvent, OnTunnelKey, RoutingStrategies.Tunnel);
        RenderScript();
    }

    private void OnTunnelKey(object? sender, KeyEventArgs e)
    {
        if (HelpOverlay.IsVisible)
        {
            e.Handled = true;
            if (e.Key is Key.F1 or Key.Escape)
            {
                CloseHelp();
            }
            return;
        }
        if (e.Key == Key.F1)
        {
            e.Handled = true;
            ShowHelp();
            return;
        }
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.T)
        {
            e.Handled = true;
            Drawer.IsPaneOpen = !Drawer.IsPaneOpen;
        }
    }

    private void UpdatePlayPauseTip(bool playing) =>
        ToolTip.SetTip(PlayPause, playing ? "Пауза \u2014 Esc" : "Играть \u2014 Space");

    private void OnHelpToggleClick(object? sender, RoutedEventArgs e) => ShowHelp();

    private void OnScenarioToggleClick(object? sender, RoutedEventArgs e) => Drawer.IsPaneOpen = !Drawer.IsPaneOpen;

    private void OnPaneCloseClick(object? sender, RoutedEventArgs e) => Drawer.IsPaneOpen = false;

    private void OnMuteClick(object? sender, RoutedEventArgs e)
    {
        bool muted = !MutedIcon.IsVisible;
        MutedIcon.IsVisible = muted;
        SoundIcon.IsVisible = !muted;
    }

    private void OnPlayPauseChanged(object? sender, RoutedEventArgs e) =>
        ToolTip.SetTip(PlayPause, PlayPause.IsChecked == true ? "Пауза \u2014 Esc" : "Играть \u2014 Space");

    private void OnHelpOverlayClick(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        CloseHelp();
    }

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e) => FocusSink.Focus();

    private void OnRowTap(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border row || row.Parent is not StackPanel stack)
        {
            return;
        }
        SelectRow(row, stack);
        TopLevel.GetTopLevel(this)?.FocusManager?.Focus(FocusSink, NavigationMethod.Unspecified, KeyModifiers.None);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragSource = row;
            _pressPos = e.GetPosition(this);
            _dragging = false;
            e.Pointer.Capture(row);
        }
        e.Handled = true;
    }

    private void OnRowMove(object? sender, PointerEventArgs e)
    {
        if (_dragSource is null || sender != _dragSource)
        {
            return;
        }
        var pos = e.GetPosition(this);
        if (!_dragging && (Math.Abs(pos.X - _pressPos.X) > 6 || Math.Abs(pos.Y - _pressPos.Y) > 6))
        {
            _dragging = true;
            _dragSource.Opacity = 0.45;
        }
        if (!_dragging)
        {
            return;
        }
        RemoveHint();
        var p = e.GetPosition(PlaylistStack);
        var q = e.GetPosition(QueuePanel);
        var overQueue = q.X >= 0 && q.Y >= 0 && q.X <= QueuePanel.Bounds.Width && q.Y <= QueuePanel.Bounds.Height;
        var overPlaylist = p.X >= 0 && p.Y >= 0 && p.X <= PlaylistStack.Bounds.Width && p.Y <= PlaylistStack.Bounds.Height;
        if (overQueue)
        {
            QueuePanel.Classes.Add("dropTarget");
        }
        else if (overPlaylist)
        {
            _hint = new Border
            {
                Classes = { "dropHint" },
                Height = 4,
                CornerRadius = new Avalonia.CornerRadius(2),
                Background = (IBrush)Application.Current!.Resources["BrushFg"]!,
                Opacity = 0.9,
                Margin = new Avalonia.Thickness(0, -2),
            };
            PlaylistStack.Children.Insert(Math.Clamp(DropIndex(p), 0, PlaylistStack.Children.Count), _hint);
        }
    }

    private void RemoveHint()
    {
        QueuePanel.Classes.Remove("dropTarget");
        if (_hint is { } hint && PlaylistStack.Children.Contains(hint))
        {
            PlaylistStack.Children.Remove(hint);
        }
        _hint = null;
    }

    private void OnRowRelease(object? sender, PointerReleasedEventArgs e)
    {
        var source = _dragSource;
        _dragSource = null;
        if (source is null)
        {
            return;
        }
        if (_dragging)
        {
            source.Opacity = 1;
        }
        _dragging = false;
        RemoveHint();
        var pos = e.GetPosition(this);
        var moved = Math.Abs(pos.X - _pressPos.X) > 6 || Math.Abs(pos.Y - _pressPos.Y) > 6;
        if (!moved)
        {
            return;
        }
        var q = e.GetPosition(QueuePanel);
        if (q.X >= 0 && q.Y >= 0 && q.X <= QueuePanel.Bounds.Width && q.Y <= QueuePanel.Bounds.Height)
        {
            var track = ExtractTrack(source);
            if (track is not null)
            {
                QueueStack.Children.Add(BuildQueueItem(track));
                StatusText.Text = "\u2192 \u043e\u0447\u0435\u0440\u0435\u0434\u044c: " + track.Name;
            }
            if (source.Classes.Contains("plRow") && source.Parent is StackPanel origin)
            {
                origin.Children.Remove(source);
                RenumberPlaylist();
            }
            e.Handled = true;
            return;
        }
        var p = e.GetPosition(PlaylistStack);
        if (p.X >= 0 && p.Y >= 0 && p.X <= PlaylistStack.Bounds.Width && p.Y <= PlaylistStack.Bounds.Height)
        {
            var index = Math.Clamp(DropIndex(p), 0, PlaylistStack.Children.Count);
            if (source.Classes.Contains("plRow") && source.Parent is StackPanel origin && origin == PlaylistStack)
            {
                var old = PlaylistStack.Children.IndexOf(source);
                PlaylistStack.Children.Remove(source);
                if (old < index)
                {
                    index--;
                }
                PlaylistStack.Children.Insert(Math.Clamp(index, 0, PlaylistStack.Children.Count), source);
                RenumberPlaylist();
                StatusText.Text = "\u2192 \u043f\u043e\u0437\u0438\u0446\u0438\u044f " + (index + 1) + ": " + ExtractTrack(source).Name;
            }
            else
            {
                var track = ExtractTrack(source);
                if (track is not null)
                {
                    PlaylistStack.Children.Insert(index, BuildPlaylistRow(track));
                    RenumberPlaylist();
                    StatusText.Text = "+ в плейлист позиция " + (index + 1) + ": " + track.Name;
                }
            }
        }
        e.Handled = true;
    }

    private static void SelectRow(Border row, StackPanel stack)
    {
        foreach (var child in stack.Children)
        {
            if (child is Border sibling)
            {
                sibling.Classes.Remove("selected");
            }
        }
        row.Classes.Add("selected");
    }

    private static DragTrack ExtractTrack(Border row)
    {
        if (row.Child is not Grid grid)
        {
            throw new InvalidOperationException("row layout");
        }
        var name = grid.Children.OfType<TextBlock>().First(t => Grid.GetColumn(t) == 1).Text ?? "";
        var duration = grid.Children.OfType<TextBlock>().Where(t => Grid.GetColumn(t) == 2)
            .Select(t => t.Text).FirstOrDefault(t => !string.IsNullOrEmpty(t)) ?? "";
        var wave = grid.Children.OfType<Path>().First().Data as StreamGeometry ?? new StreamGeometry();
        return new DragTrack(row.Classes.Contains("plRow") ? "pl" : "lib", name, duration, wave);
    }

    private void OnClearQueueClick(object? sender, RoutedEventArgs e)
    {
        while (QueueStack.Children.Count > 1)
        {
            QueueStack.Children.RemoveAt(QueueStack.Children.Count - 1);
        }
    }

    private int DropIndex(Point point)
    {
        int index = 0;
        foreach (var child in PlaylistStack.Children)
        {
            if (child is Border { Classes: var c } && c.Contains("dropHint"))
            {
                continue;
            }
            var center = child.Bounds.Y + child.Bounds.Height / 2;
            if (point.Y > center)
            {
                index++;
            }
        }
        return index;
    }

    private Border BuildPlaylistRow(DragTrack track)
    {
        var number = new TextBlock
        {
            Text = "00",
            FontFamily = (FontFamily)Application.Current!.Resources["MonoFont"]!,
            FontSize = 11,
            Foreground = (IBrush)Application.Current!.Resources["BrushDim"]!,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        var name = new TextBlock
        {
            Text = track.Name,
            FontSize = 13,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(8, 0, 0, 0),
        };
        var duration = new TextBlock
        {
            Text = track.Duration,
            FontFamily = (FontFamily)Application.Current!.Resources["MonoFont"]!,
            FontSize = 12,
            Foreground = (IBrush)Application.Current!.Resources["BrushDim"]!,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
        };
        var wave = new Path
        {
            Data = track.Wave,
            Stretch = Stretch.Fill,
            Width = 118,
            Height = 18,
            Fill = (IBrush)Application.Current!.Resources["BrushWave"]!,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(12, 0, 0, 0),
        };
        Grid.SetColumn(number, 0);
        Grid.SetColumn(name, 1);
        Grid.SetColumn(duration, 2);
        Grid.SetColumn(wave, 3);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*,58,130,30") };
        grid.Children.Add(number);
        grid.Children.Add(name);
        grid.Children.Add(duration);
        grid.Children.Add(wave);
        var border = new Border
        {
            Child = grid,
            Classes = { "plRow" },
            Height = 44,
            CornerRadius = new Avalonia.CornerRadius(6),
            Padding = new Avalonia.Thickness(10, 0, 12, 0),
            Background = Brushes.Transparent,
        };
        border.PointerPressed += OnRowTap;
        border.PointerMoved += OnRowMove;
        border.PointerReleased += OnRowRelease;
        return border;
    }

    private Border BuildQueueItem(DragTrack track)
    {
        var name = new TextBlock
        {
            Text = track.Name,
            FontSize = 12,
            Foreground = (IBrush)Application.Current!.Resources["BrushDim"]!,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(8, 0, 0, 0),
        };
        var duration = new TextBlock
        {
            Text = track.Duration,
            FontFamily = (FontFamily)Application.Current!.Resources["MonoFont"]!,
            FontSize = 10,
            Foreground = (IBrush)Application.Current!.Resources["BrushFaint"]!,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        Grid.SetColumn(name, 1);
        Grid.SetColumn(duration, 2);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("18,*,36") };
        grid.Children.Add(name);
        grid.Children.Add(duration);
        return new Border
        {
            Child = grid,
            Height = 36,
            CornerRadius = new Avalonia.CornerRadius(6),
            Padding = new Avalonia.Thickness(10, 0),
        };
    }

    private void RenumberPlaylist()
    {
        int i = 1;
        foreach (var child in PlaylistStack.Children.OfType<Border>())
        {
            if (child.Classes.Contains("dropHint"))
            {
                continue;
            }
            var grid = (Grid)child.Child!;
            var number = grid.Children.OfType<TextBlock>().First(t => Grid.GetColumn(t) == 0);
            number.Text = i.ToString("00");
            i++;
        }
    }

    private void OnScriptDemoClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string tag)
        {
            _scriptDemo = tag switch { "fifty" => 1, "empty" => 2, _ => 0 };
            RenderScript();
        }
    }

    private void RenderScript()
    {
        ScriptPopup.IsOpen = false;
        ScriptLinesHost.Children.Clear();
        var list = ActiveScriptList();
        MarkCurrent(list);
        if (_scriptDemo == 2 && list.Count == 0)
        {
            var app = Application.Current!.Resources;
            var title = new TextBlock
            {
                Text = "Сценарий пуст",
                FontSize = 14,
                Foreground = (IBrush)app["BrushFg"]!,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            };
            var hint = new TextBlock
            {
                Text = "Первая строка станет отсчётом шоу",
                FontSize = 12,
                Foreground = (IBrush)app["BrushDim"]!,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            };
            var add = new Button
            {
                Classes = { "ghost" },
                Height = 32,
                Padding = new Avalonia.Thickness(14, 0),
                FontSize = 12,
                Content = "+ Добавить строку",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            };
            add.Click += OnEmptyAddClick;
            var empty = new StackPanel { Spacing = 8, Margin = new Avalonia.Thickness(0, 48, 0, 0) };
            empty.Children.Add(title);
            empty.Children.Add(hint);
            empty.Children.Add(add);
            ScriptLinesHost.Children.Add(empty);
            return;
        }
        foreach (var line in list)
        {
            ScriptLinesHost.Children.Add(BuildScriptLine(line));
        }
    }

    private List<ScriptLineData> ActiveScriptList() => _scriptDemo switch
    {
        1 => _scriptFifty ??= BuildFiftyLines(),
        2 => _scriptEmpty,
        _ => _scriptSeven ??= BuildSevenLines(),
    };

    private static List<ScriptLineData> BuildSevenLines() =>
    [
        new("00:00", 0, "Открытие: тихий вход, свет 30%", [], false, false),
        new("03:41", 221, "Дать первый трек, подвести гул", [new("Осенний дождь", false)], false, false),
        new("12:05", 725, "Смена настроения, два трека подряд", [new("Night Drive", false), new("Deep Current", false)], false, false),
        new("58:20", 3500, "Разговор с залом, фоном спокойное", [new("Тихий час", false)], false, false),
        new("1:22:10", 4930, "Сейчас играем — медленный финал блока", [new("Slow Tide", false)], false, false),
        new("1:24:00", 5040, "Новый трек из ноутбука, имя уточняю", [new("", true)], false, false),
        new("1:27:00", 5220, "Свет вверх, тишина, поклон", [], false, false),
        new("—", int.MaxValue, "", [], false, true),
    ];

    private static List<ScriptLineData> BuildFiftyLines()
    {
        string[] texts =
        [
            "Реплика {0}: свет держим",
            "Переход, подвести фон",
            "Пауза зала, тишина",
            "Финал блока, аплодисменты",
            "Смена плана, движение",
            "Тёмная сцена, шёпот",
        ];
        var names = KnownTracks.Select(t => t.Name).ToArray();
        var list = new List<ScriptLineData>();
        for (int i = 0; i < 50; i++)
        {
            int seconds = i * 105;
            var mentions = new List<ScriptMention>();
            if (i == 17)
            {
                mentions.Add(new ScriptMention("", true));
            }
            else if (i % 7 == 3)
            {
                mentions.Add(new ScriptMention(names[i % 10], false));
                mentions.Add(new ScriptMention(names[(i + 3) % 10], false));
            }
            else if (i % 4 == 1)
            {
                mentions.Add(new ScriptMention(names[i % 10], false));
            }
            list.Add(new ScriptLineData(ScriptTime(seconds), seconds, string.Format(texts[i % texts.Length], i + 1), mentions, false, false));
        }
        return list;
    }

    private static string ScriptTime(int seconds) =>
        seconds >= 3600 ? $"{seconds / 3600}:{(seconds % 3600) / 60:00}:{seconds % 60:00}"
        : seconds >= 60 ? $"{seconds / 60}:{seconds % 60:00}"
        : $"00:{seconds:00}";

    private static void MarkCurrent(List<ScriptLineData> list)
    {
        ScriptLineData? current = null;
        foreach (var line in list)
        {
            if (line.Seconds <= ShowElapsedSeconds)
            {
                current = line;
            }
        }
        for (int i = 0; i < list.Count; i++)
        {
            list[i] = list[i] with { Current = list[i] == current };
        }
    }

    private static string TrackDuration(string name) =>
        KnownTracks.FirstOrDefault(t => t.Name == name)?.Duration ?? "";

    private Border BuildScriptLine(ScriptLineData data)
    {
        var app = Application.Current!.Resources;
        var time = new TextBlock
        {
            Text = data.Time,
            FontFamily = (FontFamily)app["MonoFont"]!,
            FontSize = 11,
            Foreground = (IBrush)app[data.Current ? "BrushFg" : "BrushDim"]!,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Avalonia.Thickness(0, 1, 0, 0),
        };
        var go = new PathIcon
        {
            Classes = { "goHint" },
            Data = (StreamGeometry)app["icon_play"]!,
            Width = 11,
            Height = 11,
            Foreground = (IBrush)app["BrushDim"]!,
            IsVisible = false,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            Margin = new Avalonia.Thickness(1, 5, 0, 0),
        };
        var left = new StackPanel();
        left.Children.Add(time);
        left.Children.Add(go);
        Grid.SetColumn(left, 0);
        var content = new StackPanel { Spacing = 6 };
        Grid.SetColumn(content, 1);
        if (data.Editing)
        {
            content.Children.Add(new TextBox
            {
                Name = "ScriptEditBox",
                Text = data.Text,
                PlaceholderText = "Текст строки…",
                FontSize = 12,
                Background = (IBrush)app["BrushSurface"]!,
                BorderBrush = (IBrush)app["BrushLine"]!,
            });
        }
        else if (!string.IsNullOrEmpty(data.Text))
        {
            content.Children.Add(new TextBlock { Text = data.Text, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        }
        if (data.Mentions.Count > 0 || data.Editing)
        {
            var chips = new WrapPanel();
            foreach (var mention in data.Mentions)
            {
                chips.Children.Add(BuildMentionChip(mention));
            }
            content.Children.Add(chips);
        }
        if (data.Editing)
        {
            var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
            var at = new Button
            {
                Classes = { "ghost" },
                Height = 26,
                Padding = new Avalonia.Thickness(10, 0),
                FontSize = 12,
                Content = "@",
                Tag = data,
            };
            ToolTip.SetTip(at, "Упомянуть трек");
            at.Click += OnMentionSuggestClick;
            var mark = new Button
            {
                Classes = { "ghost" },
                Height = 26,
                Padding = new Avalonia.Thickness(10, 0),
                FontSize = 11,
                Content = "пометить сейчас",
                Tag = data,
            };
            ToolTip.SetTip(mark, "Время строки = elapsed шоу");
            mark.Click += OnMarkTimeClick;
            buttons.Children.Add(at);
            buttons.Children.Add(mark);
            content.Children.Add(buttons);
        }
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("58,*") };
        grid.Children.Add(left);
        grid.Children.Add(content);
        var border = new Border
        {
            Child = grid,
            Classes = { "scriptLine" },
            Background = Brushes.Transparent,
            CornerRadius = new Avalonia.CornerRadius(6),
            Padding = new Avalonia.Thickness(10, 8),
            Tag = data,
        };
        if (data.Current)
        {
            border.Classes.Add("current");
            border.Padding = new Avalonia.Thickness(7, 8, 10, 8);
        }
        if (data.Editing)
        {
            border.Classes.Add("editing");
        }
        border.PointerPressed += OnScriptLineTap;
        return border;
    }

    private Border BuildMentionChip(ScriptMention mention)
    {
        var app = Application.Current!.Resources;
        var label = new TextBlock { FontSize = 11 };
        var chip = new Border
        {
            Classes = { "mention" },
            CornerRadius = new Avalonia.CornerRadius(9),
            Padding = new Avalonia.Thickness(9, 3),
            Margin = new Avalonia.Thickness(0, 0, 6, 0),
            Tag = mention,
        };
        if (mention.Dangling)
        {
            chip.Background = (IBrush)app["BrushSurface"]!;
            chip.BorderBrush = (IBrush)app["BrushLine"]!;
            chip.BorderThickness = new Avalonia.Thickness(1);
            label.Text = "неизвестный трек";
            label.FontStyle = FontStyle.Italic;
            label.Foreground = (IBrush)app["BrushDim"]!;
            ToolTip.SetTip(chip, "Трек удалён из библиотеки");
        }
        else
        {
            chip.Background = (IBrush)app["BrushSurfaceHi"]!;
            label.Text = mention.Name;
            label.Foreground = (IBrush)app["BrushFg"]!;
            ToolTip.SetTip(chip, mention.Name + " · " + TrackDuration(mention.Name));
        }
        chip.Child = label;
        chip.PointerPressed += OnMentionTap;
        return chip;
    }

    private void OnScriptLineTap(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border row || row.Tag is not ScriptLineData data)
        {
            return;
        }
        var live = data.Mentions.Where(m => !m.Dangling).Select(m => m.Name).Distinct().ToList();
        if (live.Count == 1)
        {
            EnqueueScriptTrack(live[0]);
        }
        else if (live.Count > 1)
        {
            OpenPopup(row, live.Select(name => (name, TrackDuration(name))).ToList(), OnChoicePick);
        }
        else if (data.Mentions.Any(m => m.Dangling))
        {
            StatusText.Text = "повисшее упоминание — трек удалён";
        }
        else
        {
            StatusText.Text = "заметка — не исполняется";
        }
        TopLevel.GetTopLevel(this)?.FocusManager?.Focus(FocusSink, NavigationMethod.Unspecified, KeyModifiers.None);
        e.Handled = true;
    }

    private void OnMentionTap(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border chip && chip.Tag is ScriptMention mention)
        {
            if (mention.Dangling)
            {
                StatusText.Text = "повисшее упоминание — трек удалён";
            }
            else
            {
                EnqueueScriptTrack(mention.Name);
            }
        }
        e.Handled = true;
    }

    private void EnqueueScriptTrack(string name)
    {
        var known = KnownTracks.FirstOrDefault(t => t.Name == name);
        if (known is null)
        {
            StatusText.Text = "повисшее упоминание — трек удалён";
            return;
        }
        var wave = Application.Current!.Resources[known.WaveKey] as StreamGeometry ?? new StreamGeometry();
        QueueStack.Children.Add(BuildQueueItem(new DragTrack("script", known.Name, known.Duration, wave)));
        StatusText.Text = "→ очередь: " + known.Name;
    }

    private void OpenPopup(Control anchor, List<(string Name, string Duration)> items, EventHandler<RoutedEventArgs> pick)
    {
        ScriptPopupList.Children.Clear();
        foreach (var (name, duration) in items)
        {
            var label = new TextBlock
            {
                Text = name,
                FontSize = 12,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            var sub = new TextBlock
            {
                Text = duration,
                FontFamily = (FontFamily)Application.Current!.Resources["MonoFont"]!,
                FontSize = 11,
                Foreground = (IBrush)Application.Current!.Resources["BrushDim"]!,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            var row = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            row.Children.Add(label);
            if (!string.IsNullOrEmpty(duration))
            {
                row.Children.Add(sub);
            }
            var button = new Button
            {
                Classes = { "ghost" },
                Height = 30,
                Padding = new Avalonia.Thickness(10, 0),
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                Content = row,
                Tag = name,
            };
            button.Click += pick;
            ScriptPopupList.Children.Add(button);
        }
        ScriptPopup.PlacementTarget = anchor;
        ScriptPopup.IsOpen = true;
    }

    private void OnChoicePick(object? sender, RoutedEventArgs e)
    {
        ScriptPopup.IsOpen = false;
        if (sender is Button button && button.Tag is string name)
        {
            EnqueueScriptTrack(name);
        }
    }

    private void OnMentionSuggestClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button at && at.Tag is ScriptLineData data)
        {
            _suggestTarget = data;
            OpenPopup(at, KnownTracks.Take(5).Select(t => (t.Name, t.Duration)).ToList(), OnSuggestPick);
        }
    }

    private void OnSuggestPick(object? sender, RoutedEventArgs e)
    {
        ScriptPopup.IsOpen = false;
        if (sender is Button button && button.Tag is string name && _suggestTarget is { } target)
        {
            SyncEditText();
            if (!target.Mentions.Any(m => m.Name == name))
            {
                target.Mentions.Add(new ScriptMention(name, false));
            }
            RenderScript();
        }
    }

    private void OnMarkTimeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button mark && mark.Tag is ScriptLineData data)
        {
            SyncEditText();
            var list = ActiveScriptList();
            var index = list.IndexOf(data);
            if (index >= 0)
            {
                list[index] = data with { Time = "01:23:45", Seconds = ShowElapsedSeconds };
            }
            RenderScript();
            StatusText.Text = "время строки — 01:23:45 (elapsed шоу)";
        }
    }

    private void OnEmptyAddClick(object? sender, RoutedEventArgs e)
    {
        _scriptEmpty.Add(new ScriptLineData("—", int.MaxValue, "", [], false, true));
        RenderScript();
    }

    private void SyncEditText()
    {
        var box = this.FindControl<TextBox>("ScriptEditBox");
        if (box is null)
        {
            return;
        }
        var list = ActiveScriptList();
        var index = list.FindIndex(l => l.Editing);
        if (index >= 0)
        {
            list[index] = list[index] with { Text = box.Text ?? "" };
        }
    }

    private void OnWaveSeekPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetPosition(WaveSeek);
        var fraction = WaveSeek.Bounds.Width <= 0 ? 0 : Math.Clamp(point.X / WaveSeek.Bounds.Width, 0, 1);
        SetWaveCursorFraction(fraction);
        e.Handled = true;
    }

    private void SetWaveCursorFraction(double fraction) =>
        WaveCursor.RenderTransform = new TranslateTransform(fraction * WaveSeek.Bounds.Width - 1, 0);

    private void ShowHelp()
    {
        HelpOverlay.IsVisible = true;
        HelpOverlay.Focus();
    }

    private void CloseHelp() => HelpOverlay.IsVisible = false;
}
