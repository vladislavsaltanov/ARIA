using Avalonia;

namespace Aria.Prototype;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--selftest")
        {
            SelfTest();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();

    private static void SelfTest()
    {
        BuildAvaloniaApp().SetupWithoutStarting();
        var app = Application.Current ?? throw new InvalidOperationException("app missing");
        var window = new MainWindow();
        if (window.Content is null)
        {
            Console.Error.WriteLine("selftest failed: window content empty");
            Environment.Exit(3);
        }

        string[] keys =
        [
            "icon_play", "icon_pause", "icon_stop", "icon_next", "icon_lock", "icon_settings",
            "icon_plus", "icon_search", "icon_x", "icon_list", "icon_alert", "icon_music",
            "icon_volume", "icon_volume_off", "icon_script",
            "wave_t1", "wave_t2", "wave_t3", "wave_t4", "wave_t5", "wave_t6", "wave_t7",
            "wave_t8", "wave_t9", "wave_t10", "wave_main"
        ];
        int found = 0;
        foreach (var key in keys)
        {
            if (app.TryGetResource(key, null, out var value) && value is Avalonia.Media.StreamGeometry geometry)
            {
                if (geometry.Bounds.Width <= 0 || geometry.Bounds.Height <= 0)
                {
                    Console.Error.WriteLine($"selftest failed: empty geometry {key}");
                    Environment.Exit(2);
                }

                found++;
            }
            else
            {
                Console.Error.WriteLine($"selftest failed: missing geometry {key}");
                Environment.Exit(2);
            }
        }

        Console.WriteLine($"selftest ok: {found}/{keys.Length} geometries, window '{window.Title}' parsed");
    }
}
