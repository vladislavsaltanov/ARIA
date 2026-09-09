namespace Aria.Audio.Tests;

public sealed class SampleRingTests
{
    [Fact]
    public void WriteRead_PreservesSequence_AcrossWraparound()
    {
        var ring = new SampleRing(1024, 1);
        var writeBuffer = new float[1000];
        var readBuffer = new float[1000];
        var next = 0f;

        for (var lap = 0; lap < 5; lap++)
        {
            for (var i = 0; i < writeBuffer.Length; i++)
            {
                writeBuffer[i] = next++;
            }
            var written = ring.Write(writeBuffer);
            Assert.Equal(1000, written);
            Assert.Equal(1000, ring.Count);

            var read = ring.Read(readBuffer);
            Assert.Equal(1000, read);
            for (var i = 0; i < readBuffer.Length; i++)
            {
                Assert.Equal(lap * 1000 + i, readBuffer[i]);
            }
            Assert.Equal(0, ring.Count);
        }
    }

    [Fact]
    public void Write_OnFullRing_ReturnsLessThanRequested()
    {
        var ring = new SampleRing(16, 1);
        var full = new float[16];
        Array.Fill(full, 1f);

        Assert.Equal(16, ring.Write(full));
        Assert.Equal(0, ring.Write(full));
    }

    [Fact]
    public void Capacity_IsRoundedUpToPowerOfTwo()
    {
        Assert.Equal(1024, new SampleRing(1000, 1).CapacityFrames);
        Assert.Equal(8, new SampleRing(8, 1).CapacityFrames);
        Assert.Equal(16, new SampleRing(9, 1).CapacityFrames);
    }

    [Fact]
    public void Multichannel_CountAndPartialRead_WorkInFrames()
    {
        var ring = new SampleRing(64, 2);
        var data = new float[20];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = i;
        }

        Assert.Equal(20, ring.Write(data));
        Assert.Equal(20, ring.Count);

        var readBuffer = new float[13];
        Assert.Equal(12, ring.Read(readBuffer));
        Assert.Equal(8, ring.Count);
        for (var i = 0; i < 12; i++)
        {
            Assert.Equal(i, readBuffer[i]);
        }

        var rest = new float[8];
        Assert.Equal(8, ring.Read(rest));
        Assert.Equal(0, ring.Count);
    }

    [Fact]
    public async Task Spsc_BetweenThreads_PreservesOrder()
    {
        var ring = new SampleRing(256, 1);
        const long total = 200_000;
        using var done = new CountdownEvent(2);

        var producer = Task.Run(() =>
        {
            var buffer = new float[257];
            long produced = 0;
            var value = 0f;
            while (produced < total)
            {
                for (var i = 0; i < buffer.Length && produced + i < total; i++)
                {
                    buffer[i] = value++;
                }
                var chunk = Math.Min(buffer.Length, (int)(total - produced));
                var offset = 0;
                while (offset < chunk)
                {
                    offset += ring.Write(buffer.AsSpan(offset, chunk - offset));
                }
                produced += chunk;
            }
            done.Signal();
        });

        var consumer = Task.Run(() =>
        {
            var buffer = new float[301];
            var expected = 0f;
            long consumed = 0;
            while (consumed < total)
            {
                var read = ring.Read(buffer);
                for (var i = 0; i < read; i++)
                {
                    Assert.Equal(expected++, buffer[i]);
                }
                consumed += read;
            }
            done.Signal();
        });

        Assert.True(done.Wait(TimeSpan.FromSeconds(10)), "SPSC test timed out");
        await Task.WhenAll(producer, consumer);
    }
}
