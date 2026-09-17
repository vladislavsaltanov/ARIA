namespace Aria.Audio.Tests;

using Aria.Core.Model;
using Aria.Core.Playback;

public sealed class NormalizeResolveTests
{
    private const int SampleRate = 8000;
    private const int Channels = 2;

    [Fact]
    public async Task EnabledGlobalAndTrack_AppliesOffset()
    {
        using var rig = new Rig();
        var measured = rig.Engine.ScanTrackLufs("/audio/finite.flac");
        Assert.False(double.IsNaN(measured));
        rig.Engine.SetGlobalAudio(GlobalAudioSettings.Default with { NormalizeEnabled = true, NormalizeTargetLufs = measured - 6.0 });
        var audio = new TrackAudioSettings(0, 0, AudioEq.Flat, NormalizeEnabled: true, MeasuredLufs: measured);

        var streamHandle = rig.Engine.StartStream(new TrackSource("/audio/finite.flac", TimeSpan.Zero, null, audio), new StreamOptions(StreamBus.Main, []));
        rig.Engine.Transport(streamHandle, TransportCommand.Play);
        var peak = await PeakOf(rig);

        Assert.InRange(peak, 0.2, 0.3);
    }

    [Fact]
    public async Task TrackTargetOverride_WinsOverGlobal()
    {
        using var rig = new Rig();
        var measured = rig.Engine.ScanTrackLufs("/audio/finite.flac");
        Assert.False(double.IsNaN(measured));
        rig.Engine.SetGlobalAudio(GlobalAudioSettings.Default with { NormalizeEnabled = true, NormalizeTargetLufs = measured - 6.0 });
        var audio = new TrackAudioSettings(0, 0, AudioEq.Flat, NormalizeEnabled: true, MeasuredLufs: measured, NormalizeTargetLufs: measured - 12.0);

        var streamHandle = rig.Engine.StartStream(new TrackSource("/audio/finite.flac", TimeSpan.Zero, null, audio), new StreamOptions(StreamBus.Main, []));
        rig.Engine.Transport(streamHandle, TransportCommand.Play);
        var peak = await PeakOf(rig);

        Assert.InRange(peak, 0.08, 0.18);
    }

    [Fact]
    public async Task DisabledGlobal_IgnoresOffset()
    {
        using var rig = new Rig();
        var audio = new TrackAudioSettings(0, 0, AudioEq.Flat, NormalizeEnabled: true, MeasuredLufs: -6.0);

        var streamHandle = rig.Engine.StartStream(new TrackSource("/audio/finite.flac", TimeSpan.Zero, null, audio), new StreamOptions(StreamBus.Main, []));
        rig.Engine.Transport(streamHandle, TransportCommand.Play);
        var peak = await PeakOf(rig);

        Assert.InRange(peak, 0.4, 0.6);
    }

    [Fact]
    public async Task DisabledTrack_IgnoresOffset()
    {
        using var rig = new Rig();
        rig.Engine.SetGlobalAudio(GlobalAudioSettings.Default with { NormalizeEnabled = true, NormalizeTargetLufs = -30.0 });
        var audio = new TrackAudioSettings(0, 0, AudioEq.Flat, MeasuredLufs: -6.0);

        var streamHandle = rig.Engine.StartStream(new TrackSource("/audio/finite.flac", TimeSpan.Zero, null, audio), new StreamOptions(StreamBus.Main, []));
        rig.Engine.Transport(streamHandle, TransportCommand.Play);
        var peak = await PeakOf(rig);

        Assert.InRange(peak, 0.4, 0.6);
    }

    [Fact]
    public async Task MissingMeasurement_IgnoresOffset()
    {
        using var rig = new Rig();
        rig.Engine.SetGlobalAudio(GlobalAudioSettings.Default with { NormalizeEnabled = true, NormalizeTargetLufs = -30.0 });
        var audio = new TrackAudioSettings(0, 0, AudioEq.Flat, NormalizeEnabled: true);

        var streamHandle = rig.Engine.StartStream(new TrackSource("/audio/finite.flac", TimeSpan.Zero, null, audio), new StreamOptions(StreamBus.Main, []));
        rig.Engine.Transport(streamHandle, TransportCommand.Play);
        var peak = await PeakOf(rig);

        Assert.InRange(peak, 0.4, 0.6);
    }

    private static async Task<float> PeakOf(Rig rig)
    {
        var deadline = Environment.TickCount64 + 10_000;
        while (TotalPeak(rig.Main) < 0.1)
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException("main sink never received audio");
            }
            await Task.Delay(10);
        }
        await Task.Delay(200);
        return TotalPeak(rig.Main);
    }

    private static float TotalPeak(CapturingSink sink)
    {
        var peak = 0f;
        foreach (var block in sink.Blocks)
        {
            foreach (var sample in block)
            {
                var magnitude = Math.Abs(sample);
                if (magnitude > peak)
                {
                    peak = magnitude;
                }
            }
        }
        return peak;
    }

    private sealed class Rig : IDisposable
    {
        public CapturingSink Main { get; } = new(SampleRate, Channels);

        public AriaAudioEngine Engine { get; }

        public Rig()
        {
            Engine = new AriaAudioEngine(
                new SyntheticSourceFactory(SampleRate, Channels),
                null,
                sampleRate: SampleRate,
                channels: Channels,
                blockSizeFrames: 128,
                sink: Main);
        }

        public void Dispose() => Engine.Dispose();
    }
}
