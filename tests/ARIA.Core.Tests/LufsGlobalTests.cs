namespace Aria.Core.Tests;

using Aria.Core.Model;

public sealed class LufsGlobalTests
{
    [Fact]
    public void Defaults_TargetMinusSixteen_ZonesMinusFifteenNineFive()
    {
        Assert.Equal(-16.0, GlobalAudioSettings.Default.NormalizeTargetLufs);
        Assert.Equal(new LufsMeterZones(-15.0, -9.0, -5.0), GlobalAudioSettings.Default.EffectiveZones);
    }

    [Fact]
    public void TargetOutOfRange_Rejects()
    {
        Assert.Equal("lufs-out-of-range", AudioValidation.ValidateGlobal(GlobalAudioSettings.Default with { NormalizeTargetLufs = -6.0 }));
        Assert.Equal("lufs-out-of-range", AudioValidation.ValidateGlobal(GlobalAudioSettings.Default with { NormalizeTargetLufs = -48.0 }));
        Assert.Null(AudioValidation.ValidateGlobal(GlobalAudioSettings.Default with { NormalizeTargetLufs = -23.0 }));
    }

    [Fact]
    public void UnorderedZones_Reject()
    {
        Assert.Equal("meter-out-of-range", AudioValidation.ValidateGlobal(GlobalAudioSettings.Default with { MeterZones = new LufsMeterZones(-9.0, -15.0, -5.0) }));
        Assert.Equal("meter-out-of-range", AudioValidation.ValidateGlobal(GlobalAudioSettings.Default with { MeterZones = new LufsMeterZones(-15.0, -9.0, -99.0) }));
        Assert.Null(AudioValidation.ValidateGlobal(GlobalAudioSettings.Default with { MeterZones = new LufsMeterZones(-18.0, -12.0, -4.0) }));
    }
}
