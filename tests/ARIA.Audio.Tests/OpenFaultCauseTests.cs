namespace Aria.Audio.Tests;

using Aria.Core.Playback;

public sealed class OpenFaultCauseTests
{
    [Fact]
    public async Task MissingFile_EmitsFaultedWithMissingDetail()
    {
        using var rig = new Rig(new CauseFactory(SourceOpenFault.Missing));
        rig.Engine.StartStream(new TrackSource("/audio/gone.flac", TimeSpan.Zero, null), MainOptions());

        var faulted = await PollFault(rig, "missing open fault was never reported");
        Assert.Equal("Missing", faulted.Detail);
    }

    [Fact]
    public async Task UndecodableFile_EmitsFaultedWithUndecodableDetail()
    {
        using var rig = new Rig(new CauseFactory(SourceOpenFault.Undecodable));
        rig.Engine.StartStream(new TrackSource("/audio/corrupt.flac", TimeSpan.Zero, null), MainOptions());

        var faulted = await PollFault(rig, "undecodable open fault was never reported");
        Assert.Equal("Undecodable", faulted.Detail);
    }

    [Fact]
    public void MiniaudioFactory_MapsAbsentPathToMissing()
    {
        var factory = new MiniaudioSourceFactory(8000, 2);

        var opened = factory.TryOpen("/nonexistent-aria-xyz.flac", TimeSpan.Zero, null, out var source, out var fault);

        Assert.False(opened);
        Assert.Null(source);
        Assert.Equal(SourceOpenFault.Missing, fault);
    }

    private static StreamOptions MainOptions() => new(StreamBus.Main, []);

    private static async Task<StreamEvent> PollFault(Rig rig, string message)
    {
        var deadline = Environment.TickCount64 + 10_000;
        while (true)
        {
            lock (rig.Events)
            {
                var faulted = rig.Events.Find(e => e.Kind == StreamEventKind.Faulted);
                if (faulted is not null)
                {
                    return faulted;
                }
            }
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException(message);
            }
            await Task.Delay(10);
        }
    }

    private sealed class CauseFactory(SourceOpenFault fault) : ISourceFactory
    {
        public ISampleSource? Open(string filePath, TimeSpan cueIn, TimeSpan? cueOut) => null;

        public bool TryOpen(string filePath, TimeSpan cueIn, TimeSpan? cueOut, out ISampleSource? source, out SourceOpenFault cause)
        {
            source = null;
            cause = fault;
            return false;
        }
    }

    private sealed class Rig : IDisposable
    {
        public AriaAudioEngine Engine { get; }

        public List<StreamEvent> Events { get; } = [];

        public Rig(ISourceFactory factory)
        {
            Engine = new(factory, null, sampleRate: 8000, channels: 2, blockSizeFrames: 128, sink: new CapturingSink(8000, 2));
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
