namespace Aria.App;

using System.Collections.Immutable;
using Aria.App.Services;
using Aria.Audio;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.Runtime;
using Aria.Persistence;
using Aria.Remote;

public sealed class AppHost : IAsyncDisposable
{
    public const int SampleRate = 48000;
    public const int Channels = 2;
    public const int BlockSizeFrames = 512;

    private readonly RemoteOptions? _remoteOptions;
    private readonly Func<IAudioSink>? _sinkFactory;
    private readonly Func<ISourceFactory>? _sourceFactory;

    private CommandBus? _ownedBus;
    private SqliteLibraryStore? _library;
    private SqliteWaveformStore? _waveforms;
    private MiniaudioSourceFactory? _decoderFactory;
    private TrackImporter? _importer;
    private JsonSnapshotStore? _snapshots;
    private ShowAutosaver? _autosaver;
    private AriaAudioEngine? _engine;
    private CommandBus? _busRef;
    private long _seq;
    private bool _started;

    public PlaybackMonitor Monitor { get; } = new();

    public ICommandBus Bus { get; private set; } = null!;

    public RemoteHost? Remote { get; private set; }

    public ILibraryStore? Library => _library;

    public IWaveformStore? Waveforms => _waveforms;

    public ISourceFactory? SourceFactory { get; private set; }

    public WaveformScanner? WaveformScanner { get; private set; }

    public TrackImporter? Importer => _importer;

    public string DataDirectory { get; }

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
        _engine = new AriaAudioEngine(factory, Monitor, SampleRate, Channels, BlockSizeFrames, sink);

        var controller = new ShowController(_engine, Monitor, MarshalEngineEvent);

        Bus = _ownedBus = new CommandBus(controller, BusMode.Pumped);
        _busRef = _ownedBus;

        _library = new SqliteLibraryStore(Path.Combine(DataDirectory, "library.db"));
        _waveforms = new SqliteWaveformStore(Path.Combine(DataDirectory, "waveforms.db"));
        _snapshots = new JsonSnapshotStore(Path.Combine(DataDirectory, "show.json"));
        _autosaver = new ShowAutosaver(Bus, _snapshots, TimeSpan.FromMilliseconds(500), () => _library.Load().Tracks);

        var document = _snapshots.LoadLatest();
        if (document is { } saved)
        {
            Submit(new RestoreShow(saved.Tracks, saved.Playlists, saved.ActiveId, saved.Queue, saved.MasterGainDb, saved.PanicFade));
        }

        if (_remoteOptions is { } options)
        {
            Remote = new RemoteHost(Bus, options, Monitor);
            await Remote.StartAsync(cancellationToken);
        }
    }

    private void MarshalEngineEvent(Action work) => _busRef?.Post(work);

    public void Submit(Command command) => Bus.Submit(new ClientId("app"), Interlocked.Increment(ref _seq), command);

    public async Task<ImmutableArray<Track>> ImportTracksAsync(IReadOnlyList<string> filePaths, IProgress<string>? progress = null)
    {
        if (_library is null || _importer is null || _waveforms is null)
        {
            throw new InvalidOperationException("AppHost is not started");
        }
        var (tracks, playlists) = _library.Load();
        var current = tracks;
        var changed = false;
        var result = ImmutableArray.CreateBuilder<Track>(filePaths.Count);
        foreach (var filePath in filePaths)
        {
            progress?.Report(Path.GetFileName(filePath));
            var imported = await Task.Run(() => _importer.Import(filePath));
            if (imported is null)
            {
                continue;
            }
            if (current.FirstOrDefault(t => t.FilePath == imported.Track.FilePath) is { } existing)
            {
                result.Add(existing);
                continue;
            }
            current = current.Add(imported.Track);
            changed = true;
            if (imported.Peaks is { } peaks)
            {
                _waveforms.Save(peaks);
            }
            _library.Upsert(current, playlists);
            result.Add(imported.Track);
        }
        if (changed)
        {
            SyncShowState(current);
        }
        return result.ToImmutable();
    }

    private void SyncShowState(ImmutableArray<Track> tracks)
    {
        var snapshot = Bus.Snapshot();
        Submit(new RestoreShow(
            tracks,
            snapshot.Show.Playlists,
            snapshot.Show.ActiveId,
            snapshot.Queue.Items,
            snapshot.Mixer.MasterGainDb,
            snapshot.Mixer.PanicFade));
    }

    public async ValueTask DisposeAsync()
    {
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
