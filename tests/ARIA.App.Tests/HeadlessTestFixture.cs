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

public sealed class MemoryWaveformStore : IWaveformStore
{
    private readonly Dictionary<TrackId, WaveformPeaks> _peaks = [];

    public static MemoryWaveformStore Seeded(TrackId trackId, int count)
    {
        var store = new MemoryWaveformStore();
        store.SeedFlatPeaks(trackId, count);
        return store;
    }

    public void SeedFlatPeaks(TrackId trackId, int count)
    {
        var points = ImmutableArray.CreateBuilder<PeakPoint>(count);
        for (var index = 0; index < count; index++)
        {
            points.Add(new PeakPoint(-0.5f, 0.5f));
        }
        Save(new WaveformPeaks(trackId, 25, 48000, points.ToImmutable()));
    }

    public void Save(WaveformPeaks peaks) => _peaks[peaks.TrackId] = peaks;

    public WaveformPeaks? Load(TrackId trackId) => _peaks.GetValueOrDefault(trackId);

    public void Dispose()
    {
    }
}

public sealed class RecordingController : IShowHandler
{
    private readonly ShowSnapshot _snapshot;

    public RecordingController(
        TrackId trackId,
        EntryId? entryId = null,
        ImmutableArray<Marker> markers = default,
        TransportStatus status = TransportStatus.Playing,
        TimeSpan? duration = null)
    {
        Deck = new DeckContent(
            entryId,
            trackId,
            "test",
            null,
            EndAction.Pause,
            duration ?? TimeSpan.FromSeconds(90),
            TimeSpan.Zero,
            null,
            null,
            markers);
        _snapshot = new ShowSnapshot(
            0,
            new ShowState([], null, false, new ShowClockState(TimeSpan.Zero, false), [], TrackDigest.Empty, EndAction.Advance),
            0,
            new TransportState(status, Deck, null, []),
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

public sealed class TestLane : IDisposable
{
    public TestLane(
        TrackId trackId,
        EntryId? entryId = null,
        ImmutableArray<Marker> markers = default,
        TransportStatus status = TransportStatus.Playing,
        int peaks = 500,
        bool bindMonitor = true)
    {
        Controller = new RecordingController(trackId, entryId, markers, status);
        Bus = new CommandBus(Controller, BusMode.Inline);
        ViewModel = new TransportViewModel(Bus);
        Header = new Views.PlaybackHeader { DataContext = ViewModel };
        Window = new Window { Width = 700, Height = 300, Content = Header };
        Window.Show();
        Store = MemoryWaveformStore.Seeded(trackId, peaks);
        Monitor = new PlaybackMonitor();
        if (bindMonitor)
        {
            Monitor.Bind(new StreamHandle(1), Controller.Deck);
            Monitor.Publish(new StreamHandle(1), TimeSpan.FromSeconds(30));
        }
        Header.Attach(Monitor, Store);
        Window.UpdateLayout();
    }

    public RecordingController Controller { get; }

    public CommandBus Bus { get; }

    public TransportViewModel ViewModel { get; }

    public Views.PlaybackHeader Header { get; }

    public Window Window { get; }

    public MemoryWaveformStore Store { get; }

    public PlaybackMonitor Monitor { get; }

    public Canvas MarkerCanvas => Header.FindControl<Canvas>("MarkerCanvas")!;

    public Canvas EditorCanvas => Header.FindControl<Canvas>("MarkerEditorCanvas")!;

    public Point PointAt(double seconds, double totalSeconds = 90)
    {
        var canvas = Header.FindControl<Canvas>("WaveformCanvas")!;
        var strip = Header.FindControl<Border>("WaveformStrip")!;
        var x = canvas.Bounds.Width * seconds / totalSeconds;
        return strip.TranslatePoint(new Point(x, 28), Window) ?? new Point(x, 28);
    }

    public void Dispose()
    {
        Window.Close();
        ViewModel.Dispose();
        Bus.Dispose();
    }
}

public sealed class HeaderWindow : IDisposable
{
    public HeaderWindow(Views.PlaybackHeader header)
    {
        Header = header;
        Window = new Window { Width = 700, Height = 300, Content = header };
        Window.Show();
    }

    public Views.PlaybackHeader Header { get; }

    public Window Window { get; }

    public void UpdateLayout() => Window.UpdateLayout();

    public void Dispose() => Window.Close();
}
