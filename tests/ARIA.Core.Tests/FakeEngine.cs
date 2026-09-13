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

    public FakeStream? Stream(StreamHandle handle) => _streams.GetValueOrDefault(handle);

    public FakeStream? Last => Created.Count > 0 ? Created[^1] : null;

    public void End(StreamHandle handle, StreamEndReason reason) =>
        Events?.Invoke(new StreamEvent(handle, StreamEventKind.Ended, reason));

    public void Fault(StreamHandle handle, string detail = "decode-failure") =>
        Events?.Invoke(new StreamEvent(handle, StreamEventKind.Faulted, StreamEndReason.Faulted, detail));
}
