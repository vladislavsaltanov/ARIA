namespace Aria.Core.Tests;

using Aria.Core.Playback;

public sealed class ClickSettingsTests
{
    [Fact]
    public void ZeroBpm_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClickSettings(0, 4, 0, 0));
    }

    [Fact]
    public void ZeroBeatsPerBar_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClickSettings(120, 0, 0, 0));
    }

    [Fact]
    public void Valid_KeepsValues()
    {
        var settings = new ClickSettings(120.5, 3, -6, 25);
        Assert.Equal(120.5, settings.Bpm);
        Assert.Equal(3, settings.BeatsPerBar);
        Assert.Equal(-6, settings.GainDb);
        Assert.Equal(25, settings.OffsetMs);
    }
}
