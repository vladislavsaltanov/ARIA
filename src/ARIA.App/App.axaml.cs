namespace Aria.App;

using System.Net;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Aria.App.Services;
using Aria.App.ViewModels;
using Aria.App.Views;
using Aria.Core.Commands;
using Aria.Remote;

public partial class App : Application
{
    public const int RemoteDefaultPort = 48713;

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
                Credentials: _credentials,
                Port: RemoteDefaultPort);
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
        var thumbs = new WaveformThumbs(host.Waveforms!);
        var settingsStore = host.SettingsStore;
        var rowSettings = settingsStore.Load();
        var transport = new TransportViewModel(host.Bus, host.Monitor, sync, host.Meters, () => host.Tracks, rowSettings);
        ScriptPanelViewModel? scripts = null;
        var projects = new ProjectsViewModel(host.Bus, () => host.Tracks, thumbs, rowSettings, sync, topLevel: () => desktop.MainWindow, audioImport: host.ImportTracksAsync, trackAudio: host.GetTrackAudio, scriptExporter: pid => scripts!.ExportProjectScripts(pid));
        projects.TrackRelink = (id, path) => host.RelinkTrackAsync(id, path);
        var queue = new QueueViewModel(host.Bus, sync);
        var remote = new RemotePanelViewModel(sync);
        scripts = new ScriptPanelViewModel(host.Bus, () => host.Tracks, sync, topLevel: () => desktop.MainWindow, projectDirSource: projects.GetProjectDirectory);
        MainWindow? window = null;
        var hotkeys = new HotkeyService(
            HotkeyConfig.Load(Path.Combine(dataDirectory, "hotkeys.json")),
            action =>
            {
                if (window is not null)
                {
                    DispatchHotkey(window, host, action);
                }
            });
        var settings = new SettingsViewModel(
            host.Bus,
            hotkeys,
            Path.Combine(dataDirectory, "hotkeys.json"),
            settingsStore,
            updated =>
            {
                projects.UpdateRowSettings(updated);
                transport.UpdateRowSettings(updated);
            },
            sync,
            host.Outputs,
            host.LogPath,
            updated =>
            {
                host.SetLogEnabled(updated.LogEnabled);
                host.SetLogLevel(updated.LogLevel);
            });
        window = new MainWindow(hotkeys, projects, queue, () => new SettingsDialog(settings, remote), scripts) { DataContext = transport };
        desktop.MainWindow = window;
        host.LibrarySaveFailed += error =>
        {
            var owner = desktop.MainWindow;
            if (owner is null)
            {
                return;
            }
            if (sync is null)
            {
                _ = ShowLibraryErrorDialog(owner, error.Message);
                return;
            }
            sync.Post(_ => _ = ShowLibraryErrorDialog(owner, error.Message), null);
        };
        window.Show();

        window.AttachPlaybackHeader(host.Monitor, host.Waveforms);

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

    private static long _hotkeySeq;

    private static void DispatchHotkey(MainWindow window, AppHost host, string action)
    {
        if (action == "toggle-script")
        {
            window.ToggleScriptPane();
            return;
        }
        var command = HotkeyCommands.ToCommand(action, host.Bus.Snapshot().Show.Locked);
        if (command is not null)
        {
            host.Bus.Submit(new ClientId("desktop-hotkeys"), Interlocked.Increment(ref _hotkeySeq), command);
        }
    }

    private static async Task ShowLibraryErrorDialog(Avalonia.Controls.Window owner, string detail)
    {
        var ok = new Avalonia.Controls.Button { Content = "Понятно", Margin = new Avalonia.Thickness(0, 12, 0, 0) };
        var dialog = new Avalonia.Controls.Window
        {
            Title = "Библиотека не сохраняется",
            Width = 420,
            SizeToContent = Avalonia.Controls.SizeToContent.Height,
            CanResize = false,
            Content = new Avalonia.Controls.StackPanel
            {
                Margin = new Avalonia.Thickness(16),
                Children =
                {
                    new Avalonia.Controls.TextBlock { Text = "Не удалось записать библиотеку: " + detail, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    ok,
                },
            },
        };
        ok.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(owner);
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