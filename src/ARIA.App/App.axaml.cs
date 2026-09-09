namespace Aria.App;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;
using Aria.App.Services;
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
            var hotkeys = new HotkeyService(
                HotkeyConfig.Load(Path.Combine(dataDirectory, "hotkeys.json")),
                action => DispatchHotkey(viewModel, action));
            desktop.MainWindow = new MainWindow(hotkeys) { DataContext = viewModel };
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static void DispatchHotkey(TransportViewModel viewModel, string action)
    {
        switch (action)
        {
            case "play": Run(viewModel.PlayCommand); break;
            case "pause": Run(viewModel.PauseCommand); break;
            case "stop": Run(viewModel.StopCommand); break;
            case "next": Run(viewModel.NextCommand); break;
            case "replay": Run(viewModel.ReplayCommand); break;
            case "panic": Run(viewModel.PanicCommand); break;
            case "lock": Run(viewModel.ToggleLockCommand); break;
        }
    }

    private static void Run(IRelayCommand command)
    {
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
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
