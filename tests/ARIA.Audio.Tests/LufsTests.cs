namespace Aria.Audio.Tests;

public sealed class LufsTests
{
    private const int SampleRate = 48000;

    [Fact]
    public void Silence_ReadsFloor()
    {
        var meter = new LufsMeter(2, SampleRate, TimeSpan.FromMilliseconds(400));
        var buffer = new float[9600];

        for (var i = 0; i < 10; i++)
        {
            meter.Process(buffer, 2);
        }

        Assert.Equal(-70.0, meter.MomentaryLufs);
    }

    [Fact]
    public void HalfSine_ReportsNearMinusSevenLufs()
    {
        var meter = new LufsMeter(2, SampleRate, TimeSpan.FromMilliseconds(400));
        var source = new SineSource(2, SampleRate, 440.0, 0.5);
        var buffer = new float[9600];

        for (var i = 0; i < 10; i++)
        {
            var frames = source.ReadFrames(buffer);
            meter.Process(buffer.AsSpan(0, frames * 2), 2);
        }

        Assert.InRange(meter.MomentaryLufs, -7.4, -6.0);
    }

    [Fact]
    public void PartialWindow_ReportsSaneValue()
    {
        var meter = new LufsMeter(2, SampleRate, TimeSpan.FromMilliseconds(400));
        var source = new SineSource(2, SampleRate, 440.0, 0.5);
        var buffer = new float[9600];
        var frames = source.ReadFrames(buffer);
        meter.Process(buffer.AsSpan(0, frames * 2), 2);

        Assert.InRange(meter.MomentaryLufs, -7.4, -6.0);
    }

    [Fact]
    public void Dc_IsBlockedByHighPass()
    {
        var meter = new LufsMeter(2, SampleRate, TimeSpan.FromMilliseconds(400));
        var buffer = new float[9600];
        Array.Fill(buffer, 0.5f);

        for (var i = 0; i < 10; i++)
        {
            meter.Process(buffer, 2);
        }

        Assert.True(meter.MomentaryLufs < -60.0, $"dc leaked: {meter.MomentaryLufs}");
    }

    [Fact]
    public void KWeighting_PassesMidband()
    {
        var pre = BiquadFilter.KWeightingPreFilter(SampleRate);
        var post = BiquadFilter.KWeightingHighPass(SampleRate);
        double sum = 0;
        var count = 0;
        for (var i = 0; i < SampleRate; i++)
        {
            var x = 0.5 * Math.Sin(2.0 * Math.PI * 1000.0 * i / SampleRate);
            var y = post.ProcessSample(pre.ProcessSample(x));
            if (i > SampleRate / 10)
            {
                sum += y * y;
                count++;
            }
        }
        var lufs = -0.691 + 10.0 * Math.Log10(sum / count * 2.0);

        Assert.InRange(lufs, -7.5, -5.9);
    }

    [Fact]
    public void Process_AllocatesNothing_InSteadyState()
    {
        var meter = new LufsMeter(2, SampleRate, TimeSpan.FromMilliseconds(400));
        var source = new SineSource(2, SampleRate, 440.0, 0.5);
        var buffer = new float[1024];

        for (var warmup = 0; warmup < 500; warmup++)
        {
            var frames = source.ReadFrames(buffer);
            meter.Process(buffer.AsSpan(0, frames * 2), 2);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            var frames = source.ReadFrames(buffer);
            meter.Process(buffer.AsSpan(0, frames * 2), 2);
        }
        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }
}
