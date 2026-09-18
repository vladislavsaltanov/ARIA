namespace Aria.Audio.Tests;

public sealed class ClickVoiceTests
{
    private const int SampleRate = 48000;

    private static double Energy(float[] buffer, int from, int count)
    {
        var sum = 0.0;
        for (var i = 0; i < count; i++)
        {
            sum += Math.Abs(buffer[from + i]);
        }
        return sum;
    }

    [Fact]
    public void BeatInterval_MatchesBpm()
    {
        var voice = new ClickVoice(2, SampleRate, new ClickSettings(120, 4, 0, 0));
        var buffer = new float[24000 * 2 * 2];
        Assert.Equal(buffer.Length / 2, voice.ReadFrames(buffer));
        Assert.True(Energy(buffer, 0, 96) > 1, "no click at grid start");
        Assert.Equal(0, Energy(buffer, 24000 * 2 - 2000, 2000));
        Assert.True(Energy(buffer, 24000 * 2, 96) > 1, "no click on second beat");
    }

    [Fact]
    public void Downbeat_AccentIsLouderThanBeat()
    {
        var voice = new ClickVoice(2, SampleRate, new ClickSettings(120, 4, 0, 0));
        var buffer = new float[24000 * 2 * 3];
        voice.ReadFrames(buffer);
        var downbeat = 0.0;
        var beat = 0.0;
        for (var i = 0; i < 1920 * 2; i++)
        {
            downbeat = Math.Max(downbeat, Math.Abs(buffer[i]));
            beat = Math.Max(beat, Math.Abs(buffer[24000 * 2 + i]));
        }
        Assert.True(downbeat > 0, "downbeat is silent");
        Assert.True(beat > 0, "beat is silent");
        Assert.Equal(downbeat, beat * 2, 3);
    }

    [Fact]
    public void Offset_DelaysFirstClick()
    {
        var voice = new ClickVoice(2, SampleRate, new ClickSettings(120, 4, 0, 100));
        var buffer = new float[4800 * 2 + 96];
        voice.ReadFrames(buffer);
        Assert.Equal(0, Energy(buffer, 0, 4800 * 2));
        Assert.True(Energy(buffer, 4800 * 2, 96) > 1, "no click after offset");
    }

    [Fact]
    public void Seek_RestartsGrid()
    {
        var voice = new ClickVoice(1, SampleRate, new ClickSettings(120, 4, 0, 0));
        var first = new float[1024];
        voice.ReadFrames(first);
        voice.Seek(24000);
        var second = new float[1024];
        voice.ReadFrames(second);
        voice.Seek(0);
        var replay = new float[1024];
        voice.ReadFrames(replay);
        Assert.Equal(first, replay);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void InvalidSettings_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClickSettings(0, 4, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClickSettings(120, 0, 0, 0));
    }
}
