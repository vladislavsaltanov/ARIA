namespace Aria.App.Views.SettingsSections;

using Avalonia.Controls;

public sealed record SettingsSectionDescriptor(string Key, string Title, Func<Control> Create);

public static class SettingsSectionRegistry
{
    public static IReadOnlyList<SettingsSectionDescriptor> All { get; } =
    [
        new("remote", "Пульт", () => new RemotePanel()),
        new("hotkeys", "Жесты", () => new HotkeysSection()),
        new("clock", "Часы", () => new ClockSection()),
        new("rowformat", "Формат строк", () => new RowFormatSection()),
        new("playback", "Воспроизведение", () => new PlaybackSection()),
        new("audio", "Звук", () => new AudioSection()),
        new("engine", "Движок", () => new EngineSection()),
        new("log", "Журнал", () => new LogSection()),
    ];
}
