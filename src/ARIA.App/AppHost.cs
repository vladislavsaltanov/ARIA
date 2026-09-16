namespace Aria.App;

using System.Collections.Immutable;
using System.Net.Sockets;
using Aria.App.Services;
using Aria.Audio;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.Runtime;
using Aria.Persistence;
using Aria.Remote;

public sealed record ImportReport(int Added, int Skipped, ImmutableArray<string> Failed);

public sealed class AppHost : IAsyncDisposable
{
    public const int SampleRate = 48000;
    public const int Channels = 2;
    public const int BlockSizeFrames = 512;

    private readonly RemoteOptions? _remoteOptions;
    private readonly Func<IAudioSink>? _sinkFactory;
    private readonly Func<ISourceFactory>? _sourceFactory;

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav",
        ".flac",
        ".mp3",
        ".ogg",
    };

    private CommandBus? _ownedBus;
    private SqliteLibraryStore? _library;
    private SqliteWaveformStore? _waveforms;
    private MiniaudioSourceFactory? _decoderFactory;
    private TrackImporter? _importer;
    private JsonSnapshotStore? _snapshots;
    private ShowAutosaver? _autosaver;
    private AriaAudioEngine? _engine;
    private CommandBus? _busRef;
    private ShowController? _controller;
    private System.Threading.Timer? _clockTimer;
    private long _seq;
    private bool _started;
    private bool _disposed;

    public PlaybackMonitor Monitor { get; } = new();

    public MeterMonitor Meters { get; } = new();

    public ICommandBus Bus { get; private set; } = null!;

    public RemoteHost? Remote { get; private set; }

    public ILibraryStore? Library => _library;

    public IWaveformStore? Waveforms => _waveforms;

    public ISourceFactory? SourceFactory { get; private set; }

    public WaveformScanner? WaveformScanner { get; private set; }

    public TrackImporter? Importer => _importer;

    public string DataDirectory { get; }

    public SampleRing PreviewTap { get; private set; } = null!;

    public AppHost(
        string dataDirectory,
        RemoteOptions? remoteOptions = null,
        Func<IAudioSink>? sinkFactory = null,
        Func<ISourceFactory>? sourceFactory = null)
    {
        DataDirectory = dataDirectory;
        _remoteOptions = remoteOptions;
        _sinkFactory = sinkFactory;
        _sourceFactory = sourceFactory;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            return;
        }
        _started = true;
        Directory.CreateDirectory(DataDirectory);

        var sink = _sinkFactory?.Invoke() ?? new MiniaudioSink(SampleRate, Channels, BlockSizeFrames);
        var factory = _sourceFactory?.Invoke() ?? new MiniaudioSourceFactory(SampleRate, Channels);
        SourceFactory = factory;
        _decoderFactory = factory as MiniaudioSourceFactory ?? new MiniaudioSourceFactory(SampleRate, Channels);
        WaveformScanner = new WaveformScanner(_decoderFactory);
        _importer = new TrackImporter(_decoderFactory, WaveformScanner);
        PreviewTap = new SampleRing(SampleRate * 2, Channels);
        _engine = new AriaAudioEngine(factory, Monitor, SampleRate, Channels, BlockSizeFrames, sink, Meters, CreatePreviewSink(), PreviewTap);

        var controller = new ShowController(_engine, Monitor, MarshalEngineEvent);
        _controller = controller;

        Bus = _ownedBus = new CommandBus(controller, BusMode.Pumped);
        _busRef = _ownedBus;

        _library = new SqliteLibraryStore(Path.Combine(DataDirectory, "library.db"));
        _waveforms = new SqliteWaveformStore(Path.Combine(DataDirectory, "waveforms.db"));
        _snapshots = new JsonSnapshotStore(Path.Combine(DataDirectory, "show.json"));
        _autosaver = new ShowAutosaver(Bus, _snapshots, TimeSpan.FromMilliseconds(500), () => _library.Load().Tracks);

        var document = _snapshots.LoadLatest();
        if (document is { } saved)
        {
            Submit(new RestoreShow(saved.Tracks, saved.Playlists, saved.ActiveId, saved.Queue, saved.MasterGainDb, saved.PanicFade, saved.ClockElapsed, saved.ClockRunning, saved.Scripts));
            Submit(new SetGlobalAudio(saved.EffectiveGlobal));
        }
        RepairTrackNames();
        ReportMissingFiles();

        if (_remoteOptions is { } options)
        {
            Remote = new RemoteHost(Bus, options, Monitor, Meters, PreviewTap);
            try
            {
                await Remote.StartAsync(cancellationToken);
            }
            catch (Exception e) when (options.Port != 0 && IsPortBusy(e))
            {
                Remote = new RemoteHost(Bus, options with { Port = 0 }, Monitor, Meters, PreviewTap);
                await Remote.StartAsync(cancellationToken);
            }
        }

        _clockTimer = new System.Threading.Timer(
            _ => Submit(new TickShowClock()),
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
    }

    private void MarshalEngineEvent(Action work) => _busRef?.Post(work);

    private static IAudioSink CreatePreviewSink()
    {
        try
        {
            return new MiniaudioSink(SampleRate, Channels, BlockSizeFrames);
        }
        catch (Exception e) when (e is InvalidOperationException or DllNotFoundException)
        {
            return new NullSink(SampleRate, Channels);
        }
    }

    public void Submit(Command command) => Bus.Submit(new ClientId("app"), Interlocked.Increment(ref _seq), command);

    public TrackAudioSettings? GetTrackAudio(TrackId id) => _controller?.TrackAudio(id);

    public async Task<ImportReport> ImportTracksAsync(IReadOnlyList<string> paths, IProgress<string>? progress = null)
    {
        if (_library is null || _importer is null || _waveforms is null)
        {
            throw new InvalidOperationException("AppHost is not started");
        }
        Func<string, ImportedTrack?> import = ImportOverride ?? (path => _importer.Import(path));
        var (tracks, playlists) = _library.Load();
        var current = tracks;
        var added = 0;
        var skipped = 0;
        var failed = ImmutableArray.CreateBuilder<string>();
        foreach (var filePath in ExpandAudioFiles(paths))
        {
            progress?.Report(Path.GetFileName(filePath));
            var imported = await Task.Run(() => import(filePath));
            if (imported is null)
            {
                failed.Add(filePath);
                continue;
            }
            if (current.Any(t => UnicodePaths.Key(t.FilePath) == UnicodePaths.Key(imported.Track.FilePath)))
            {
                skipped++;
                continue;
            }
            current = current.Add(imported.Track);
            if (imported.Peaks is { } peaks)
            {
                _waveforms.Save(peaks);
            }
            added++;
        }
        if (added > 0)
        {
            _library.Upsert(current, playlists);
            SyncShowState(current);
        }
        return new ImportReport(added, skipped, failed.ToImmutable());
    }

    internal Func<string, ImportedTrack?>? ImportOverride { get; set; }

    public async Task<bool> RelinkTrackAsync(TrackId trackId, string newPath)
    {
        if (_library is null || _importer is null || _waveforms is null)
        {
            return false;
        }
        if (!File.Exists(newPath))
        {
            return false;
        }
        var imported = await Task.Run(() => _importer.Import(newPath));
        if (imported is null)
        {
            return false;
        }
        var (tracks, playlists) = _library.Load();
        var index = -1;
        for (var i = 0; i < tracks.Length; i++)
        {
            if (tracks[i].Id == trackId)
            {
                index = i;
                break;
            }
        }
        if (index < 0)
        {
            return false;
        }
        var relinked = imported.Track with { Id = trackId };
        _library.Upsert(tracks.SetItem(index, relinked), playlists);
        if (imported.Peaks is { } peaks)
        {
            _waveforms.Save(peaks with { TrackId = trackId });
        }
        SyncShowState([relinked]);
        return true;
    }

    private void ReportMissingFiles()
    {
        if (_library is null)
        {
            return;
        }
        var (tracks, _) = _library.Load();
        var missing = tracks.Where(t => !File.Exists(t.FilePath)).Select(t => t.Id).ToImmutableArray();
        if (!missing.IsEmpty)
        {
            Submit(new MarkMissing(missing));
        }
    }

    private void RepairTrackNames()
    {
        if (_library is null || _importer is null)
        {
            return;
        }
        var marker = Path.Combine(DataDirectory, "metadata-backfill.done");
        if (File.Exists(marker))
        {
            return;
        }
        var (tracks, playlists) = _library.Load();
        var repaired = tracks.Select(t => _importer.RefreshDisplayName(t)).ToImmutableArray();
        if (!repaired.SequenceEqual(tracks))
        {
            _library.Upsert(repaired, playlists);
            SyncShowState(repaired);
        }
        File.WriteAllText(marker, DateTimeOffset.UtcNow.ToString("o"));
    }

    private static bool IsPortBusy(Exception exception)
    {
        for (var current = (Exception?)exception; current is not null; current = current.InnerException)
        {
            if (current is SocketException socket && socket.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                return true;
            }
        }
        return false;
    }

    private static IEnumerable<string> ExpandAudioFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                string[] files;
                try
                {
                    files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                foreach (var file in files.Order(StringComparer.Ordinal))
                {
                    if (AudioExtensions.Contains(Path.GetExtension(file)))
                    {
                        yield return file;
                    }
                }
            }
            else if (File.Exists(path))
            {
                yield return path;
            }
        }
    }

    private void SyncShowState(ImmutableArray<Track> tracks)
    {
        Submit(new MergeTracks(tracks));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _clockTimer?.Dispose();
        _clockTimer = null;
        if (_autosaver is { } autosaver)
        {
            autosaver.FlushNow();
            autosaver.Dispose();
        }
        if (Remote is { } remote)
        {
            await remote.DisposeAsync();
        }
        _ownedBus?.Dispose();
        if (_engine is { } engine)
        {
            await Task.Run(engine.Dispose);
        }
        _snapshots?.Dispose();
        _library?.Dispose();
        _waveforms?.Dispose();
    }
}
