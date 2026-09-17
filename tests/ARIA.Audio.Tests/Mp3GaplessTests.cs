namespace Aria.Audio.Tests;

public sealed class Mp3GaplessTests
{
    [Fact]
    public void NoTag_ReturnsFalse()
    {
        Assert.False(Mp3Gapless.TryRead(GarbageFrame(), out _));
    }

    [Fact]
    public void Truncated_ReturnsFalse()
    {
        Assert.False(Mp3Gapless.TryRead([0x49, 0x44, 0x33], out _));
    }

    [Fact]
    public void LameTag_ParsesDelayPaddingAndRate()
    {
        var info = AssertRead(LameFrame(delay: 2112, padding: 172));

        Assert.Equal(2112, info.DelaySamples);
        Assert.Equal(172, info.PaddingSamples);
        Assert.Equal(44100, info.SampleRate);
    }

    [Fact]
    public void InfoMagic_AcceptedLikeXing()
    {
        var frame = LameFrame(delay: 2112, padding: 0);
        frame[36] = (byte)'I';
        frame[37] = (byte)'n';
        frame[38] = (byte)'f';
        frame[39] = (byte)'o';

        var info = AssertRead(frame);

        Assert.Equal(2112, info.DelaySamples);
    }

    [Fact]
    public void Id3Prefix_Skipped()
    {
        var id3 = new byte[10 + 100];
        id3[0] = (byte)'I';
        id3[1] = (byte)'D';
        id3[2] = (byte)'3';
        var frame = LameFrame(delay: 2112, padding: 0);

        var info = AssertRead([.. id3, .. frame]);

        Assert.Equal(2112, info.DelaySamples);
    }

    private static Mp3GapInfo AssertRead(byte[] data)
    {
        Assert.True(Mp3Gapless.TryRead(data, out var info));
        return info;
    }

    private static byte[] GarbageFrame()
    {
        var data = new byte[256];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i * 7 + 3);
        }
        return data;
    }

    private static byte[] LameFrame(int delay, int padding)
    {
        var frame = new byte[256];
        frame[0] = 0xFF;
        frame[1] = 0xFB;
        frame[2] = 0x90;
        frame[3] = 0x00;
        var xing = 4 + 32;
        frame[xing] = (byte)'X';
        frame[xing + 1] = (byte)'i';
        frame[xing + 2] = (byte)'n';
        frame[xing + 3] = (byte)'g';
        frame[xing + 4] = 0x00;
        frame[xing + 5] = 0x00;
        frame[xing + 6] = 0x00;
        frame[xing + 7] = 0x0F;
        var lame = xing + 8 + 4 + 4 + 100 + 4;
        frame[lame] = (byte)'L';
        frame[lame + 1] = (byte)'A';
        frame[lame + 2] = (byte)'M';
        frame[lame + 3] = (byte)'E';
        frame[lame + 21] = (byte)(delay >> 4);
        frame[lame + 22] = (byte)(((delay & 0xF) << 4) | ((padding >> 8) & 0xF));
        frame[lame + 23] = (byte)(padding & 0xFF);
        return frame;
    }
}
