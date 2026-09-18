namespace Aria.Audio.Tests;

public sealed class PreviewTapTests
{
    [Fact]
    public void TwoReaders_EachReceivesFullSequence()
    {
        var tap = new PreviewTap(1024, 2);
        var first = tap.Subscribe();
        var second = tap.Subscribe();
        var data = new float[256 * 2];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = i;
        }
        tap.Publish(data);
        var a = new float[data.Length];
        var b = new float[data.Length];
        Assert.Equal(data.Length, first.Read(a));
        Assert.Equal(data.Length, second.Read(b));
        Assert.Equal(data, a);
        Assert.Equal(data, b);
    }

    [Fact]
    public void SlowReader_SkipsOverrun_ReceivesNewest()
    {
        var tap = new PreviewTap(256, 1);
        var slow = tap.Subscribe();
        var chunk = new float[256];
        for (var i = 0; i < 4; i++)
        {
            Array.Fill(chunk, i + 1);
            tap.Publish(chunk);
        }
        var read = new float[256];
        Assert.Equal(256, slow.Read(read));
        Assert.All(read, v => Assert.Equal(4, v));
    }

    [Fact]
    public void LateSubscriber_StartsAtLiveEdge()
    {
        var tap = new PreviewTap(1024, 1);
        var old = new float[100];
        Array.Fill(old, 1);
        tap.Publish(old);
        var late = tap.Subscribe();
        var fresh = new float[100];
        Array.Fill(fresh, 2);
        tap.Publish(fresh);
        var read = new float[100];
        Assert.Equal(100, late.Read(read));
        Assert.All(read, v => Assert.Equal(2, v));
    }
}
