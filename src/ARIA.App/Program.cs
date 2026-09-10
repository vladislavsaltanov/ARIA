namespace Aria.App;

using System.Collections.Immutable;
using Aria.App.Services;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Aria.Core.Commands;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Any(a => a == "--selftest"))
        {
            return RunSelfTestAsync().GetAwaiter().GetResult();
        }
        if (args.Any(a => a.StartsWith("--remote-")))
        {
            var dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ARIA");
            Directory.CreateDirectory(dataDirectory);
            return RemoteCredentialCli.Run(args, dataDirectory);
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    private static async Task<int> RunSelfTestAsync()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"aria-selftest-{Guid.NewGuid():N}");
        try
        {
            Console.WriteLine("aria selftest: starting host");
            await using var host = new AppHost(dataDirectory);
            await host.StartAsync();

            host.Submit(new CreatePlaylist("SelfTest"));
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (host.Bus.Snapshot().Show.Playlists.Length == 0)
            {
                if (DateTime.UtcNow > deadline)
                {
                    Console.Error.WriteLine("SELFTEST FAIL: playlist not applied");
                    return 1;
                }
                await Task.Delay(50);
            }
            Console.WriteLine($"aria selftest: bus ok, playlists={host.Bus.Snapshot().Show.Playlists.Length}");

            Console.WriteLine("aria selftest: playback smoke");
            var track = new Aria.Core.Model.Track(
                Aria.Core.Model.TrackId.New(),
                "/nonexistent/sine.flac",
                "smoke",
                TimeSpan.FromSeconds(10),
                new Aria.Core.Model.TrackDefaults());
            var playlist = new Aria.Core.Model.Playlist(
                Aria.Core.Model.PlaylistId.New(), "Selftest", ImmutableArray.Create(
                    new Aria.Core.Model.PlaylistEntry(Aria.Core.Model.EntryId.New(), track.Id)));
            host.Submit(new LoadShow([track], ImmutableArray.Create(playlist), playlist.Id));
            host.Submit(new Play());
            await Task.Delay(2000);
            var status = host.Bus.Snapshot().Transport.Status;
            Console.WriteLine($"aria selftest: transport={status} (faulted skip expected for missing file)");
            host.Submit(new Stop());

            if (host.Remote is { } remote)
            {
                Console.WriteLine($"aria selftest: remote at {remote.HttpEndpoint} (loopback)");
            }

            Console.WriteLine("aria selftest: autosave flush");
            await host.DisposeAsync();
            if (!File.Exists(Path.Combine(dataDirectory, "show.json")))
            {
                Console.Error.WriteLine("SELFTEST FAIL: show.json not written");
                return 1;
            }
            Console.WriteLine("aria selftest: OK");
            return 0;
        }
        catch (DllNotFoundException e)
        {
            Console.Error.WriteLine($"SELFTEST FAIL: native audio library missing: {e.Message}");
            return 1;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"SELFTEST FAIL: {e.GetType().Name}: {e.Message}");
            return 1;
        }
        finally
        {
            try
            {
                if (Directory.Exists(dataDirectory))
                {
                    Directory.Delete(dataDirectory, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}