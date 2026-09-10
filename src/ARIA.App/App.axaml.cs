namespace Aria.App;

using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Aria.App.Services;
using Aria.App.ViewModels;
using Aria.App.Views;
using Aria.Remote;

public partial class App : Application
{
    public static AppHost? Host { get; private set; }

    private static RemoteAnnouncer? _announcer;

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var dataDirectory = DefaultDataDirectory();
            var remoteOptions = new RemoteOptions(LoadOrCreateToken(dataDirectory));
            var host = new AppHost(dataDirectory, remoteOptions);
            Host = host;

            desktop.ShutdownRequested += (_, _) =>
            {
                _announcer?.Dispose();
                host.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
            };
            _ = ComposeAsync(desktop, host, remoteOptions, dataDirectory);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static async Task ComposeAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        AppHost host,
        RemoteOptions remoteOptions,
        string dataDirectory)
    {
        await host.StartAsync();
        await Dispatcher.UIThread.InvokeAsync(() => Compose(desktop, host, remoteOptions, dataDirectory));
    }

    private static void Compose(
        IClassicDesktopStyleApplicationLifetime desktop,
        AppHost host,
        RemoteOptions remoteOptions,
        string dataDirectory)
    {
        var sync = SynchronizationContext.Current;
        var transport = new TransportViewModel(host.Bus, host.Monitor, sync);
        var playlists = new PlaylistsViewModel(host.Bus, () => host.Library!.Load().Tracks);
        var library = new LibraryViewModel(host.Bus, host.Library!, host.Waveforms!, host.Importer!, () => desktop.MainWindow);
        var remote = new RemotePanelViewModel(sync);
        var hotkeys = new HotkeyService(
            HotkeyConfig.Load(Path.Combine(dataDirectory, "hotkeys.json")),
            action => DispatchHotkey(transport, action));
        var window = new MainWindow(hotkeys, playlists, remote, library) { DataContext = transport };
        desktop.MainWindow = window;
        window.Show();

        if (host.Remote is { } remoteHost)
        {
            _announcer = new RemoteAnnouncer(remoteHost.HttpEndpoint, remoteOptions.AuthToken);
            remote.Init(_announcer.Announce());
            remote.SetMdns(_announcer.Start());
        }
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

    private static string LoadOrCreateToken(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "remote-token.txt");
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Trim();
            if (existing.Length > 0)
            {
                return existing;
            }
        }
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        File.WriteAllText(path, token);
        return token;
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