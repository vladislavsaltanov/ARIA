namespace Aria.App.Tests;

using Aria.Core.Playback;
using Aria.Core.Runtime;

internal sealed class StubEngine : IAudioEngine
{
    private int _next;

    public event Action<StreamEvent>? Events;

    public void Raise(StreamEvent e) => Events?.Invoke(e);

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

    public void Panic(PanicSpec spec)
    {
    }

    public void DisposeStream(StreamHandle handle)
    {
    }
}
