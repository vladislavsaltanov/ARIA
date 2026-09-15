namespace Aria.Audio.Tests;

using System.Collections.Immutable;
using Aria.Core.Model;
using Aria.Core.Playback;

public sealed class EngineWireTests
{
    private const int SampleRate = 48000;
    private const int Channels = 2;
    private const int Block = 256;

    [Fact]
    public void VoiceEq_BoostsBand()
    {
        using var mixer = new MixerBus(Channels, SampleRate, Block);
        mixer.AddVoice(new VoiceConfig(
            new ToneSource(400.0, 0.5, Channels, SampleRate),
            0.0, null, null, [], TimeSpan.Zero, null,
            VoiceAudio(eq: EqWith(2, 12f))));
        var buffer = new float[SampleRate * Channels];
        mixer.Render(buffer);

        var ratio = RmsTail(buffer) / (0.5 / Math.Sqrt(2));
        Assert.True(ratio > 3.0, $"Boost ratio {ratio} is not above 3.");
    }

    [Fact]
    public void VoiceEq_Flat_IsTransparent()
    {
        using var mixer = new MixerBus(Channels, SampleRate, Block);
        mixer.AddVoice(new VoiceConfig(
            new ToneSource(400.0, 0.5, Channels, SampleRate),
            0.0, null, null, [], TimeSpan.Zero, null,
            VoiceAudio()));
        var buffer = new float[SampleRate * Channels];
        mixer.Render(buffer);

        var ratio = RmsTail(buffer) / (0.5 / Math.Sqrt(2));
        Assert.InRange(ratio, 0.99, 1.01);
    }

    [Fact]
    public void VoicePan_HardLeft_SilencesRight()
    {
        using var mixer = new MixerBus(Channels, SampleRate, Block);
        mixer.AddVoice(new VoiceConfig(
            new ToneSource(400.0, 0.5, Channels, SampleRate),
            0.0, null, null, [], TimeSpan.Zero, null,
            VoiceAudio(pan: -1)));
        var buffer = new float[SampleRate * Channels];
        mixer.Render(buffer);

        Assert.InRange(ChannelPeak(buffer, 0), 0.4, 0.6);
        Assert.Equal(0f, ChannelPeak(buffer, 1));
    }

    [Fact]
    public void SetVoiceAudio_SwapsEq()
    {
        using var mixer = new MixerBus(Channels, SampleRate, Block);
        var handle = mixer.AddVoice(new VoiceConfig(
            new ToneSource(400.0, 0.5, Channels, SampleRate),
            0.0, null, null, [], TimeSpan.Zero, null,
            VoiceAudio()));
        mixer.Render(new float[SampleRate * Channels]);

        mixer.SetVoiceAudio(handle, VoiceAudio(eq: EqWith(2, 12f)));
        var buffer = new float[SampleRate * Channels];
        mixer.Render(buffer);

        var ratio = RmsTail(buffer) / (0.5 / Math.Sqrt(2));
        Assert.True(ratio > 3.0, $"Boost ratio {ratio} is not above 3.");
    }

    [Fact]
    public async Task MasterLimiter_ClampsHotMix()
    {
        using var rig = new Rig();
        var handle = rig.Engine.StartStream(
            new TrackSource("/audio/sine.flac", TimeSpan.Zero, null),
            new StreamOptions(StreamBus.Main, []));
        rig.Engine.Transport(handle, TransportCommand.Play);
        rig.Engine.SetMasterGain(12.0);

        var fromFrame = rig.Main.TotalFrames;
        await Poll(() => rig.Main.TotalFrames > fromFrame + SampleRate, "main sink stalled");

        Assert.True(PeakFrom(rig.Main, fromFrame + SampleRate / 4) < 0.95, "limiter did not clamp hot mix");
    }

    [Fact]
    public async Task MasterLimiter_Disabled_PassesHotMix()
    {
        using var rig = new Rig();
        var handle = rig.Engine.StartStream(
            new TrackSource("/audio/sine.flac", TimeSpan.Zero, null),
            new StreamOptions(StreamBus.Main, []));
        rig.Engine.Transport(handle, TransportCommand.Play);
        rig.Engine.SetGlobalAudio(GlobalAudioSettings.Default with
        {
            Limiter = new LimiterSettings(false, -1.0, 100.0),
        });
        rig.Engine.SetMasterGain(12.0);

        var fromFrame = rig.Main.TotalFrames;
        await Poll(() => rig.Main.TotalFrames > fromFrame + SampleRate, "main sink stalled");

        Assert.True(PeakFrom(rig.Main, fromFrame) > 1.5, "unlimited hot mix was clamped");
    }

    [Fact]
    public async Task MasterHpf_BlocksDc()
    {
        using var rig = new Rig();
        var handle = rig.Engine.StartStream(
            new TrackSource("/audio/dc.flac", TimeSpan.Zero, null),
            new StreamOptions(StreamBus.Main, []));
        rig.Engine.Transport(handle, TransportCommand.Play);
        await Poll(() => PeakFrom(rig.Main, 0) > 0.4, "dc source never reached output");

        rig.Engine.SetGlobalAudio(GlobalAudioSettings.Default with { HpfHz = 400 });
        var fromFrame = rig.Main.TotalFrames;
        await Poll(() => rig.Main.TotalFrames > fromFrame + SampleRate, "main sink stalled");

        Assert.True(PeakFrom(rig.Main, fromFrame + SampleRate / 2) < 0.05, "hpf did not block dc");
    }

    [Fact]
    public async Task MasterMono_SumsChannels()
    {
        using var rig = new Rig();
        var handle = rig.Engine.StartStream(
            new TrackSource("/audio/sine.flac", TimeSpan.Zero, null, VoiceAudio(pan: -1)),
            new StreamOptions(StreamBus.Main, []));
        rig.Engine.Transport(handle, TransportCommand.Play);
        await Poll(() => PeakFrom(rig.Main, 0) > 0.4, "main sink never received audio");
        await Task.Delay(200);
        Assert.Equal(0f, ChannelPeakFrom(rig.Main, 1, 0));

        rig.Engine.SetGlobalAudio(GlobalAudioSettings.Default with { Mono = true });
        var fromFrame = rig.Main.TotalFrames;
        await Poll(() => rig.Main.TotalFrames > fromFrame + SampleRate, "main sink stalled");
        var measureFrom = rig.Main.TotalFrames;
        await Poll(() => rig.Main.TotalFrames > measureFrom + SampleRate / 2, "main sink stalled");

        Assert.InRange(ChannelPeakFrom(rig.Main, 0, measureFrom), 0.2, 0.3);
        Assert.InRange(ChannelPeakFrom(rig.Main, 1, measureFrom), 0.2, 0.3);
    }

    [Fact]
    public async Task Preview_SkipsMasterLimiter()
    {
        using var rig = new Rig();
        rig.Engine.StartPreview(
            new TrackSource("/audio/sine.flac", TimeSpan.Zero, null),
            new StreamOptions(StreamBus.Preview, []));
        rig.Engine.SetPreviewGain(12.0);

        var fromFrame = rig.Preview.TotalFrames;
        await Poll(() => rig.Preview.TotalFrames > fromFrame + SampleRate, "preview sink stalled");

        Assert.True(PeakFrom(rig.Preview, fromFrame) > 1.5, "preview was limited");
        Assert.Equal(0f, PeakFrom(rig.Main, 0));
    }

    [Fact]
    public async Task PreviewVoice_AppliesTrackEq()
    {
        using var rig = new Rig();
        rig.Engine.StartPreview(
            new TrackSource("/audio/sine.flac", TimeSpan.Zero, null, VoiceAudio(eq: EqWith(2, 12f))),
            new StreamOptions(StreamBus.Preview, []));
        await Poll(() => PeakFrom(rig.Preview, 0) > 0.9, "preview eq boost never reached output");

        Assert.True(PeakFrom(rig.Preview, 0) > 0.9, "preview voice ignored track eq");
    }

    [Fact]
    public async Task SetGlobalAudio_SwapsMasterEq()
    {
        using var rig = new Rig();
        var handle = rig.Engine.StartStream(
            new TrackSource("/audio/sine.flac", TimeSpan.Zero, null),
            new StreamOptions(StreamBus.Main, []));
        rig.Engine.Transport(handle, TransportCommand.Play);
        await Poll(() => PeakFrom(rig.Main, 0) > 0.4, "main sink never received audio");

        rig.Engine.SetGlobalAudio(GlobalAudioSettings.Default with { Eq = EqWith(2, 12f) });
        var fromFrame = rig.Main.TotalFrames;
        await Poll(() => rig.Main.TotalFrames > fromFrame + SampleRate, "main sink stalled");

        Assert.True(PeakFrom(rig.Main, fromFrame) > 0.9, "master eq boost never reached output");
    }

    [Fact]
    public async Task PreviewTap_ReceivesPreviewAudio()
    {
        using var rig = new Rig();
        rig.Engine.StartPreview(
            new TrackSource("/audio/sine.flac", TimeSpan.Zero, null),
            new StreamOptions(StreamBus.Preview, []));

        await Poll(() => rig.Tap.Count > 4096, "preview tap never received audio");

        var buffer = new float[4096];
        var read = rig.Tap.Read(buffer);
        Assert.True(read > 0, "tap read returned nothing");
        var peak = 0f;
        for (var index = 0; index < read; index++)
        {
            var magnitude = Math.Abs(buffer[index]);
            if (magnitude > peak)
            {
                peak = magnitude;
            }
        }
        Assert.True(peak > 0.1, "tap audio is silent");
    }

    [Fact]
    public async Task PreviewTap_Full_DoesNotBlockPreview()
    {
        using var rig = new Rig(tapFrames: 256);
        rig.Engine.StartPreview(
            new TrackSource("/audio/sine.flac", TimeSpan.Zero, null),
            new StreamOptions(StreamBus.Preview, []));

        await Poll(() => rig.Preview.TotalFrames > SampleRate, "preview stalled with full tap");
    }

    private static TrackAudioSettings VoiceAudio(double gainDb = 0, double pan = 0, AudioEq? eq = null)
        => new(gainDb, pan, eq ?? AudioEq.Flat);

    private static AudioEq EqWith(int band, float gainDb, float q = 1f)
        => new([.. AudioEq.DefaultFrequencies.Select((f, i) => new EqBand(f, i == band ? gainDb : 0f, q))]);

    private static double RmsTail(float[] buffer)
    {
        var tail = buffer.AsSpan(buffer.Length - SampleRate / 2 * Channels);
        double sum = 0;
        foreach (var sample in tail)
        {
            sum += sample * sample;
        }
        return Math.Sqrt(sum / tail.Length);
    }

    private static float ChannelPeak(float[] buffer, int channel)
    {
        var peak = 0f;
        for (var index = channel; index < buffer.Length; index += Channels)
        {
            var magnitude = Math.Abs(buffer[index]);
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

    private static float PeakFrom(CapturingSink sink, int fromFrame)
    {
        var peak = 0f;
        var cursor = 0;
        foreach (var block in sink.Blocks)
        {
            var frames = block.Length / sink.Channels;
            if (cursor + frames > fromFrame)
            {
                var skip = Math.Max(0, fromFrame - cursor);
                for (var index = skip * sink.Channels; index < block.Length; index++)
                {
                    var magnitude = Math.Abs(block[index]);
                    if (magnitude > peak)
                    {
                        peak = magnitude;
                    }
                }
            }
            cursor += frames;
        }
        return peak;
    }

    private static float ChannelPeakFrom(CapturingSink sink, int channel, int fromFrame)
    {
        var peak = 0f;
        var cursor = 0;
        foreach (var block in sink.Blocks)
        {
            var frames = block.Length / sink.Channels;
            if (cursor + frames > fromFrame)
            {
                var skip = Math.Max(0, fromFrame - cursor);
                for (var frame = skip; frame < frames; frame++)
                {
                    var magnitude = Math.Abs(block[frame * sink.Channels + channel]);
                    if (magnitude > peak)
                    {
                        peak = magnitude;
                    }
                }
            }
            cursor += frames;
        }
        return peak;
    }

    private sealed class ToneSource : ISampleSource
    {
        private readonly double _frequency;
        private readonly double _amplitude;
        private double _phase;

        public ToneSource(double frequency, double amplitude, int channels, int sampleRate)
        {
            _frequency = frequency;
            _amplitude = amplitude;
            Channels = channels;
            SampleRate = sampleRate;
        }

        public int Channels { get; }

        public int SampleRate { get; }

        public int ReadFrames(Span<float> destination)
        {
            var frames = destination.Length / Channels;
            for (var frame = 0; frame < frames; frame++)
            {
                var sample = (float)(_amplitude * Math.Sin(2.0 * Math.PI * _frequency * _phase / SampleRate));
                _phase++;
                for (var channel = 0; channel < Channels; channel++)
                {
                    destination[frame * Channels + channel] = sample;
                }
            }
            return frames;
        }

        public void Seek(long frameIndex) => _phase = frameIndex;
    }

    private sealed class DcSource : ISampleSource
    {
        private readonly double _value;

        public DcSource(double value, int channels, int sampleRate)
        {
            _value = value;
            Channels = channels;
            SampleRate = sampleRate;
        }

        public int Channels { get; }

        public int SampleRate { get; }

        public int ReadFrames(Span<float> destination)
        {
            destination.Fill((float)_value);
            return destination.Length / Channels;
        }

        public void Seek(long frameIndex)
        {
        }
    }

    private sealed class WireFactory : ISourceFactory
    {
        private readonly int _sampleRate;

        public WireFactory(int sampleRate) => _sampleRate = sampleRate;

        public ISampleSource? Open(string filePath, TimeSpan cueIn, TimeSpan? cueOut)
            => Path.GetFileName(filePath) switch
            {
                "sine.flac" => new ToneSource(440.0, 0.5, Channels, _sampleRate),
                "dc.flac" => new DcSource(0.5, Channels, _sampleRate),
                _ => null,
            };
    }

    private sealed class Rig : IDisposable
    {
        public CapturingSink Main { get; } = new(SampleRate, Channels);

        public CapturingSink Preview { get; } = new(SampleRate, Channels);

        public SampleRing Tap { get; }

        public AriaAudioEngine Engine { get; }

        public Rig(int tapFrames = 0)
        {
            Tap = new SampleRing(Math.Max(tapFrames, SampleRate * 2), Channels);
            Engine = new AriaAudioEngine(
                new WireFactory(SampleRate),
                null,
                SampleRate,
                Channels,
                Block,
                Main,
                null,
                Preview,
                Tap);
        }

        public void Dispose() => Engine.Dispose();
    }
}
