namespace Aria.Audio.Tests;

using Aria.Core.Playback;

public sealed class MidStreamFaultTests
{
    [Fact]
    public async Task MidStreamFailure_EmitsFaulted_WithoutCompleted()
    {
        using var rig = new Rig();
        var handle = rig.Engine.StartStream(
            new TrackSource("/audio/dying.flac", TimeSpan.Zero, null),
            new StreamOptions(StreamBus.Main, []));
        rig.Engine.Transport(handle, TransportCommand.Play);

        var deadline = Environment.TickCount64 + 10_000;
        while (true)
        {
            lock (rig.Events)
            {
                if (rig.Events.Exists(e => e.Handle.Value == handle.Value && e.Kind == StreamEventKind.Faulted))
                {
                    break;
                }
            }
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException("mid-stream fault was never reported");
            }
            await Task.Delay(10);
        }

        lock (rig.Events)
        {
            Assert.DoesNotContain(
                rig.Events,
                e => e.Handle.Value == handle.Value && e.Kind == StreamEventKind.Ended && e.Reason == StreamEndReason.Completed);
        }
    }

    private sealed class Rig : IDisposable
    {
        public CapturingSink Main { get; } = new(8000, 2);

        public AriaAudioEngine Engine { get; }

        public List<StreamEvent> Events { get; } = [];

        public Rig()
        {
            Engine = new AriaAudioEngine(
                new SyntheticSourceFactory(8000, 2),
                null,
                sampleRate: 8000,
                channels: 2,
                blockSizeFrames: 128,
                sink: Main);
            Engine.Events += e =>
            {
                lock (Events)
                {
                    Events.Add(e);
                }
            };
        }

        public void Dispose() => Engine.Dispose();
    }
}
