namespace Aria.App.Views;

public static class HotkeyLabels
{
    public static string Label(string action) => action switch
    {
        "play" => "Воспроизведение",
        "pause" => "Пауза",
        "panic" => "PANIC",
        "next" => "Следующий трек",
        "replay" => "Повторить трек",
        "lock" => "Заблокировать управление",
        "toggle-script" => "Панель сценария",
        "reset-clock" => "Сбросить show clock",
        "settings" => "Настройки",
        _ => action,
    };

    public static string Tip(string label, string gesture) =>
        string.IsNullOrEmpty(gesture) ? label : $"{label} — {gesture}";
}
