namespace Aria.Audio.Tests;

public sealed class MixReportRingTests
{
    private static VoiceReport[] Voices(long sequence, int count)
    {
        var voices = new VoiceReport[count];
        for (var index = 0; index < count; index++)
        {
            voices[index] = new VoiceReport { Tag = (int)sequence, SourceFrame = sequence * 10 + index, Active = true };
        }
        return voices;
    }

    [Fact]
    public void Publish_ThenRead_RoundTrip()
    {
        var ring = new MixReportRing();
        ring.Publish(4096, 3, Voices(1, 2), overflow: true, testClick: true);
        var target = new MixBlockReport();

        Assert.Equal(1, ring.LatestSequence);
        Assert.True(ring.TryRead(1, target));
        Assert.Equal(1, target.Sequence);
        Assert.Equal(4096, target.OutputFrame);
        Assert.Equal(3, target.SinkGeneration);
        Assert.True(target.Overflow);
        Assert.True(target.TestClick);
        Assert.Equal(2, target.Count);
        Assert.Equal(11, target.Voices[1].SourceFrame);
    }

    [Fact]
    public void Read_Overwritten_ReturnsFalse()
    {
        var ring = new MixReportRing();
        for (var sequence = 1; sequence <= 70; sequence++)
        {
            ring.Publish(sequence * 512, 0, Voices(sequence, 1), false, false);
        }
        var target = new MixBlockReport();

        Assert.False(ring.TryRead(1, target));
        Assert.False(ring.TryRead(71, target));
        Assert.True(ring.TryRead(70, target));
        Assert.Equal(70 * 512, target.OutputFrame);
    }

    [Fact]
    public void ConcurrentWriterReader_NoTornReads()
    {
        var ring = new MixReportRing();
        using var stop = new CancellationTokenSource();
        var writer = new Thread(() =>
        {
            var sequence = 0L;
            var voices = new VoiceReport[MixerBus.MaxReportVoices];
            while (!stop.IsCancellationRequested)
            {
                sequence++;
                var count = (int)(sequence % MixerBus.MaxReportVoices) + 1;
                for (var index = 0; index < count; index++)
                {
                    voices[index] = new VoiceReport { Tag = (int)sequence, SourceFrame = sequence, EndSourceFrame = sequence, Epoch = (int)sequence };
                }
                ring.Publish(sequence, (int)sequence, voices.AsSpan(0, count), false, false);
            }
        });
        writer.Start();
        var target = new MixBlockReport();
        var good = 0;
        var deadline = Environment.TickCount64 + 10_000;
        while (good < 1000 && Environment.TickCount64 < deadline)
        {
            var sequence = ring.LatestSequence;
            if (sequence == 0 || !ring.TryRead(sequence, target))
            {
                continue;
            }
            good++;
            Assert.Equal(target.Sequence, target.OutputFrame);
            Assert.Equal((int)target.Sequence, target.SinkGeneration);
            Assert.Equal((int)(target.Sequence % MixerBus.MaxReportVoices) + 1, target.Count);
            for (var index = 0; index < target.Count; index++)
            {
                Assert.Equal(target.Sequence, target.Voices[index].SourceFrame);
                Assert.Equal(target.Sequence, target.Voices[index].EndSourceFrame);
                Assert.Equal((int)target.Sequence, target.Voices[index].Tag);
            }
        }
        stop.Cancel();
        writer.Join();
        Assert.Equal(1000, good);
    }

    [Fact]
    public void Publish_AllocatesNothing()
    {
        var ring = new MixReportRing();
        var voices = Voices(1, 4);
        for (var warmup = 0; warmup < 200; warmup++)
        {
            ring.Publish(warmup, 0, voices, false, false);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var block = 0; block < 1000; block++)
        {
            ring.Publish(block, 0, voices, false, false);
        }
        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }
}
