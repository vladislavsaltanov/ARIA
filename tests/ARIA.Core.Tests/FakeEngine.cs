namespace Aria.Core.Tests;

using Aria.Core.Model;
using Aria.Core.Playback;

public sealed class FakeEngine : IAudioEngine
{
    public sealed class FakeStream(StreamHandle handle, TrackSource source, StreamOptions options)
    {
        public StreamHandle Handle { get; } = handle;
        public TrackSource Source { get; } = source;
        public StreamOptions Options { get; } = options;
        public List<TransportCommand> Transports { get; } = [];
        public List<MixParameters> Mixes { get; } = [];
        public List<TimeSpan> Seeks { get; } = [];
    }

    private int _next;
    private readonly Dictionary<StreamHandle, FakeStream> _streams = [];

    public List<FakeStream> Created { get; } = [];
    public List<StreamHandle> Disposed { get; } = [];
    public List<double> MasterGains { get; } = [];
    public List<PanicSpec> Panics { get; } = [];
    public List<Smoothing> Smoothings { get; } = [];
    public List<(TrackSource Source, StreamOptions Options)> PreviewStarts { get; } = [];
    public int PreviewStops { get; private set; }
    public List<double> PreviewGains { get; } = [];
    public List<bool> PreviewMutes { get; } = [];
    public List<GlobalAudioSettings> GlobalAudios { get; } = [];
    public List<(StreamHandle Handle, TrackAudioSettings Audio)> VoiceAudios { get; } = [];
    public Func<string, double> ScanLufs { get; set; } = _ => double.NaN;
    public List<string> ScannedPaths { get; } = [];
    public List<(PreviewSessionHandle Session, TrackSource Source)> SessionTracks { get; } = [];
    public List<(PreviewSessionHandle Session, string Name)> SessionNames { get; } = [];
    public List<(PreviewSessionHandle Session, double GainDb)> SessionGains { get; } = [];
    public List<PreviewSessionHandle> ClosedSessions { get; } = [];
    private readonly Dictionary<int, SessionProfile> _profiles = [];

    public double ScanTrackLufs(string filePath)
    {
        ScannedPaths.Add(filePath);
        return ScanLufs(filePath);
    }

    public event Action<StreamEvent>? Events;

    public StreamHandle StartStream(TrackSource source, StreamOptions options)
    {
        var handle = new StreamHandle(++_next);
        var stream = new FakeStream(handle, source, options);
        _streams[handle] = stream;
        Created.Add(stream);
        return handle;
    }

    public void Transport(StreamHandle handle, TransportCommand command)
    {
        if (_streams.TryGetValue(handle, out var stream))
        {
            stream.Transports.Add(command);
        }
    }

    public void SetMix(StreamHandle handle, MixParameters mix)
    {
        if (_streams.TryGetValue(handle, out var stream))
        {
            stream.Mixes.Add(mix);
        }
    }

    public void Seek(StreamHandle handle, TimeSpan position)
    {
        if (_streams.TryGetValue(handle, out var stream))
        {
            stream.Seeks.Add(position);
        }
    }

    public void SetMasterGain(double gainDb) => MasterGains.Add(gainDb);

    public void SetSmoothing(Smoothing smoothing) => Smoothings.Add(smoothing);

    public void Panic(PanicSpec spec) => Panics.Add(spec);

    public void DisposeStream(StreamHandle handle)
    {
        if (_streams.Remove(handle))
        {
            Disposed.Add(handle);
        }
    }

    public StreamHandle StartPreview(TrackSource source, StreamOptions options)
    {
        var handle = new StreamHandle(++_next);
        var stream = new FakeStream(handle, source, options);
        _streams[handle] = stream;
        PreviewStarts.Add((source, options));
        return handle;
    }

    public void StopPreview() => PreviewStops++;

    public void SetPreviewGain(double gainDb) => PreviewGains.Add(gainDb);

    public void SetPreviewMuted(bool muted) => PreviewMutes.Add(muted);

    public void SetVoiceAudio(StreamHandle handle, TrackAudioSettings audio) => VoiceAudios.Add((handle, audio));

    public void StartSessionTrack(PreviewSessionHandle session, TrackSource source) => SessionTracks.Add((session, source));

    public void RenameSession(PreviewSessionHandle session, string name)
    {
        SessionNames.Add((session, name));
        var gain = _profiles.TryGetValue(session.Value, out var renamed) ? renamed.BackingGainDb : 0.0;
        var click = _profiles.TryGetValue(session.Value, out var current) ? current.ClickGainDb : 0.0;
        _profiles[session.Value] = new SessionProfile(session, name, gain, false, click);
    }

    public void SetSessionBackingGain(PreviewSessionHandle session, double gainDb)
    {
        SessionGains.Add((session, gainDb));
        var name = _profiles.TryGetValue(session.Value, out var existing) ? existing.Name : $"Session {session.Value}";
        var click = _profiles.TryGetValue(session.Value, out var current) ? current.ClickGainDb : 0.0;
        _profiles[session.Value] = new SessionProfile(session, name, gainDb, false, click);
    }

    public List<(PreviewSessionHandle Session, double GainDb)> SessionClickGains { get; } = [];

    public List<bool> SessionFollows { get; } = [];

    public void SetSessionFollow(bool follow) => SessionFollows.Add(follow);

    public void SetSessionClickGain(PreviewSessionHandle session, double gainDb)
    {
        SessionClickGains.Add((session, gainDb));
        var name = _profiles.TryGetValue(session.Value, out var existing) ? existing.Name : $"Session {session.Value}";
        var backing = _profiles.TryGetValue(session.Value, out var current) ? current.BackingGainDb : 0.0;
        _profiles[session.Value] = new SessionProfile(session, name, backing, false, gainDb);
    }

    public void ClosePreviewSession(PreviewSessionHandle session)
    {
        ClosedSessions.Add(session);
        _profiles.Remove(session.Value);
    }

    public IReadOnlyList<SessionProfile> ListSessions() => [.. _profiles.Values];

    public void SetGlobalAudio(GlobalAudioSettings audio) => GlobalAudios.Add(audio);

    public FakeStream? Stream(StreamHandle handle) => _streams.GetValueOrDefault(handle);

    public FakeStream? Last => Created.Count > 0 ? Created[^1] : null;

    public void End(StreamHandle handle, StreamEndReason reason) =>
        Events?.Invoke(new StreamEvent(handle, StreamEventKind.Ended, reason));

    public void Fault(StreamHandle handle, string detail = "decode-failure") =>
        Events?.Invoke(new StreamEvent(handle, StreamEventKind.Faulted, StreamEndReason.Faulted, detail));
}
