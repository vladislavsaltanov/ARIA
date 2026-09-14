namespace Aria.App.Tests;

using System.Collections.Immutable;
using Aria.App.ViewModels;
using Aria.App.Views;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.Runtime;
using Aria.Core.State;
using Aria.Persistence;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;

[Collection("headless")]
public sealed class PlaybackHeaderPumpTests
{
    private readonly HeadlessUnitTestSession _session;

    public PlaybackHeaderPumpTests(HeadlessSessionFixture fixture)
    {
        _session = fixture.Session;
    }

    [Fact]
    public async Task PumpedBus_OffThreadTrackChange_DrawsWaveform()
    {
        await _session.Dispatch(() =>
        {
            var track = new Track(TrackId.New(), "/audio/wire.wav", "wire", TimeSpan.FromSeconds(90), new TrackDefaults());
            var entry = new PlaylistEntry(EntryId.New(), track.Id);
            var playlist = new Playlist(PlaylistId.New(), "Main", [entry]);
            using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Pumped);
            bus.Submit(new ClientId("setup"), 1, new RestoreShow(
                [track], [playlist], playlist.Id, [], 0, TimeSpan.FromMilliseconds(100),
                TimeSpan.Zero, false, []));
            bus.Submit(new ClientId("setup"), 2, new MergeTracks([track]));
            using var viewModel = new TransportViewModel(bus);
            var header = new PlaybackHeader { DataContext = viewModel };
            var window = new Window { Width = 700, Height = 300, Content = header };
            window.Show();

            var store = new PumpMemoryStore();
            var points = ImmutableArray.CreateBuilder<PeakPoint>();
            for (var i = 0; i < 500; i++)
            {
                points.Add(new PeakPoint(-0.5f, 0.5f));
            }
            store.Save(new WaveformPeaks(track.Id, 25, 48000, points.ToImmutable()));
            var monitor = new PlaybackMonitor();
            header.Attach(monitor, store);
            window.UpdateLayout();

            bus.Submit(new ClientId("setup"), 3, new JumpTo(entry.Id));
            bus.Submit(new ClientId("setup"), 4, new Play());
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (viewModel.CurrentTrackId != track.Id && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(10);
            }

            Assert.Equal(track.Id, viewModel.CurrentTrackId);
            var canvas = header.FindControl<Canvas>("WaveformCanvas");
            Assert.NotNull(canvas);
            var drew = false;
            deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                window.UpdateLayout();
                Thread.Sleep(10);
                if (canvas.Children.Count > 0)
                {
                    drew = true;
                    break;
                }
            }

            window.Close();
            Assert.True(drew);
            return 0;
        }, CancellationToken.None);
    }

    private sealed class PumpMemoryStore : IWaveformStore
    {
        private readonly Dictionary<TrackId, WaveformPeaks> _peaks = [];

        public void Save(WaveformPeaks peaks) => _peaks[peaks.TrackId] = peaks;

        public WaveformPeaks? Load(TrackId trackId) => _peaks.GetValueOrDefault(trackId);

        public void Dispose()
        {
        }
    }
}
