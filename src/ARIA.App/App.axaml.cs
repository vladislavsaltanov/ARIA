namespace Aria.App;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Aria.App.ViewModels;
using Aria.App.Views;

public partial class App : Application
{
    public static AppHost? Host { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var dataDirectory = DefaultDataDirectory();
            var host = new AppHost(dataDirectory);
            Host = host;

            desktop.ShutdownRequested += (_, _) => host.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
            _ = host.StartAsync();

            var viewModel = new TransportViewModel(host.Bus, host.Monitor, SynchronizationContext.Current);
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static string DefaultDataDirectory()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ARIA");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
