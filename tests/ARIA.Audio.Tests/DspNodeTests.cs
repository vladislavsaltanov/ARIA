namespace Aria.Audio.Tests;

using Aria.Core.Model;

public sealed class DspNodeTests
{
    [Fact]
    public void Fader_RampsFromStartToEnd_AndCompletes()
    {
        var fader = new FaderNode(100, FadeCurve.Linear, 0.0, 1.0, stopWhenDone: true);
        var buffer = new float[200];
        Array.Fill(buffer, 1f);

        fader.Process(buffer, 1);

        Assert.Equal(0f, buffer[0], 6);
        Assert.True(fader.HasCompleted);
        Assert.Equal(0.99, buffer[99], 4);

        Array.Fill(buffer, 1f);
        fader.Process(buffer, 1);
        Assert.Equal(1f, buffer[0], 6);
    }

    [Fact]
    public void Fader_ZeroRamp_IsImmediatelyComplete()
    {
        var fader = new FaderNode(0, FadeCurve.Linear, 0.0, 1.0, stopWhenDone: false);

        Assert.True(fader.HasCompleted);

        var buffer = new float[] { 0.5f };
        fader.Process(buffer, 1);
        Assert.Equal(0.5f, buffer[0], 6);
    }

    [Fact]
    public void Fader_SCurve_AppliesCurveShape()
    {
        var fader = new FaderNode(10, FadeCurve.SCurve, 0.0, 1.0, stopWhenDone: false);
        var buffer = new float[10];
        Array.Fill(buffer, 1f);

        fader.Process(buffer, 1);

        Assert.Equal(0.5f, buffer[5], 3);
    }

    [Fact]
    public void Meter_ComputesPeakAndRms()
    {
        var source = new SineSource(1, 48000, 1000, 0.8);
        var meter = new MeterNode();
        var buffer = new float[4800];

        var frames = source.ReadFrames(buffer);
        meter.Process(buffer.AsSpan(0, frames), 1);

        Assert.Equal(0.8, meter.Peak, 2);
        Assert.Equal(0.5657, meter.Rms, 2);
        Assert.Equal(meter.Peak, meter.RunningPeak, 2);
    }

    [Fact]
    public void Gain_MinusSixDb_HalvesAmplitude()
    {
        var gain = new GainNode(-6.0);
        var buffer = new float[] { 1f, -1f, 0.5f, -0.5f };

        gain.Process(buffer, 1);

        Assert.Equal(0.5012f, buffer[0], 3);
        Assert.Equal(-0.5012f, buffer[1], 3);
    }

    [Fact]
    public void Pipeline_NoAllocations_InSteadyState()
    {
        var source = new SineSource(2, 48000, 440.0, 0.8);
        var fader = new FaderNode(10_000_000, FadeCurve.Linear, 0.0, 1.0, stopWhenDone: true);
        var meter = new MeterNode();
        var gain = new GainNode(0.0);
        var buffer = new float[1024];

        for (var warmup = 0; warmup < 200; warmup++)
        {
            Step(source, fader, gain, meter, buffer);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            Step(source, fader, gain, meter, buffer);
        }
        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }

    private static void Step(SineSource source, FaderNode fader, GainNode gain, MeterNode meter, float[] buffer)
    {
        var frames = source.ReadFrames(buffer);
        var span = buffer.AsSpan(0, frames * 2);
        fader.Process(span, 2);
        gain.Process(span, 2);
        meter.Process(span, 2);
    }
}
