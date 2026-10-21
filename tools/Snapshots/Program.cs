using System.Collections.Immutable;
using Aria.App;
using Aria.App.Services;
using Aria.App.ViewModels;
using Aria.App.Views;
using Aria.Audio;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.State;
using Aria.Persistence;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ShapePath = Avalonia.Controls.Shapes.Path;

internal static class SnapshotProgram
{
    private static readonly (string Name, int Seconds)[] Scene =
    [
        ("Интро — выход ведущего", 42),
        ("Тема ARIA", 68),
        ("Подложка — интервью с приглашённым экспертом", 185),
        ("Отбивка — блок 2", 26),
        ("Финал — титры", 96),
    ];

    private static AppHost? _host;

    [STAThread]
    public static int Main(string[] args)
    {
        var output = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "aria-snapshots"));
        return Run(output);
    }

    private static int Run(string output)
    {
        Directory.CreateDirectory(output);
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"aria-snapshot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDirectory);
        try
        {
            var tracks = SeedLibrary(dataDirectory);
            WriteDocument(dataDirectory, tracks);

            var host = new AppHost(dataDirectory, null, () => new NullSink(8000, 1), () => new MiniaudioSourceFactory(8000, 1));
            _host = host;
            host.StartAsync().GetAwaiter().GetResult();
            Report("host started");

            AppBuilder.Configure<App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false, ShouldRenderOnUIThread = true })
                .SetupWithoutStarting();
            AvaloniaSynchronizationContext.InstallIfNeeded();
            Report("platform started");

            MainWindow? window = null;
            var thumbs = new WaveformThumbs(host.Waveforms!);
            var rowSettings = host.SettingsStore.Load();
            using var transport = new TransportViewModel(host.Bus, host.Monitor, SynchronizationContext.Current, host.Meters, () => host.Tracks, rowSettings);
            using var projects = new ProjectsViewModel(host.Bus, () => host.Tracks, thumbs, rowSettings, SynchronizationContext.Current, topLevel: () => window, audioImport: host.ImportTracksAsync, trackAudio: host.GetTrackAudio, scriptExporter: _ => Array.Empty<(string Name, string Json)>());
            using var queue = new QueueViewModel(host.Bus, SynchronizationContext.Current);
            using var scripts = new ScriptPanelViewModel(host.Bus, () => host.Tracks, SynchronizationContext.Current, topLevel: () => window, projectDirSource: projects.GetProjectDirectory);
            window = new MainWindow(null, projects, queue, null, scripts) { DataContext = transport };
            window.Show();
            window.AttachPlaybackHeader(host.Monitor, host.Waveforms);
            Pump(TimeSpan.FromMilliseconds(400));
            Report("window shown");

            Capture(window, Path.Combine(output, "main-stopped.png"), 1440, 900);
            Inspect(window, "main-stopped");
            InjectStates(window, transport, tracks[0].Id);
            Pump(TimeSpan.FromMilliseconds(400));
            Capture(window, Path.Combine(output, "main-states.png"), 1440, 900);
            Inspect(window, "main-states");
            ProbeHover(window, output);
            ProbeDragHold(window, output);
            CaptureWidths(window, output);
            transport.IsPlaying = false;
            transport.StatusText = "STOP";
            Capture(window, Path.Combine(output, "main-min.png"), 1100, 700);
            Inspect(window, "main-min");
            window.ToggleScriptPane();
            Pump(TimeSpan.FromMilliseconds(500));
            Capture(window, Path.Combine(output, "main-script.png"), 1440, 900);
            Report($"bus status: {host.Bus.Snapshot().Transport.Status}");
            Console.Out.Flush();
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"SNAPSHOT FAIL: {e}");
            return 1;
        }
        finally
        {
            if (_host is { } running)
            {
                Task.Run(() => running.DisposeAsync().AsTask()).GetAwaiter().GetResult();
            }
            TryDelete(dataDirectory);
        }
    }

    private static ImmutableArray<Track> SeedLibrary(string dataDirectory)
    {
        var audio = Path.Combine(dataDirectory, "audio");
        Directory.CreateDirectory(audio);
        var paths = Scene.Select(item => WriteWav(Path.Combine(audio, item.Name + ".wav"), item.Seconds)).ToArray();
        var staging = new AppHost(dataDirectory, null, () => new NullSink(8000, 1), () => new MiniaudioSourceFactory(8000, 1));
        staging.StartAsync().GetAwaiter().GetResult();
        var report = staging.ImportTracksAsync(paths).GetAwaiter().GetResult();
        Report($"import added={report.Added} skipped={report.Skipped} failed={report.Failed.Length}");
        var tracks = staging.Library!.Load().Tracks;
        Report($"library tracks={tracks.Length}");
        staging.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return tracks;
    }

    private static void WriteDocument(string dataDirectory, ImmutableArray<Track> tracks)
    {
        var project = new Project(
            ProjectId.New(),
            "Основной сценарий",
            [.. tracks.Select(track => new ProjectEntry(EntryId.New(), track.Id))]);
        var rehearsal = new Project(
            ProjectId.New(),
            "Репетиция",
            [.. new[] { tracks[0], tracks[3], tracks[4] }.Select(track => new ProjectEntry(EntryId.New(), track.Id))]);
        var queue = ImmutableArray.Create(
            new QueueItem(null, tracks[1].Id, tracks[1].DefaultName, null),
            new QueueItem(null, tracks[2].Id, tracks[2].DefaultName, null),
            new QueueItem(null, tracks[3].Id, tracks[3].DefaultName, null));
        var script = new Script(
            ScriptId.New(),
            "Церемония",
            [
                new ScriptLine(ScriptLineId.New(), TimeSpan.Zero, "Здравствуйте, мы начинаем", []),
                new ScriptLine(ScriptLineId.New(), TimeSpan.FromSeconds(120), "Представление участников", []),
                new ScriptLine(ScriptLineId.New(), TimeSpan.FromSeconds(481), "Блок вопросов из зала", []),
                new ScriptLine(ScriptLineId.New(), TimeSpan.FromSeconds(742), "Показываем фрагмент — выводим тему", [new Mention(tracks[1].Id)]),
                new ScriptLine(ScriptLineId.New(), TimeSpan.FromSeconds(1050), "Финальные слова, выход на поклон", [new Mention(tracks[4].Id)]),
            ],
            project.Id);
        using var store = new JsonSnapshotStore(Path.Combine(dataDirectory, "show.json"));
        store.Save(new ShowDocument(
            tracks,
            [project, rehearsal],
            project.Id,
            queue,
            -3.5,
            TimeSpan.FromMilliseconds(90),
            TimeSpan.FromSeconds(757),
            true,
            [script],
            DateTimeOffset.UtcNow));
    }

    private static void Capture(Window window, string path, double width, double height)
    {
        window.Width = width;
        window.Height = height;
        Pump(TimeSpan.FromMilliseconds(250));
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException($"no frame for {Path.GetFileName(path)}");
        frame.Save(path, new PngBitmapEncoderOptions());
        Report($"wrote {path} {frame.PixelSize.Width}x{frame.PixelSize.Height}");
    }

    private static void InjectStates(Window window, TransportViewModel transport, TrackId trackId)
    {
        var store = new MemoryWaveformStore();
        var points = ImmutableArray.CreateBuilder<PeakPoint>();
        for (var index = 0; index < 400; index++)
        {
            var amplitude = (float)(0.35 + (0.25 * Math.Sin(index * 0.3)));
            points.Add(new PeakPoint(-amplitude, amplitude));
        }
        store.Save(new WaveformPeaks(trackId, 25, 48000, points.ToImmutable()));
        var monitor = new PlaybackMonitor();
        var handle = new StreamHandle(1);
        monitor.Bind(handle, new DeckContent(null, trackId, "Тема ARIA", null, EndAction.Pause, TimeSpan.FromSeconds(42), TimeSpan.Zero));
        monitor.Publish(handle, TimeSpan.FromSeconds(15));
        _monitor = monitor;
        Find<PlaybackHeader>(window, "PlaybackHeader")?.Attach(monitor, store);
        transport.CurrentTrackId = trackId;
        transport.IsPlaying = true;
        transport.StatusText = "PLAY";
        foreach (var row in window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("plRow")).Take(1))
        {
            row.Classes.Add("linked");
        }
        foreach (var row in window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("qRow")).Take(1))
        {
            row.Classes.Add("current");
        }
    }

    private sealed class MemoryWaveformStore : IWaveformStore
    {
        private readonly Dictionary<TrackId, WaveformPeaks> _peaks = [];

        public void Save(WaveformPeaks peaks) => _peaks[peaks.TrackId] = peaks;

        public WaveformPeaks? Load(TrackId id) => _peaks.GetValueOrDefault(id);

        public void Dispose()
        {
        }
    }

    private static PlaybackMonitor? _monitor;

    private static void ProbeHover(Window window, string output)
    {
        var plain = window.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("plRow") && !b.Classes.Contains("linked"))
            .OrderBy(b => Box(b, window).Y).FirstOrDefault();
        if (plain is null)
        {
            Report("hover: нет обычной строки плейлиста");
            return;
        }
        var box = Box(plain, window);
        window.MouseMove(new Point(box.X + (box.Width / 2), box.Y + (box.Height / 2)));
        Pump(TimeSpan.FromMilliseconds(300));
        var container = plain.GetVisualAncestors().OfType<ContentPresenter>().FirstOrDefault();
        Report($"hover: rowBg={Brush(plain.Background)} containerBg={Brush(container?.Background)} rowY={box.Y:0} rowH={box.Height:0}");
        Capture(window, Path.Combine(output, "hover.png"), 1440, 900);
        var queuePlain = window.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("qRow") && !b.Classes.Contains("current"))
            .OrderBy(b => Box(b, window).Y).FirstOrDefault();
        if (queuePlain is not null)
        {
            var qbox = Box(queuePlain, window);
            window.MouseMove(new Point(qbox.X + (qbox.Width / 2), qbox.Y + (qbox.Height / 2)));
            Pump(TimeSpan.FromMilliseconds(300));
            Report($"hover queue: rowBg={Brush(queuePlain.Background)} y={qbox.Y:0}");
            Capture(window, Path.Combine(output, "hover-queue.png"), 1440, 900);
        }
        window.MouseMove(new Point(2, 2));
        Pump(TimeSpan.FromMilliseconds(250));
    }

    private static void ProbeDragHold(Window window, string output)
    {
        var strip = Find<Control>(window, "WaveformStrip");
        if (strip is null || _monitor is null)
        {
            Report("drag: нет полосы волны или монитора");
            return;
        }
        var box = Box(strip, window);
        var at = new Point(box.X + (box.Width * 0.6), box.Y + (box.Height / 2));
        window.MouseDown(at, MouseButton.Left);
        Pump(TimeSpan.FromMilliseconds(300));
        _monitor.Publish(new StreamHandle(1), TimeSpan.FromSeconds(24));
        Pump(TimeSpan.FromMilliseconds(700));
        var cursor = Find<Control>(window, "WaveCursor");
        var played = Find<Border>(window, "WavePlayed");
        var expected = box.X + (box.Width * 0.6);
        Report($"drag hold: cursorVisible={cursor?.IsVisible} cursorX={Box(cursor!, window).X:0} expectedX={expected:0} played={played?.Bounds.Width:0} playedVisible={played?.IsVisible}");
        Capture(window, Path.Combine(output, "drag-hold.png"), 1440, 900);
        window.MouseUp(at, MouseButton.Left);
        Pump(TimeSpan.FromMilliseconds(300));
    }

    private static void CaptureWidths(Window window, string output)
    {
        foreach (var width in new[] { 1280, 1180, 1100 })
        {
            window.Width = width;
            Pump(TimeSpan.FromMilliseconds(260));
            var meters = Find<Border>(window, "MetersCluster");
            var lufsValue = Find<TextBlock>(window, "LufsValue");
            var lufsBar = Find<ProgressBar>(window, "LufsBar");
            var levels = Find<StackPanel>(window, "LevelsGroup");
            var panic = Find<Button>(window, "PanicButton");
            var center = Find<Control>(window, "ProjectCenter");
            var chips = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("chip")).ToList();
            var maxChipRight = chips.Count == 0 ? 0.0 : chips.Max(c => Box(c, window).Right);
            var centerRight = center is null ? 0.0 : Box(center, window).Right;
            Report($"width {width}: meters=w{(meters?.Bounds.Width ?? 0):0} lufsValue={lufsValue?.IsVisible} lufsBar={lufsBar?.IsVisible} levels={levels?.IsVisible} panicRight={Box(panic!, window).Right:0} chips={chips.Count} maxChipRight={maxChipRight:0} centerRight={centerRight:0} overflow={maxChipRight > centerRight}");
            Capture(window, Path.Combine(output, $"width-{width}.png"), width, 900);
        }
        window.Width = 1440;
        Pump(TimeSpan.FromMilliseconds(200));
    }

    private static void Inspect(Window window, string label)
    {
        Report($"--- inspect {label} ---");
        foreach (var name in new[] { "PlaybackHeader", "RailProjects", "ProjectCenter", "QueueColumn", "TransportBar", "ScriptDrawer", "WaveformStrip", "WaveformCanvas", "WaveCursor", "WavePlayed", "WaveRemain", "ShowClockBig", "PanicButton", "PlayPauseButton" })
        {
            var control = Find<Control>(window, name);
            if (control is null)
            {
                Report($"{name}: missing");
                continue;
            }
            var rect = Box(control, window);
            Report($"{name}: x={rect.X:0} y={rect.Y:0} w={rect.Width:0} h={rect.Height:0} visible={control.IsVisible}");
        }
        foreach (var cluster in window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("cluster")).ToList())
        {
            var rect = Box(cluster, window);
            Report($"cluster: x={rect.X:0} w={rect.Width:0} h={rect.Height:0}");
        }
        var clock = Find<TextBlock>(window, "ShowClockBig");
        if (clock is not null)
        {
            var rect = Box(clock, window);
            Report($"clock: '{clock.Text}' size={clock.FontSize:0} fg={Brush(clock.Foreground)} centerDelta={(rect.X + rect.Width / 2 - window.Width / 2):0.0}");
        }
        var panic = Find<Button>(window, "PanicButton");
        var play = Find<Button>(window, "PlayPauseButton");
        if (panic is not null && play is not null)
        {
            var panicBox = Box(panic, window);
            var playBox = Box(play, window);
            Report($"transport: play={playBox.Width:0}x{playBox.Height:0} panic={panicBox.Width:0}x{panicBox.Height:0} panicRightGap={window.ClientSize.Width - panicBox.Right:0} playBg={Brush(BackgroundOf(play))} panicBg={Brush(BackgroundOf(panic))}");
        }
        var strip = Find<Control>(window, "WaveformStrip");
        var canvas = Find<Control>(window, "WaveformCanvas");
        var cursor = Find<Control>(window, "WaveCursor");
        var played = Find<Border>(window, "WavePlayed");
        var remain = Find<Border>(window, "WaveRemain");
        if (strip is not null && canvas is not null && cursor is not null)
        {
            Report($"waveform: strip={Box(strip, window).Width:0} canvas={Box(canvas, window).Width:0} paths={canvas.GetVisualDescendants().OfType<ShapePath>().Count()} cursorVisible={cursor.IsVisible} cursorX={Box(cursor, window).X:0} played={played?.Bounds.Width:0} remain={remain?.Bounds.Width:0}");
        }
        foreach (var chip in window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("stateChip")).ToList())
        {
            Report($"stateChip: '{chip.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text}' bg={Brush(chip.Background)}");
        }
        foreach (var current in window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("current")).ToList())
        {
            Report($"current: classes=[{string.Join(",", current.Classes)}] bg={Brush(current.Background)} border={Brush(current.BorderBrush)} text='{current.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))}'");
        }
        var queueRows = window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("qRow")).ToList();
        Report($"queueRows: {queueRows.Count} current={queueRows.Count(b => b.Classes.Contains("current"))} texts=[{string.Join(", ", queueRows.Select(r => Trim(r.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)))))}]");
        var rowBorders = window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("plRow")).ToList();
        var linkedRow = rowBorders.FirstOrDefault(b => b.Classes.Contains("linked"));
        Report($"playlistRows: {rowBorders.Count} linked={rowBorders.Count(b => b.Classes.Contains("linked"))} height={rowBorders.FirstOrDefault()?.Bounds.Height:0} linkedBg={Brush(linkedRow?.Background)} linkedBorder={Brush(linkedRow?.BorderBrush)}");
        var headerCells = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("col")).ToList();
        if (headerCells.Count > 0)
        {
            Report("columns: " + string.Join(" | ", headerCells.Select(c => $"{c.Text}@{Box(c, window).X:0}")));
        }
        var rowCells = rowBorders.FirstOrDefault()?.GetVisualDescendants().OfType<TextBlock>()
            .Select(c => $"{Trim(c.Text)}@{Box(c, window).X:0}").ToList();
        if (rowCells is not null && rowCells.Count > 0)
        {
            Report("row0: " + string.Join(" | ", rowCells));
        }
        Report($"window: {window.ClientSize.Width:0}x{window.ClientSize.Height:0}");
    }

    private static string Trim(string? text) => text is null ? "-" : text.Length > 14 ? text[..14] : text;

    private static T? Find<T>(Visual root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name);

    private static IBrush? BackgroundOf(Control control) =>
        control.GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault(c => c.Name == "PART_ContentPresenter")?.Background;

    private static string Brush(IBrush? brush) => brush is ISolidColorBrush solid ? solid.Color.ToString() : brush?.ToString() ?? "none";

    private static Rect Box(Control control, Visual root) =>
        new(control.TranslatePoint(new Point(0, 0), root) ?? new Point(0, 0), control.Bounds.Size);

    private static void Pump(TimeSpan duration)
    {
        var deadline = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static void Report(string message)
    {
        Console.WriteLine(message);
        Console.Out.Flush();
    }

    private static string WriteWav(string path, int seconds, int sampleRate = 8000)
    {
        var frames = sampleRate * seconds;
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + frames * 2);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(frames * 2);
        for (var i = 0; i < frames; i++)
        {
            var value = Math.Sin(2.0 * Math.PI * 220.0 * i / sampleRate) * 0.6;
            writer.Write((short)(value * short.MaxValue));
        }
        return path;
    }

    private static void TryDelete(string directory)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
                return;
            }
            catch (IOException) when (attempt < 20)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(100);
            }
        }
    }
}
