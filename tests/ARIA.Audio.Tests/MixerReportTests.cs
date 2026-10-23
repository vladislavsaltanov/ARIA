namespace Aria.Audio.Tests;

using System.Collections.Immutable;
using Aria.Core.Model;
using Aria.Core.Playback;

public sealed class MixerReportTests
{
    private const int Block = 512;

    [Fact]
    public void Report_ActiveVoice_TagAndFrame()
    {
        using var bus = new MixerBus(1, 48000, Block);
        var output = new float[Block];
        bus.AddVoice(Config(new SineSource(1, 48000, 1000, 0.5), tag: 7));

        bus.Render(output);
        bus.Render(output);
        bus.Render(output);

        var report = Assert.Single(bus.LastReport.ToArray());
        Assert.Equal(7, report.Tag);
        Assert.Equal(1024, report.SourceFrame);
        Assert.True(report.Active);
        Assert.False(report.Ended);
        Assert.Equal(0, report.Epoch);
        Assert.False(bus.LastReportOverflow);
    }

    [Fact]
    public void Report_Seek_IncrementsEpoch_AndJumpsFrame()
    {
        using var bus = new MixerBus(1, 48000, Block);
        var output = new float[Block];
        var handle = bus.AddVoice(Config(new SineSource(1, 48000, 1000, 0.5)));
        bus.Render(output);
        bus.Render(output);

        bus.Seek(handle, 48000);
        bus.Render(output);

        var report = Assert.Single(bus.LastReport.ToArray());
        Assert.Equal(48000, report.SourceFrame);
        Assert.Equal(1, report.Epoch);
    }

    [Fact]
    public void Report_PauseWithSmoothing_FadeThenInactive()
    {
        using var bus = new MixerBus(1, 48000, Block);
        bus.SetSmoothing(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10), true);
        var output = new float[Block];
        var handle = bus.AddVoice(Config(new SineSource(1, 48000, 1000, 0.5)));
        bus.Render(output);

        bus.Transport(handle, TransportCommand.Pause);
        bus.Render(output);

        var fading = Assert.Single(bus.LastReport.ToArray());
        Assert.True(fading.Active);
        Assert.True(fading.PauseWhenFaded);
        Assert.Equal(0.0, fading.FadeTo);
        Assert.Equal(480, fading.FadeFrames);
        Assert.Equal(0.0, fading.FadeProgress);
        Assert.Equal(1, fading.Epoch);

        bus.Render(output);
        bus.Render(output);

        Assert.False(Assert.Single(bus.LastReport.ToArray()).Active);
    }

    [Fact]
    public void Report_Crossfade_OldVoiceStopWhenDone()
    {
        using var bus = new MixerBus(1, 48000, Block);
        var output = new float[Block];
        var first = bus.AddVoice(Config(new SineSource(1, 48000, 440, 0.5), tag: 1));
        bus.AddVoice(Config(new SineSource(1, 48000, 880, 0.3), tag: 2));
        bus.SetMix(first, new MixParameters(0.0, new FadeSpec(TimeSpan.FromSeconds(1), FadeCurve.Exponential, -80.0, true)));

        bus.Render(output);

        var reports = bus.LastReport.ToArray();
        var old = Assert.Single(reports, r => r.Tag == 1);
        var fresh = Assert.Single(reports, r => r.Tag == 2);
        Assert.True(old.StopWhenDone);
        Assert.Equal(FadeCurve.Exponential, old.FadeCurve);
        Assert.Equal(48000, old.FadeFrames);
        Assert.InRange(old.FadeTo, 0.0, 0.0002);
        Assert.False(fresh.StopWhenDone);
    }

    [Fact]
    public void Report_MarkerAutostop_EndedAtMarkerFrame()
    {
        var markers = ImmutableArray.Create(new MarkerSpec("half", TimeSpan.FromSeconds(0.5), MarkerAction.Pause));
        var ended = RenderUntilEnded(Config(new SineSource(1, 48000, 1000, 0.5), markers: markers));

        Assert.Equal(24000, ended.EndSourceFrame);
    }

    [Fact]
    public void Report_CueOut_EndedAtCueOut()
    {
        var ended = RenderUntilEnded(Config(new SineSource(1, 48000, 1000, 0.5), cueOut: TimeSpan.FromSeconds(0.25)));

        Assert.Equal(12000, ended.EndSourceFrame);
    }

    [Fact]
    public void Report_NaturalEnd_EndedAtLastFrame()
    {
        var ended = RenderUntilEnded(Config(new FiniteSine(1200)));

        Assert.Equal(1200, ended.EndSourceFrame);
    }

    [Fact]
    public void Report_StopCommand_EndedAtStartFrame()
    {
        using var bus = new MixerBus(1, 48000, Block);
        var output = new float[Block];
        var handle = bus.AddVoice(Config(new SineSource(1, 48000, 1000, 0.5)));
        bus.Render(output);
        bus.Render(output);

        bus.Transport(handle, TransportCommand.Stop);
        bus.Render(output);

        var report = Assert.Single(bus.LastReport.ToArray());
        Assert.True(report.Ended);
        Assert.Equal(1024, report.EndSourceFrame);
    }

    [Fact]
    public void Report_Overflow_Over16Voices_Flagged()
    {
        using var bus = new MixerBus(1, 48000, Block);
        var output = new float[Block];
        for (var voice = 0; voice < MixerBus.MaxReportVoices + 1; voice++)
        {
            bus.AddVoice(Config(new SineSource(1, 48000, 100 + voice, 0.01), tag: voice + 1));
        }

        bus.Render(output);

        Assert.Equal(MixerBus.MaxReportVoices, bus.LastReport.Length);
        Assert.True(bus.LastReportOverflow);
    }

    [Fact]
    public void Render_WithReport_AllocatesNothing()
    {
        using var bus = new MixerBus(2, 48000, 256);
        bus.AddVoice(Config(new SineSource(2, 48000, 440, 0.5), tag: 3));
        var output = new float[512];
        long checksum = 0;
        for (var warmup = 0; warmup < 200; warmup++)
        {
            bus.Render(output);
            checksum += bus.LastReport[0].SourceFrame;
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var block = 0; block < 1000; block++)
        {
            bus.Render(output);
            checksum += bus.LastReport[0].SourceFrame;
        }
        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
        Assert.True(checksum > 0);
    }

    private static VoiceReport RenderUntilEnded(VoiceConfig config)
    {
        using var bus = new MixerBus(1, 48000, Block);
        var output = new float[Block];
        bus.AddVoice(config);
        for (var block = 0; block < 200; block++)
        {
            bus.Render(output);
            var report = Assert.Single(bus.LastReport.ToArray());
            if (report.Ended)
            {
                return report;
            }
        }
        throw new Xunit.Sdk.XunitException("voice never ended");
    }

    private static VoiceConfig Config(
        ISampleSource source,
        int tag = 0,
        ImmutableArray<MarkerSpec>? markers = null,
        TimeSpan? cueOut = null)
        => new(source, 0.0, null, null, markers ?? ImmutableArray<MarkerSpec>.Empty, TimeSpan.Zero, cueOut, Tag: tag);

    private sealed class FiniteSine(int totalFrames) : ISampleSource
    {
        private readonly SineSource _inner = new(1, 48000, 1000, 0.5);
        private readonly int _total = totalFrames;
        private int _remaining = totalFrames;

        public int Channels => 1;

        public int SampleRate => 48000;

        public int ReadFrames(Span<float> destination)
        {
            var frames = Math.Min(_remaining, destination.Length);
            if (frames <= 0)
            {
                return 0;
            }
            var read = _inner.ReadFrames(destination.Slice(0, frames));
            _remaining -= read;
            return read;
        }

        public void Seek(long frameIndex)
        {
            _inner.Seek(frameIndex);
            _remaining = _total - (int)Math.Clamp(frameIndex, 0, _total);
        }
    }
}
