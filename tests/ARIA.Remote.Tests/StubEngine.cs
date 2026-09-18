namespace Aria.Remote.Tests;

using System.Text;
using System.Text.Json;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.Runtime;

internal sealed class StubEngine : IAudioEngine
{
    private int _next;

    private int _sessionNext;

    private readonly Dictionary<int, PreviewTap> _sessions = new();

    public event Action<StreamEvent>? Events { add { } remove { } }

    public StreamHandle StartStream(TrackSource source, StreamOptions options) => new(++_next);

    public void Transport(StreamHandle handle, TransportCommand command)
    {
    }

    public void SetMix(StreamHandle handle, MixParameters mix)
    {
    }

    public void Seek(StreamHandle handle, TimeSpan position)
    {
    }

    public void SetMasterGain(double gainDb)
    {
    }

    public void SetSmoothing(Smoothing smoothing)
    {
    }

    public void Panic(PanicSpec spec)
    {
    }

    public void DisposeStream(StreamHandle handle)
    {
    }

    public PreviewSessionHandle OpenPreviewSession()
    {
        var handle = new PreviewSessionHandle(++_sessionNext);
        _sessions[handle.Value] = new PreviewTap(48000, 2);
        return handle;
    }

    public PreviewTap? PreviewSessionTap(PreviewSessionHandle session) =>
        _sessions.TryGetValue(session.Value, out var tap) ? tap : null;

    public void ClosePreviewSession(PreviewSessionHandle session) => _sessions.Remove(session.Value);

    public int OpenSessions => _sessions.Count;
}
