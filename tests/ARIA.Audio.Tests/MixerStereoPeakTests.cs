namespace Aria.Audio.Tests;

using System.Collections.Immutable;
using Aria.Core.Model;
using Aria.Core.Playback;

public sealed class MixerStereoPeakTests
{
    private sealed class StereoDcSource(float left, float right) : ISampleSource
    {
        public int Channels => 2;

        public int SampleRate => 48000;

        public int ReadFrames(Span<float> destination)
        {
            var frames = destination.Length / 2;
            for (var frame = 0; frame < frames; frame++)
            {
                destination[frame * 2] = left;
                destination[frame * 2 + 1] = right;
            }
            return frames;
        }

        public void Seek(long frameIndex)
        {
        }
    }

    private static VoiceConfig Config(ISampleSource source) =>
        new(source, 0.0, null, null, ImmutableArray<MarkerSpec>.Empty, TimeSpan.Zero, null);

    [Fact]
    public void Render_StereoSource_ReportsPerChannelPeaks()
    {
        using var bus = new MixerBus(2, 48000, 512);
        bus.AddVoice(Config(new StereoDcSource(0.8f, 0.3f)));
        var output = new float[512 * 2];

        bus.Render(output);

        Assert.Equal(0.8f, bus.PeakLeft, 3);
        Assert.Equal(0.3f, bus.PeakRight, 3);
        Assert.Equal(0.8f, bus.Peak, 3);
    }

    [Fact]
    public void Render_MonoSource_ReportsEqualChannelPeaks()
    {
        using var bus = new MixerBus(1, 48000, 512);
        bus.AddVoice(Config(new SineSource(1, 48000, 1000, 0.5)));
        var output = new float[512];

        bus.Render(output);

        Assert.Equal(bus.Peak, bus.PeakLeft, 3);
        Assert.Equal(bus.Peak, bus.PeakRight, 3);
    }
}
