namespace Aria.App.Tests;

using System.Collections.Immutable;
using Aria.App.Services;
using Aria.App.ViewModels;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.Runtime;
using Aria.Core.State;
using Aria.Persistence;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

[Collection("headless")]
public sealed class MarkerLaneHeadlessTests
{
    private static readonly TrackId TestTrackId = TrackId.New();
    private static readonly EntryId TestEntryId = EntryId.New();
    private static readonly TimeSpan TrackDuration = TimeSpan.FromSeconds(90);

    private readonly HeadlessUnitTestSession _session;

    public MarkerLaneHeadlessTests(HeadlessSessionFixture fixture)
    {
        _session = fixture.Session;
    }

    [Fact]
    public async Task RightPress_OnEmptyWave_PlacesNamedMarker()
    {
        await _session.Dispatch(() =>
        {
            using var lane = new Lane(withEntry: false, markers: []);
            lane.Window.MouseDown(lane.PointAt(45), MouseButton.Right);
            Dispatcher.UIThread.RunJobs();

            var editor = Assert.IsType<TextBox>(Assert.Single(lane.EditorCanvas.Children));
            Assert.Equal("Метка 1", editor.Text);

            editor.Text = "вход";
            lane.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

            var command = Assert.IsType<SetTrackMarkers>(Assert.Single(lane.Controller.Received));
            var marker = Assert.Single(command.Markers!.Value);
            Assert.Equal("вход", marker.Name);
            Assert.Equal(45.0, marker.Position.TotalSeconds, 1);
            Assert.Null(lane.EditorCanvas.Children.OfType<TextBox>().FirstOrDefault());
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RightPress_OnEntry_UsesEntryCommand()
    {
        await _session.Dispatch(() =>
        {
            using var lane = new Lane(withEntry: true, markers: []);
            lane.Window.MouseDown(lane.PointAt(45), MouseButton.Right);
            Dispatcher.UIThread.RunJobs();
            lane.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

            var command = Assert.IsType<SetEntryMarkers>(Assert.Single(lane.Controller.Received));
            Assert.Equal(TestEntryId, command.Entry);
            Assert.Equal(45.0, Assert.Single(command.Markers!.Value).Position.TotalSeconds, 1);
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EditorEscape_DropsPlacedMarker()
    {
        await _session.Dispatch(() =>
        {
            using var lane = new Lane(withEntry: false, markers: []);
            lane.Window.MouseDown(lane.PointAt(45), MouseButton.Right);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(lane.EditorCanvas.Children);

            lane.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

            Assert.Empty(lane.Controller.Received);
            Assert.Empty(lane.EditorCanvas.Children);
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task LeftPress_OnMarker_SeeksToItsPosition()
    {
        await _session.Dispatch(() =>
        {
            using var lane = new Lane(withEntry: false, markers: [Marker("вход", 45)]);
            lane.Window.MouseDown(lane.PointAt(45), MouseButton.Left);
            lane.Window.MouseUp(lane.PointAt(45), MouseButton.Left);

            var command = Assert.IsType<SeekTo>(Assert.Single(lane.Controller.Received));
            Assert.Equal(45.0, command.FilePosition.TotalSeconds, 1);
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RightPress_OnMarker_WithoutMove_RemovesIt()
    {
        await _session.Dispatch(() =>
        {
            using var lane = new Lane(withEntry: false, markers: [Marker("вход", 45)]);
            lane.Window.MouseDown(lane.PointAt(45), MouseButton.Right);
            lane.Window.MouseUp(lane.PointAt(45), MouseButton.Right);

            var command = Assert.IsType<SetTrackMarkers>(Assert.Single(lane.Controller.Received));
            Assert.True(command.Markers!.Value.IsEmpty);
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RightPress_OnMarker_WithMove_SendsMovedPosition()
    {
        await _session.Dispatch(() =>
        {
            using var lane = new Lane(withEntry: false, markers: [Marker("вход", 45)]);
            lane.Window.MouseDown(lane.PointAt(45), MouseButton.Right);
            lane.Window.MouseMove(lane.PointAt(67.5), RawInputModifiers.RightMouseButton);
            lane.Window.MouseUp(lane.PointAt(67.5), MouseButton.Right);

            var command = Assert.IsType<SetTrackMarkers>(Assert.Single(lane.Controller.Received));
            var marker = Assert.Single(command.Markers!.Value);
            Assert.Equal("вход", marker.Name);
            Assert.Equal(67.5, marker.Position.TotalSeconds, 1);
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task LeftPress_OnEmptyWave_StillSeeks()
    {
        await _session.Dispatch(() =>
        {
            using var lane = new Lane(withEntry: false, markers: [Marker("вход", 45)]);
            lane.Window.MouseDown(lane.PointAt(22.5), MouseButton.Left);
            lane.Window.MouseUp(lane.PointAt(22.5), MouseButton.Left);

            var command = Assert.IsType<SeekTo>(Assert.Single(lane.Controller.Received));
            Assert.Equal(22.5, command.FilePosition.TotalSeconds, 1);
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CrowdedMarkers_DrawOneName_TwoTimes()
    {
        await _session.Dispatch(() =>
        {
            using var lane = new Lane(withEntry: false, markers: [Marker("первая", 45), Marker("вторая", 45.05)]);
            var texts = lane.MarkerCanvas.Children.OfType<TextBlock>().ToList();

            Assert.Equal(2, texts.Count(text => text.FontSize == 10));
            Assert.Equal("первая", Assert.Single(texts, text => text.FontSize == 11).Text);
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CtrlZ_InTextInput_DoesNotDispatchHotkey()
    {
        await _session.Dispatch(() =>
        {
            var dispatched = new List<string>();
            var service = new HotkeyService(HotkeyConfig.Default, dispatched.Add);
            var window = new Views.MainWindow(service);
            window.Show();
            window.UpdateLayout();

            window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, null);
            Assert.Equal(["undo"], dispatched);

            dispatched.Clear();
            var host = Assert.IsAssignableFrom<Panel>(window.Content);
            var box = new TextBox();
            host.Children.Add(box);
            box.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Z,
                KeyModifiers = KeyModifiers.Control,
            });

            Assert.Empty(dispatched);
            window.Close();
            return 0;
        }, CancellationToken.None);
    }

    private static Marker Marker(string name, double seconds) =>
        new(name, TimeSpan.FromSeconds(seconds), MarkerAction.Stop);

    private sealed class Lane : IDisposable
    {
        public Lane(bool withEntry, ImmutableArray<Marker> markers)
        {
            Controller = new RecordingController(withEntry, markers);
            Bus = new CommandBus(Controller, BusMode.Inline);
            ViewModel = new TransportViewModel(Bus);
            Header = new Views.PlaybackHeader { DataContext = ViewModel };
            Window = new Window { Width = 700, Height = 300, Content = Header };
            Window.Show();
            var store = new MemoryWaveformStore();
            var points = ImmutableArray.CreateBuilder<PeakPoint>();
            for (var index = 0; index < 500; index++)
            {
                points.Add(new PeakPoint(-0.5f, 0.5f));
            }
            store.Save(new WaveformPeaks(TestTrackId, 25, 48000, points.ToImmutable()));
            var monitor = new PlaybackMonitor();
            monitor.Bind(new StreamHandle(1), Controller.Deck);
            monitor.Publish(new StreamHandle(1), TimeSpan.FromSeconds(30));
            Header.Attach(monitor, store);
            Window.UpdateLayout();
        }

        public RecordingController Controller { get; }

        public CommandBus Bus { get; }

        public TransportViewModel ViewModel { get; }

        public Views.PlaybackHeader Header { get; }

        public Window Window { get; }

        public Canvas MarkerCanvas => Header.FindControl<Canvas>("MarkerCanvas")!;

        public Canvas EditorCanvas => Header.FindControl<Canvas>("MarkerEditorCanvas")!;

        public Point PointAt(double seconds)
        {
            var canvas = Header.FindControl<Canvas>("WaveformCanvas")!;
            var strip = Header.FindControl<Border>("WaveformStrip")!;
            var x = canvas.Bounds.Width * seconds / TrackDuration.TotalSeconds;
            return strip.TranslatePoint(new Point(x, 28), Window) ?? new Point(x, 28);
        }

        public void Dispose()
        {
            Window.Close();
            ViewModel.Dispose();
            Bus.Dispose();
        }
    }

    private sealed class RecordingController : IShowHandler
    {
        private readonly ShowSnapshot _snapshot;

        public RecordingController(bool withEntry, ImmutableArray<Marker> markers)
        {
            Deck = new DeckContent(
                withEntry ? TestEntryId : null,
                TestTrackId,
                "test",
                null,
                EndAction.Pause,
                TrackDuration,
                TimeSpan.Zero,
                null,
                null,
                markers);
            _snapshot = new ShowSnapshot(
                0,
                new ShowState([], null, false, new ShowClockState(TimeSpan.Zero, false), [], TrackDigest.Empty, EndAction.Advance),
                0,
                new TransportState(TransportStatus.Playing, Deck, null, []),
                0,
                new QueueState([]),
                0,
                new MixerState(0, false, TimeSpan.FromMilliseconds(100), Smoothing.Default));
        }

        public DeckContent Deck { get; }

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
