namespace Aria.App;

using Aria.Audio;
using Aria.Core.Commands;
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
    private JsonSnapshotStore? _snapshots;
    private ShowAutosaver? _autosaver;
    private AriaAudioEngine? _engine;
    private CommandBus? _busRef;
    private long _seq;
    private bool _started;

    public PlaybackMonitor Monitor { get; } = new();

    public ICommandBus Bus { get; private set; } = null!;

    public RemoteHost? Remote { get; private set; }

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
        _engine = new AriaAudioEngine(factory, Monitor, SampleRate, Channels, BlockSizeFrames, sink);

        var controller = new ShowController(_engine, Monitor, MarshalEngineEvent);

        Bus = _ownedBus = new CommandBus(controller, BusMode.Pumped);
        _busRef = _ownedBus;

        _library = new SqliteLibraryStore(Path.Combine(DataDirectory, "library.db"));
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
    }
}
