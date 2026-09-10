namespace Aria.Audio.Tests;

using System.Runtime.CompilerServices;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.Runtime;

public sealed class DecoderTests : IDisposable
{
    private const int SampleRate = 8000;

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "aria-decoder-tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Wav_DecodesSine_EndOfStream()
    {
        var path = TestWav.WriteSine(_directory, "mono-8000.wav", SampleRate, 1, 1.0, 440.0, 0.5);
        var factory = new MiniaudioSourceFactory(SampleRate, 1);
        var source = factory.Open(path, TimeSpan.Zero, null);
        Assert.NotNull(source);
        using var scope = (IDisposable)source;

        var buffer = new float[1024];
        var total = 0;
        var peak = 0f;
        while (source.ReadFrames(buffer) is var read && read > 0)
        {
            total += read;
            peak = Math.Max(peak, Peak(buffer.AsSpan(0, read)));
        }
        Assert.InRange(total, 8000 - 32, 8000 + 32);
        Assert.InRange(peak, 0.5 - 0.01, 0.5 + 0.01);
        Assert.Equal(0, source.ReadFrames(buffer));
    }

    [Fact]
    public void Wav_StereoChannels_Expand()
    {
        var path = TestWav.WriteSine(_directory, "stereo-8000.wav", SampleRate, 2, 0.5, 440.0, 0.5);
        var factory = new MiniaudioSourceFactory(SampleRate, 2);
        var source = factory.Open(path, TimeSpan.Zero, null);
        Assert.NotNull(source);
        using var scope = (IDisposable)source;

        var buffer = new float[1024 * 2];
        var read = source.ReadFrames(buffer);
        Assert.True(read > 0, "stereo file produced no frames");
        for (var frame = 0; frame < read; frame++)
        {
            var left = buffer[frame * 2];
            var right = buffer[frame * 2 + 1];
            Assert.InRange(Math.Abs(left - right), 0.0, 0.01);
        }
    }

    [Fact]
    public void Wav_CueInCueOut_SlicesFile()
    {
        var path = TestWav.WriteSine(_directory, "sliced-8000.wav", SampleRate, 1, 1.0, 440.0, 0.5);
        var factory = new MiniaudioSourceFactory(SampleRate, 1);
        var source = factory.Open(path, TimeSpan.FromSeconds(0.25), TimeSpan.FromSeconds(0.75));
        Assert.NotNull(source);
        using var scope = (IDisposable)source;

        var buffer = new float[1024];
        var read = source.ReadFrames(buffer);
        Assert.True(read > 0, "sliced file produced no frames");
        var expected = Math.Sin(2.0 * Math.PI * 440.0 * 0.25);
        Assert.InRange(Math.Abs(buffer[0] - expected), 0.0, 0.05);

        var total = read;
        while (source.ReadFrames(buffer) is var next && next > 0)
        {
            total += next;
        }
        Assert.InRange(total, 4000 - 32, 4000 + 32);
        Assert.Equal(0, source.ReadFrames(buffer));
    }

    [Fact]
    public void Wav_CueInOnly_PlaysToEOS()
    {
        var path = TestWav.WriteSine(_directory, "cuein-8000.wav", SampleRate, 1, 1.0, 440.0, 0.5);
        var factory = new MiniaudioSourceFactory(SampleRate, 1);
        var source = factory.Open(path, TimeSpan.FromSeconds(0.5), null);
        Assert.NotNull(source);
        using var scope = (IDisposable)source;

        var buffer = new float[1024];
        var total = 0;
        while (source.ReadFrames(buffer) is var read && read > 0)
        {
            total += read;
        }
        Assert.InRange(total, 4000 - 32, 4000 + 32);
    }

    [Fact]
    public void Resample_Wav44k_To8k()
    {
        var path = TestWav.WriteSine(_directory, "mono-44100.wav", 44100, 1, 1.0, 440.0, 0.5);
        var factory = new MiniaudioSourceFactory(SampleRate, 1);
        var source = factory.Open(path, TimeSpan.Zero, null);
        Assert.NotNull(source);
        using var scope = (IDisposable)source;

        var buffer = new float[1024];
        var total = 0;
        var peak = 0f;
        while (source.ReadFrames(buffer) is var read && read > 0)
        {
            total += read;
            peak = Math.Max(peak, Peak(buffer.AsSpan(0, read)));
        }
        Assert.InRange(total, 8000 - 200, 8000 + 200);
        Assert.InRange(peak, 0.5 - 0.05, 0.5 + 0.05);
    }

    [Fact]
    public void Ogg_DecodesSine_ResampledToTarget()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "sine-44100.ogg");
        var factory = new MiniaudioSourceFactory(SampleRate, 1);
        var source = factory.Open(path, TimeSpan.Zero, null);
        Assert.NotNull(source);
        using var scope = (IDisposable)source;

        var buffer = new float[1024];
        var total = 0;
        var peak = 0f;
        while (source.ReadFrames(buffer) is var read && read > 0)
        {
            total += read;
            peak = Math.Max(peak, Peak(buffer.AsSpan(0, read)));
        }
        Assert.InRange(total, 8000 - 200, 8000 + 200);
        Assert.InRange(peak, 0.5 - 0.05, 0.5 + 0.05);
        Assert.Equal(0, source.ReadFrames(buffer));
    }

    [Fact]
    public void Ogg_CueInCueOut_SlicesFile()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "sine-44100.ogg");
        var factory = new MiniaudioSourceFactory(SampleRate, 1);
        var source = factory.Open(path, TimeSpan.FromSeconds(0.25), TimeSpan.FromSeconds(0.75));
        Assert.NotNull(source);
        using var scope = (IDisposable)source;

        var buffer = new float[1024];
        var total = 0;
        while (source.ReadFrames(buffer) is var read && read > 0)
        {
            total += read;
        }
        Assert.InRange(total, 4000 - 200, 4000 + 200);
        Assert.Equal(0, source.ReadFrames(buffer));
    }

    [Fact]
    public void Missing_File_OpenReturnsNull()
    {
        var factory = new MiniaudioSourceFactory(SampleRate, 1);
        var missing = Path.Combine(_directory, "does-not-exist.wav");
        Assert.Null(factory.Open(missing, TimeSpan.Zero, null));
    }

    [Fact]
    public async Task E2E_Decoded_WavThroughEngine()
    {
        var firstPath = TestWav.WriteSine(_directory, "e2e-first.wav", SampleRate, 2, 1.0, 440.0, 0.5);
        var secondPath = TestWav.WriteSine(_directory, "e2e-second.wav", SampleRate, 2, 5.0, 440.0, 0.5);
        var sink = TryCreateSink();
        if (sink is null)
        {
            return;
        }
        using var sinkScope = sink;

        var first = new Track(
            TrackId.New(),
            firstPath,
            "e2e-first.wav",
            TimeSpan.FromSeconds(1),
            new TrackDefaults(EndAction: EndAction.Advance));
        var second = new Track(
            TrackId.New(),
            secondPath,
            "e2e-second.wav",
            TimeSpan.FromSeconds(5),
            new TrackDefaults(EndAction: EndAction.Advance));

        var monitor = new PlaybackMonitor();
        using var engine = new AriaAudioEngine(
            new MiniaudioSourceFactory(SampleRate, 2),
            monitor,
            sampleRate: SampleRate,
            channels: 2,
            blockSizeFrames: 128,
            sink);
        var holder = new StrongBox<CommandBus>();
        Action<Action> marshal = work => holder.Value!.Post(work);
        var controller = new ShowController(engine, monitor, marshal);
        using var bus = new CommandBus(controller, BusMode.Pumped);
        holder.Value = bus;

        var client = new ClientId("decoder-e2e");
        long seq = 0;
        var playlist = new Playlist(
            PlaylistId.New(),
            "Main",
            [new PlaylistEntry(EntryId.New(), first.Id), new PlaylistEntry(EntryId.New(), second.Id)]);
        bus.Submit(client, ++seq, new LoadShow([first, second], [playlist], playlist.Id));
        bus.Submit(client, ++seq, new Play());

        await Poll(() => sink.PlayedFrames > SampleRate / 2, "sink never received decoded audio");
        await Poll(
            () => bus.Snapshot().Transport.Current?.TrackId == second.Id,
            "engine never advanced from first decoded track");
        bus.Submit(client, ++seq, new Stop());
    }

    private static MiniaudioSink? TryCreateSink()
    {
        try
        {
            return new MiniaudioSink(SampleRate, 2, 256, backend: 1);
        }
        catch (DllNotFoundException)
        {
            return null;
        }
    }

    private static float Peak(ReadOnlySpan<float> samples)
    {
        var peak = 0f;
        foreach (var sample in samples)
        {
            var magnitude = Math.Abs(sample);
            if (magnitude > peak)
            {
                peak = magnitude;
            }
        }
        return peak;
    }

    private static async Task Poll(Func<bool> condition, string message)
    {
        var deadline = Environment.TickCount64 + 10_000;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException(message);
            }
            await Task.Delay(10);
        }
    }
}
