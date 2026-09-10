namespace Aria.App;

using System.Net;
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
    private static RemoteCredentialsStore? _credentials;

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var dataDirectory = DefaultDataDirectory();
            _credentials = new RemoteCredentialsStore(Path.Combine(dataDirectory, "remote-auth.json"));
            var credentials = _credentials.Load();
            var remoteOptions = new RemoteOptions(
                credentials.Password,
                BindAddress: IPAddress.Any,
                Credentials: _credentials);
            var host = new AppHost(dataDirectory, remoteOptions);
            Host = host;

            desktop.ShutdownRequested += (_, _) =>
            {
                _announcer?.Dispose();
                host.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
            };
            _ = ComposeAsync(desktop, host, remoteOptions, dataDirectory, credentials);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static async Task ComposeAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        AppHost host,
        RemoteOptions remoteOptions,
        string dataDirectory,
        RemoteCredentials credentials)
    {
        await host.StartAsync();
        await Dispatcher.UIThread.InvokeAsync(() => Compose(desktop, host, dataDirectory, credentials));
    }

    private static void Compose(
        IClassicDesktopStyleApplicationLifetime desktop,
        AppHost host,
        string dataDirectory,
        RemoteCredentials credentials)
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

        if (host.Remote is { } remoteHost && _credentials is { } store)
        {
            var announcer = new RemoteAnnouncer(remoteHost.HttpEndpoint, () => store.Load());
            _announcer = announcer;
            var info = announcer.Announce();
            remote.Configure(
                info,
                RemoteQr.ToBitmap(info),
                store.Load(),
                () =>
                {
                    store.ResetPassword();
                    var fresh = announcer.Announce();
                    return (fresh, RemoteQr.ToBitmap(fresh), store.Load());
                });
            remote.SetMdns(announcer.Start());
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

    private static string DefaultDataDirectory()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ARIA");
        Directory.CreateDirectory(directory);
        return directory;
    }
}