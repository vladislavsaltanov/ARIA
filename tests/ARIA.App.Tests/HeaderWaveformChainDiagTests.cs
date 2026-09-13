namespace Aria.App.Tests;

using Aria.Audio;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;

[Collection("headless")]
public sealed class HeaderWaveformChainDiagTests : IDisposable
{
    private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"aria-hdiag-{Guid.NewGuid():N}");
    private readonly HeadlessUnitTestSession _session = HeadlessUnitTestSession.StartNew(typeof(App));

    public void Dispose()
    {
        _session.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task PlayingTrackId_MatchesStoredPeaks_AndHeaderDraws()
    {
        Directory.CreateDirectory(_directory);
        var wav = TestWav.Write(_directory, "diag.wav", seconds: 3);
        await using var host = new AppHost(
            System.IO.Path.Combine(_directory, "data"),
            null,
            () => new NullSink(AppHost.SampleRate, AppHost.Channels),
            () => new MiniaudioSourceFactory(AppHost.SampleRate, AppHost.Channels));
        await host.StartAsync();
        var report = await host.ImportTracksAsync([wav]);
        Assert.Equal(1, report.Added);
        var stored = Assert.Single(host.Library!.Load().Tracks);
        Assert.NotNull(host.Waveforms!.Load(stored.Id));

        host.Submit(new EnqueueTrack(stored.Id));
        await PollAsync(() => host.Bus.Snapshot().Queue.Items.Length == 1);
        host.Submit(new Play());
        var samples = new List<string>();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        string? seen = null;
        while (DateTime.UtcNow < deadline)
        {
            var snap = host.Bus.Snapshot();
            samples.Add($"{snap.Transport.Status}/{snap.Transport.Current?.TrackId}/{snap.Queue.Items.Length}/{snap.Transport.Faulted.Length}");
            if (snap.Transport.Current is not null)
            {
                seen = snap.Transport.Current.TrackId.Value.ToString();
                break;
            }
            await Task.Delay(5);
        }
        if (seen is null)
        {
            throw new TimeoutException("play trace: " + string.Join(" ", samples.Take(40)));
        }
        var current = host.Bus.Snapshot().Transport.Current!;
        Assert.Equal(stored.Id, current.TrackId);
        Assert.NotNull(host.Waveforms!.Load(current.TrackId));

        await _session.Dispatch(() =>
        {
            var header = new Views.PlaybackHeader();
            var window = new Window { Width = 700, Height = 300, Content = header };
            window.Show();
            header.Attach(host.Monitor, host.Waveforms);
            var handle = new StreamHandle(7);
            host.Monitor.Bind(handle, current);
            host.Monitor.Publish(handle, TimeSpan.FromSeconds(1));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var canvas = header.FindControl<Canvas>("WaveformCanvas");
            Assert.NotNull(canvas);
            var paths = canvas.Children.OfType<Path>().ToList();
            Assert.Equal(2, paths.Count);
            foreach (var path in paths)
            {
                Assert.Equal(0.6, path.Opacity);
                var geometry = Assert.IsAssignableFrom<Geometry>(path.Data);
                Assert.True(geometry.Bounds.Width > 0 && geometry.Bounds.Height > 0);
            }
            window.Close();
            return 0;
        }, CancellationToken.None);
    }

    private static async Task PollAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }
            await Task.Delay(25);
        }
        throw new TimeoutException("condition not met");
    }

    private sealed class NullSourceFactory : ISourceFactory
    {
        public ISampleSource? Open(string filePath, TimeSpan cueIn, TimeSpan? cueOut) => null;
    }
}
