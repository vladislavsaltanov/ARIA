namespace Aria.Audio.Tests;

public sealed class TestClickGeneratorTests
{
    private const int Channels = 2;

    [Fact]
    public void Click_AtMultiplesOf48000_SpansBlocks()
    {
        var generator = new TestClickGenerator(48000, Channels);
        var first = new float[512 * Channels];
        var second = new float[512 * Channels];

        generator.Mix(first, 47872);
        generator.Mix(second, 48384);

        Assert.All(first.AsSpan(0, 128 * Channels).ToArray(), sample => Assert.Equal(0f, sample));
        Assert.Contains(first.AsSpan(128 * Channels).ToArray(), sample => sample != 0f);
        Assert.Contains(second, sample => sample != 0f);
        Assert.Equal(first[129 * Channels], first[129 * Channels + 1]);
    }

    [Fact]
    public void Click_LastsFortyMilliseconds()
    {
        var generator = new TestClickGenerator(48000, 1);
        var block = new float[4000];

        generator.Mix(block, 46000);

        Assert.Contains(block.AsSpan(2000, 1920).ToArray(), sample => sample != 0f);
        Assert.All(block.AsSpan(3920).ToArray(), sample => Assert.Equal(0f, sample));
        Assert.All(block.AsSpan(0, 2000).ToArray(), sample => Assert.Equal(0f, sample));
    }

    [Fact]
    public void Click_AddsToExistingAudio()
    {
        var generator = new TestClickGenerator(48000, 1);
        var block = new float[512];
        Array.Fill(block, 0.25f);

        generator.Mix(block, 1_000_000);

        Assert.All(block, sample => Assert.Equal(0.25f, sample));
    }

    [Fact]
    public void Click_AllocatesNothing()
    {
        var generator = new TestClickGenerator(48000, Channels);
        var block = new float[512 * Channels];
        for (var warmup = 0; warmup < 200; warmup++)
        {
            generator.Mix(block, warmup * 512L);
        }

        var smallest = long.MaxValue;
        for (var attempt = 0; attempt < 5 && smallest != 0; attempt++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < 1000; index++)
            {
                generator.Mix(block, index * 512L);
            }
            smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.Equal(0, smallest);
    }
}
