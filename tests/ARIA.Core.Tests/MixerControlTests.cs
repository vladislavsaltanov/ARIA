namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.State;

public sealed class MixerControlTests
{
    [Fact]
    public void FreshBoot_IsUnmuted()
    {
        using var h = new Harness();

        Assert.False(h.Snapshot.Mixer.Muted);
    }

    [Fact]
    public void SetMuted_SilencesEngine_RemembersGain()
    {
        using var h = new Harness();
        h.Submit(new SetMasterGain(-6));

        h.Submit(new SetMuted(true));

        Assert.True(h.Snapshot.Mixer.Muted);
        Assert.Equal(-6, h.Snapshot.Mixer.MasterGainDb);
        Assert.Equal(-80.0, h.Engine.MasterGains[^1]);
    }

    [Fact]
    public void SetMuted_Unmute_RestoresGain()
    {
        using var h = new Harness();
        h.Submit(new SetMasterGain(-6));
        h.Submit(new SetMuted(true));

        h.Submit(new SetMuted(false));

        Assert.False(h.Snapshot.Mixer.Muted);
        Assert.Equal(-6, h.Engine.MasterGains[^1]);
    }

    [Fact]
    public void SetMasterGain_Unmutes()
    {
        using var h = new Harness();
        h.Submit(new SetMuted(true));

        h.Submit(new SetMasterGain(-3));

        Assert.False(h.Snapshot.Mixer.Muted);
        Assert.Equal(-3, h.Engine.MasterGains[^1]);
    }

    [Fact]
    public void SetMuted_SameValue_IsIdempotent()
    {
        using var h = new Harness();
        h.Submit(new SetMuted(true));
        var gains = h.Engine.MasterGains.Count;

        h.Submit(new SetMuted(true));

        Assert.Equal(gains, h.Engine.MasterGains.Count);
    }

    [Fact]
    public void MixerDelta_CarriesMuted()
    {
        using var h = new Harness();

        h.Submit(new SetMuted(true));

        var delta = h.Events.OfType<MixerDelta>().Last();
        Assert.True(delta.State.Muted);
    }
}
