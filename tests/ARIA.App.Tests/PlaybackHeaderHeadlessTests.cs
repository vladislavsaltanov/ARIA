namespace Aria.App.Tests;

using System.Collections.Immutable;
using Aria.App.ViewModels;
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
using Avalonia.Input;

[Collection("headless")]
public sealed class PlaybackHeaderHeadlessTests
{
    private static readonly Track TestTrack = new(
        TrackId.New(),
        "/audio/header-test.flac",
        "header-test",
        TimeSpan.FromSeconds(90),
        new TrackDefaults());

    private readonly HeadlessUnitTestSession _session;

    public PlaybackHeaderHeadlessTests(HeadlessSessionFixture fixture)
    {
        _session = fixture.Session;
    }

    [Fact]
    public async Task MainWindow_Composes_PlaybackHeader_AndDragOnWaveSeeks()
    {
        await _session.Dispatch(() =>
        {
            var window = new Views.MainWindow(null);
            window.Show();
            var header = window.FindControl<Views.PlaybackHeader>("PlaybackHeader");
            Assert.NotNull(header);
            Assert.NotNull(header.FindControl<TextBlock>("ShowClockBig"));
            Assert.NotNull(header.FindControl<TextBlock>("TimerSubBig"));
            Assert.NotNull(header.FindControl<Border>("WaveformStrip"));
            Assert.NotNull(header.FindControl<Canvas>("WaveformCanvas"));
            Assert.NotNull(header.FindControl<Rectangle>("WaveCursor"));
            Assert.NotNull(header.FindControl<Button>("StartClockButton"));
            Assert.NotNull(header.FindControl<Button>("PauseClockButton"));
            Assert.NotNull(header.FindControl<Button>("ResetClockButton"));
            window.Close();

            var controller = new RecordingController();
            using var bus = new CommandBus(controller, BusMode.Inline);
            using var viewModel = new TransportViewModel(bus);
            var dragHeader = new Views.PlaybackHeader { DataContext = viewModel };
            var dragWindow = new Window { Width = 700, Height = 300, Content = dragHeader };
            dragWindow.Show();

            var strip = dragHeader.FindControl<Border>("WaveformStrip");
            Assert.NotNull(strip);

            var store = new MemoryWaveformStore();
            var points = ImmutableArray.CreateBuilder<PeakPoint>();
            for (var index = 0; index < 500; index++)
            {
                points.Add(new PeakPoint(-0.5f, 0.5f));
            }
            store.Save(new WaveformPeaks(TestTrack.Id, 25, 48000, points.ToImmutable()));
            var monitor = new PlaybackMonitor();
            var deck = new DeckContent(null, TestTrack.Id, "test", null, EndAction.Pause, TimeSpan.FromSeconds(90), TimeSpan.Zero);
            monitor.Bind(new StreamHandle(1), deck);
            monitor.Publish(new StreamHandle(1), TimeSpan.FromSeconds(30));
            dragHeader.Attach(monitor, store);

            dragWindow.UpdateLayout();
            var cursor = dragHeader.FindControl<Rectangle>("WaveCursor");
            Assert.NotNull(cursor);
            Assert.True(cursor.IsVisible);
            var canvas = dragHeader.FindControl<Canvas>("WaveformCanvas");
            Assert.NotNull(canvas);
            Assert.True(canvas.Children.Count > 0);
            Assert.Equal(canvas.Bounds.Width / 3, Canvas.GetLeft(cursor), 1);
            Assert.Same(canvas, cursor.Parent);
            var cursorAt = cursor.TranslatePoint(new Point(0, 0), canvas);
            Assert.NotNull(cursorAt);
            Assert.Equal(canvas.Bounds.Width / 3, cursorAt.Value.X, 0);

            var width = strip.Bounds.Width;
            var at = strip.TranslatePoint(new Point(width * 0.25, 28), dragWindow);
            Assert.NotNull(at);
            dragWindow.MouseDown(at.Value, MouseButton.Left);
            dragWindow.MouseUp(at.Value, MouseButton.Left);

            var command = Assert.IsType<SeekTo>(Assert.Single(controller.Received));
            Assert.Equal(22.5, command.FilePosition.TotalSeconds, 3);

            dragWindow.Close();
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Waveform_Draws_FromViewModelCurrentTrack_WhenTransportStopped()
    {
        await _session.Dispatch(() =>
        {
            var controller = new RecordingController();
            using var bus = new CommandBus(controller, BusMode.Inline);
            using var viewModel = new TransportViewModel(bus);
            var header = new Views.PlaybackHeader { DataContext = viewModel };
            var window = new Window { Width = 700, Height = 300, Content = header };
            window.Show();

            var store = new MemoryWaveformStore();
            var points = ImmutableArray.CreateBuilder<PeakPoint>();
            for (var index = 0; index < 100; index++)
            {
                points.Add(new PeakPoint(-0.5f, 0.5f));
            }
            store.Save(new WaveformPeaks(TestTrack.Id, 25, 48000, points.ToImmutable()));
            var monitor = new PlaybackMonitor();
            header.Attach(monitor, store);

            window.UpdateLayout();
            var canvas = header.FindControl<Canvas>("WaveformCanvas");
            Assert.NotNull(canvas);
            Assert.True(canvas.Children.Count > 0);

            window.Close();
            return 0;
        }, CancellationToken.None);
    }

    private sealed class RecordingController : IShowHandler
    {
        private static readonly DeckContent TestDeck = new(
            null,
            TestTrack.Id,
            "test",
            null,
            EndAction.Pause,
            TimeSpan.FromSeconds(90),
            TimeSpan.Zero);

        private readonly ShowSnapshot _snapshot = new(
            0,
            new ShowState([], null, false, new ShowClockState(TimeSpan.Zero, false), [], TrackDigest.Empty, EndAction.Advance),
            0,
            new TransportState(TransportStatus.Stopped, TestDeck, null, []),
            0,
            new QueueState([]),
            0,
            new MixerState(0, false, TimeSpan.FromMilliseconds(100), Smoothing.Default));

        public List<Command> Received { get; } = [];

        public Action<StateEvent>? Emitted { get; set; }

        public void Handle(ClientId client, long seq, Command command) => Received.Add(command);

        public ShowSnapshot Snapshot() => _snapshot;
    }

    private sealed class MemoryWaveformStore : IWaveformStore
    {
        private readonly Dictionary<TrackId, WaveformPeaks> _peaks = [];

        public void Save(WaveformPeaks peaks) => _peaks[peaks.TrackId] = peaks;

        public WaveformPeaks? Load(TrackId trackId) => _peaks.GetValueOrDefault(trackId);

        public void Dispose()
        {
        }
    }
}