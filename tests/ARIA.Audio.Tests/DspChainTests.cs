namespace Aria.Audio.Tests;

public sealed class DspChainTests
{
    private const int SampleRate = 48000;
    private const int Channels = 2;

    private static readonly float[] BandFrequencies = [63, 160, 400, 1000, 2500, 6300, 12000];

    [Fact]
    public void Eq_Flat_IsTransparent()
    {
        var eq = new SevenBandEq(BandFrequencies, Channels, SampleRate);
        var buffer = Sine(1000.0, 0.8, SampleRate);

        var reference = (float[])buffer.Clone();
        eq.Process(buffer, Channels);

        Assert.Equal(0.0, MaxAbsDiff(reference, buffer), 3);
    }

    [Fact]
    public void Eq_Constructor_RequiresSevenBands()
    {
        Assert.Throws<ArgumentException>(() => new SevenBandEq([1000], Channels, SampleRate));
        Assert.Throws<ArgumentException>(() => new SevenBandEq([], Channels, SampleRate));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SevenBandEq(BandFrequencies, 0, SampleRate));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SevenBandEq(BandFrequencies, Channels, 0));
    }

    [Fact]
    public void Eq_SetBand_ValidatesRanges()
    {
        var eq = new SevenBandEq(BandFrequencies, Channels, SampleRate);

        Assert.Throws<ArgumentOutOfRangeException>(() => eq.SetBand(-1, 0.0, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => eq.SetBand(7, 0.0, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => eq.SetBand(3, 16.0, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => eq.SetBand(3, -16.0, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => eq.SetBand(3, 0.0, 0.05));
        Assert.Throws<ArgumentOutOfRangeException>(() => eq.SetBand(3, 0.0, 11.0));
    }

    [Fact]
    public void Eq_BoostsCenterFrequency()
    {
        var eq = new SevenBandEq(BandFrequencies, Channels, SampleRate);
        eq.SetBand(3, 6.0, 1.0);
        var buffer = Sine(1000.0, 0.5, SampleRate * 2);

        eq.Process(buffer, Channels);

        var tail = buffer.AsSpan(buffer.Length - 4800 * Channels);
        var ratio = Rms(tail) / (0.5 / Math.Sqrt(2));
        Assert.True(ratio > 1.8, $"Boost ratio {ratio} is not above 1.8.");
    }

    [Fact]
    public void Eq_LeavesDistantFrequencyUntouched()
    {
        var eq = new SevenBandEq(BandFrequencies, Channels, SampleRate);
        eq.SetBand(3, 6.0, 1.0);
        var buffer = Sine(100.0, 0.5, SampleRate * 2);

        eq.Process(buffer, Channels);

        var tail = buffer.AsSpan(buffer.Length - 4800 * Channels);
        var ratio = Rms(tail) / (0.5 / Math.Sqrt(2));
        Assert.InRange(ratio, 0.95, 1.05);
    }

    [Fact]
    public void Eq_CutAttenuatesCenterFrequency()
    {
        var eq = new SevenBandEq(BandFrequencies, Channels, SampleRate);
        eq.SetBand(3, -6.0, 1.0);
        var buffer = Sine(1000.0, 0.5, SampleRate * 2);

        eq.Process(buffer, Channels);

        var tail = buffer.AsSpan(buffer.Length - 4800 * Channels);
        var ratio = Rms(tail) / (0.5 / Math.Sqrt(2));
        Assert.InRange(ratio, 0.4, 0.6);
    }

    [Fact]
    public void Limiter_ClampsAboveThreshold()
    {
        var limiter = new SimpleLimiter(Channels, SampleRate);
        limiter.SetParams(-6.0, 100.0);
        var buffer = Sine(1000.0, 2.0, SampleRate * 2);

        limiter.Process(buffer, Channels);

        var tail = buffer.AsSpan(buffer.Length - 4800 * Channels);
        var ceiling = Math.Pow(10.0, -6.0 / 20.0);
        var peak = Peak(tail);
        Assert.True(peak <= ceiling + 0.02, $"Peak {peak} exceeds ceiling {ceiling}.");
        Assert.True(peak > ceiling * 0.9, $"Peak {peak} is over-attenuated below ceiling {ceiling}.");
    }

    [Fact]
    public void Limiter_LeavesQuietSignalUntouched()
    {
        var limiter = new SimpleLimiter(Channels, SampleRate);
        limiter.SetParams(-6.0, 100.0);
        var buffer = Sine(1000.0, 0.1, SampleRate);

        var reference = (float[])buffer.Clone();
        limiter.Process(buffer, Channels);

        Assert.True(MaxAbsDiff(reference, buffer) < 1e-4, "Quiet signal was modified.");
    }

    [Fact]
    public void Limiter_RecoversAfterBurst()
    {
        var limiter = new SimpleLimiter(Channels, SampleRate);
        limiter.SetParams(-6.0, 100.0);
        var loud = Sine(1000.0, 2.0, SampleRate / 2);
        limiter.Process(loud, Channels);

        var quiet = Sine(1000.0, 0.1, SampleRate, Math.Atan2(0, -1));
        limiter.Process(quiet, Channels);

        var tail = quiet.AsSpan(quiet.Length - 4800 * Channels);
        Assert.Equal(0.1, Peak(tail), 2);
    }

    [Fact]
    public void Limiter_SetParams_ValidatesRanges()
    {
        var limiter = new SimpleLimiter(Channels, SampleRate);

        Assert.Throws<ArgumentOutOfRangeException>(() => limiter.SetParams(1.0, 100.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => limiter.SetParams(-25.0, 100.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => limiter.SetParams(-6.0, 9.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => limiter.SetParams(-6.0, 1001.0));
    }

    [Fact]
    public void Pan_Center_SplitsEqualPower()
    {
        var pan = new PanNode(0.0);
        var buffer = new float[] { 1f, 1f, 1f, 1f };

        pan.Process(buffer, Channels);

        Assert.Equal(0.70711, buffer[0], 4);
        Assert.Equal(0.70711, buffer[1], 4);
    }

    [Fact]
    public void Pan_HardSides_RouteSingleChannel()
    {
        var left = new PanNode(-1.0);
        var buffer = new float[] { 1f, 1f };
        left.Process(buffer, Channels);
        Assert.Equal(1f, buffer[0], 6);
        Assert.Equal(0f, buffer[1], 6);

        var right = new PanNode(1.0);
        buffer = new float[] { 1f, 1f };
        right.Process(buffer, Channels);
        Assert.Equal(0f, buffer[0], 6);
        Assert.Equal(1f, buffer[1], 6);
    }

    [Fact]
    public void Pan_SetPan_ValidatesRange()
    {
        var pan = new PanNode(0.0);

        Assert.Throws<ArgumentOutOfRangeException>(() => pan.SetPan(1.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => pan.SetPan(-1.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PanNode(2.0));
    }

    [Fact]
    public void Hpf_Bypass_IsTransparent()
    {
        var hpf = new HighPassNode(0.0, Channels, SampleRate);
        var buffer = Sine(100.0, 0.8, SampleRate);

        var reference = (float[])buffer.Clone();
        hpf.Process(buffer, Channels);

        Assert.Equal(reference, buffer);
    }

    [Fact]
    public void Hpf_BlocksDcAndLowFrequencies()
    {
        var hpf = new HighPassNode(400.0, Channels, SampleRate);
        var dc = new float[SampleRate * Channels];
        Array.Fill(dc, 1f);
        hpf.Process(dc, Channels);
        Assert.True(Math.Abs(dc[^1]) < 1e-3, $"DC leaked: {dc[^1]}.");

        var low = Sine(30.0, 0.8, SampleRate * 2);
        hpf.Process(low, Channels);
        var tail = low.AsSpan(low.Length - 4800 * Channels);
        Assert.True(Rms(tail) / (0.8 / Math.Sqrt(2)) < 0.05, "30 Hz passes 400 Hz HPF.");
    }

    [Fact]
    public void Hpf_PassesHighFrequencies()
    {
        var hpf = new HighPassNode(400.0, Channels, SampleRate);
        var buffer = Sine(5000.0, 0.8, SampleRate);

        hpf.Process(buffer, Channels);

        var tail = buffer.AsSpan(buffer.Length - 4800 * Channels);
        Assert.True(Rms(tail) / (0.8 / Math.Sqrt(2)) > 0.98, "5 kHz attenuated by 400 Hz HPF.");
    }

    [Fact]
    public void Hpf_SetFrequency_ValidatesRange()
    {
        var hpf = new HighPassNode(0.0, Channels, SampleRate);

        Assert.Throws<ArgumentOutOfRangeException>(() => hpf.SetFrequency(-1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => hpf.SetFrequency(401.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HighPassNode(-10.0, Channels, SampleRate));
    }

    [Fact]
    public void Mono_Disabled_IsTransparent()
    {
        var mono = new MonoSumNode();
        var buffer = new float[] { 0.8f, 0.4f, 0.8f, 0.4f };

        var reference = (float[])buffer.Clone();
        mono.Process(buffer, Channels);

        Assert.Equal(reference, buffer);
    }

    [Fact]
    public void Mono_Enabled_SumsBothChannels()
    {
        var mono = new MonoSumNode { Enabled = true };
        var buffer = new float[] { 0.8f, 0.4f, -0.8f, -0.4f };

        mono.Process(buffer, Channels);

        Assert.Equal(0.6f, buffer[0], 6);
        Assert.Equal(0.6f, buffer[1], 6);
        Assert.Equal(-0.6f, buffer[2], 6);
        Assert.Equal(-0.6f, buffer[3], 6);
    }

    [Fact]
    public void Chain_AllNodes_NoAllocationsInSteadyState()
    {
        var eq = new SevenBandEq(BandFrequencies, Channels, SampleRate);
        eq.SetBand(3, 3.0, 1.0);
        var limiter = new SimpleLimiter(Channels, SampleRate);
        limiter.SetParams(-3.0, 50.0);
        var pan = new PanNode(0.2);
        var hpf = new HighPassNode(120.0, Channels, SampleRate);
        var mono = new MonoSumNode { Enabled = true };
        var buffer = new float[1024 * Channels];
        FillSine(buffer, 440.0, 0.5, SampleRate);

        IDspNode[] chain = [hpf, eq, pan, limiter, mono];
        for (var warmup = 0; warmup < 200; warmup++)
        {
            foreach (var node in chain)
            {
                node.Process(buffer, Channels);
            }
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            foreach (var node in chain)
            {
                node.Process(buffer, Channels);
            }
        }
        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }

    private static float[] Sine(double frequency, double amplitude, int frames, double phase = 0.0)
    {
        var buffer = new float[frames * Channels];
        FillSine(buffer, frequency, amplitude, SampleRate, phase);
        return buffer;
    }

    private static void FillSine(float[] buffer, double frequency, double amplitude, int sampleRate, double phase = 0.0)
    {
        var delta = 2.0 * Math.PI * frequency / sampleRate;
        for (var frame = 0; frame < buffer.Length / Channels; frame++)
        {
            var value = (float)(amplitude * Math.Sin(phase));
            phase += delta;
            for (var channel = 0; channel < Channels; channel++)
            {
                buffer[frame * Channels + channel] = value;
            }
        }
    }

    private static double Rms(ReadOnlySpan<float> samples)
    {
        double sum = 0;
        foreach (var sample in samples)
        {
            sum += sample * sample;
        }
        return Math.Sqrt(sum / samples.Length);
    }

    private static float Peak(ReadOnlySpan<float> samples)
    {
        float peak = 0;
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

    private static float MaxAbsDiff(float[] left, float[] right)
    {
        float max = 0;
        for (var i = 0; i < left.Length; i++)
        {
            var diff = Math.Abs(left[i] - right[i]);
            if (diff > max)
            {
                max = diff;
            }
        }
        return max;
    }
}
