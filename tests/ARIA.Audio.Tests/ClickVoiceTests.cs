namespace Aria.Audio.Tests;

using System.Diagnostics;
using Aria.Core.Playback;

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
        Assert.Equal(downbeat, beat * 2, 1);
        Assert.True(downbeat > beat * 1.5, "accent missing");
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
    public void UpdateSettings_PreservesPhase()
    {
        var voice = new ClickVoice(1, SampleRate, new ClickSettings(120, 4, 0, 0));
        var warm = new float[1000];
        voice.ReadFrames(warm);
        voice.UpdateSettings(new ClickSettings(60, 4, 0, 0));
        var rest = new float[48000];
        voice.ReadFrames(rest);
        Assert.Equal(0, Energy(rest, 23000, 2000));
        Assert.True(Energy(rest, 47000, 192) > 1, "no click on rebased grid");
    }

    [Fact]
    public void InvalidSettings_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClickSettings(0, 4, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClickSettings(120, 0, 0, 0));
    }

    [Fact]
    public void Seek_HugeFrameIndex_ReturnsFast_WithCoherentGrid()
    {
        var voice = new ClickVoice(1, SampleRate, new ClickSettings(120, 4, 0, 0));
        var sw = Stopwatch.StartNew();
        voice.Seek(9_000_000_000_000_000_000L);
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), "seek hung on huge frame");
        var buffer = new float[48000];
        Assert.Equal(48000, voice.ReadFrames(buffer));
        Assert.True(Energy(buffer, 0, 48000) > 1, "no click within two beats of huge seek");
    }
}
