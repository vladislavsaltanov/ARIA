namespace Aria.Audio.Tests;

using System.Runtime.CompilerServices;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.Runtime;

public sealed class MiniaudioSinkTests
{
    private const int SampleRate = 8000;

    [Fact]
    public void MiniaudioSink_NullBackend_PlaysThrough()
    {
        using var sink = TryCreate(channels: 1, blockSizeFrames: 256);
        if (sink is null)
        {
            return;
        }
        var buffer = new float[256];
        double phase = 0;
        var deadline = Environment.TickCount64 + 1000;
        while (Environment.TickCount64 < deadline)
        {
            FillSine(buffer, ref phase);
            sink.Write(buffer);
        }
        Assert.InRange(sink.PlayedFrames, (long)(0.75 * SampleRate), (long)(1.25 * SampleRate));
    }

    [Fact]
    public void MasterGain_Zero_SilencesClockStillAdvances()
    {
        using var sink = TryCreate(channels: 1, blockSizeFrames: 256);
        if (sink is null)
        {
            return;
        }
        sink.SetMasterGain(0f);
        WriteFor(sink, 500);
        var before = sink.PlayedFrames;
        WriteFor(sink, 300);
        Assert.True(sink.PlayedFrames > before, $"clock stalled at {before}");
    }

    [Fact]
    public void Engine_WithMiniaudioSink_PumpsShow_AndClockAdvances()
    {
        using var sink = TryCreate(channels: 2, blockSizeFrames: 256);
        if (sink is null)
        {
            return;
        }
        var monitor = new PlaybackMonitor();
        using var engine = new AriaAudioEngine(
            new SyntheticSourceFactory(SampleRate, 2),
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

        var track = TestTracks.Track("sine.flac");
        var playlist = new Playlist(PlaylistId.New(), "Main", [new PlaylistEntry(EntryId.New(), track.Id)]);
        var client = new ClientId("miniaudio-e2e");
        bus.Submit(client, 1, new LoadShow([track], [playlist], playlist.Id));
        bus.Submit(client, 2, new Play());

        var deadline = Environment.TickCount64 + 10_000;
        while (sink.PlayedFrames < SampleRate)
        {
            if (Environment.TickCount64 > deadline)
            {
                Assert.Fail($"played frames never advanced past 1s, stopped at {sink.PlayedFrames}");
            }
            Thread.Sleep(10);
        }
        var atStop = sink.PlayedFrames;
        bus.Submit(client, 3, new Stop());
        Assert.True(atStop > 0);
    }

    private static MiniaudioSink? TryCreate(int channels, int blockSizeFrames)
    {
        try
        {
            return new MiniaudioSink(SampleRate, channels, blockSizeFrames, backend: 1);
        }
        catch (DllNotFoundException)
        {
            return null;
        }
    }

    private static void FillSine(float[] buffer, ref double phase)
    {
        for (var index = 0; index < buffer.Length; index++)
        {
            buffer[index] = (float)(0.5 * Math.Sin(2.0 * Math.PI * 440.0 * phase / SampleRate));
            phase++;
        }
    }

    private static void WriteFor(MiniaudioSink sink, int milliseconds)
    {
        var buffer = new float[256];
        double phase = 0;
        var frames = 0;
        var target = (long)milliseconds * SampleRate / 1000;
        while (frames < target)
        {
            FillSine(buffer, ref phase);
            sink.Write(buffer);
            frames += buffer.Length;
        }
    }
}
