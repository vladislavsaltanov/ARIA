namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.State;

public sealed class LufsNormalizeTests
{
    private static void EnableGlobal(Harness h) =>
        h.Submit(new SetGlobalAudio(GlobalAudioSettings.Default with { NormalizeEnabled = true }));

    [Fact]
    public void StoresMeasurementAndEnables()
    {
        using var h = new Harness();
        h.Engine.ScanLufs = _ => -10.0;
        var track = TestShow.Track("a");
        var project = TestShow.Project("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [project], project.Id));
        EnableGlobal(h);

        var seq = h.Submit(new NormalizeTrack(track.Id));

        Assert.Null(h.RejectionOf(seq));
        Assert.NotEmpty(h.Events.OfType<ShowDelta>());
        Assert.Equal([track.FilePath], h.Engine.ScannedPaths);
    }

    [Fact]
    public void DisabledGlobal_Rejects()
    {
        using var h = new Harness();
        var track = TestShow.Track("a");
        var project = TestShow.Project("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [project], project.Id));

        var seq = h.Submit(new NormalizeTrack(track.Id));

        Assert.Equal("normalize-disabled", h.RejectionOf(seq)!.Reason);
        Assert.Empty(h.Engine.ScannedPaths);
    }

    [Fact]
    public void UnknownTrack_Rejects()
    {
        using var h = new Harness();
        EnableGlobal(h);

        var seq = h.Submit(new NormalizeTrack(TrackId.New()));

        Assert.Equal("unknown-track", h.RejectionOf(seq)!.Reason);
    }

    [Fact]
    public void ScanFailure_Rejects()
    {
        using var h = new Harness();
        h.Engine.ScanLufs = _ => double.NaN;
        var track = TestShow.Track("a");
        var project = TestShow.Project("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [project], project.Id));
        EnableGlobal(h);

        var seq = h.Submit(new NormalizeTrack(track.Id));

        Assert.Equal("lufs-scan-failed", h.RejectionOf(seq)!.Reason);
    }

    [Fact]
    public void NormalizePlaying_PushesLiveVoice()
    {
        using var h = new Harness();
        h.Engine.ScanLufs = _ => -10.0;
        var track = TestShow.Track("a");
        var project = TestShow.Project("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [project], project.Id));
        EnableGlobal(h);
        h.Submit(new PlayTrack(track.Id));
        var handle = h.Engine.Last!.Handle;

        var seq = h.Submit(new NormalizeTrack(track.Id));

        Assert.Null(h.RejectionOf(seq));
        var push = Assert.Single(h.Engine.VoiceAudios);
        Assert.Equal(handle, push.Handle);
        Assert.Equal(-10.0, push.Audio.MeasuredLufs);
        Assert.True(push.Audio.NormalizeEnabled);
    }

    [Fact]
    public void NormalizeIdle_TouchesNoVoice()
    {
        using var h = new Harness();
        h.Engine.ScanLufs = _ => -10.0;
        var track = TestShow.Track("a");
        var project = TestShow.Project("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [project], project.Id));
        EnableGlobal(h);

        var seq = h.Submit(new NormalizeTrack(track.Id));

        Assert.Null(h.RejectionOf(seq));
        Assert.Empty(h.Engine.VoiceAudios);
    }

    [Fact]
    public void SetTrackAudioPlaying_PushesLiveVoice()
    {
        using var h = new Harness();
        var track = TestShow.Track("a");
        var project = TestShow.Project("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [project], project.Id));
        h.Submit(new PlayTrack(track.Id));
        var handle = h.Engine.Last!.Handle;

        var audio = new TrackAudioSettings(-3, 0.5, AudioEq.Flat);
        var seq = h.Submit(new SetTrackAudio(track.Id, audio));

        Assert.Null(h.RejectionOf(seq));
        var push = Assert.Single(h.Engine.VoiceAudios);
        Assert.Equal(handle, push.Handle);
        Assert.Equal(-3, push.Audio.GainDb);
    }

    [Fact]
    public void SetGlobalAudioPlaying_RepushesLiveVoice()
    {
        using var h = new Harness();
        var track = TestShow.Track("a");
        var project = TestShow.Project("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [project], project.Id));
        h.Submit(new PlayTrack(track.Id));
        h.Submit(new SetTrackAudio(track.Id, new TrackAudioSettings(0, 0, AudioEq.Flat, true, -10.0)));
        h.Engine.VoiceAudios.Clear();

        var seq = h.Submit(new SetGlobalAudio(GlobalAudioSettings.Default with { NormalizeEnabled = true, NormalizeTargetLufs = -23.0 }));

        Assert.Null(h.RejectionOf(seq));
        var push = Assert.Single(h.Engine.VoiceAudios);
        Assert.Equal(h.Engine.Last!.Handle, push.Handle);
        Assert.Equal(-10.0, push.Audio.MeasuredLufs);
    }

    [Fact]
    public void SetEntryAudio_UnrelatedEntry_TouchesNoVoice()
    {
        using var h = new Harness();
        var track = TestShow.Track("a");
        var entry = TestShow.Entry(track);
        var project = TestShow.Project("Main", entry);
        h.Submit(new LoadShow([track], [project], project.Id));
        h.Submit(new PlayTrack(track.Id));

        var seq = h.Submit(new SetEntryAudio(entry.Id, new TrackAudioSettings(-3, 0, AudioEq.Flat)));

        Assert.Null(h.RejectionOf(seq));
        Assert.Empty(h.Engine.VoiceAudios);
    }

    [Fact]
    public void NormalizeProject_MeasuresAndEnablesAll()
    {
        using var h = new Harness();
        h.Engine.ScanLufs = _ => -10.0;
        var a = TestShow.Track("a");
        var b = TestShow.Track("b");
        var project = TestShow.Project("Main", TestShow.Entry(a), TestShow.Entry(b));
        h.Submit(new LoadShow([a, b], [project], project.Id));
        EnableGlobal(h);

        var seq = h.Submit(new NormalizeProject(project.Id));

        Assert.Null(h.RejectionOf(seq));
        Assert.Equal(2, h.Engine.ScannedPaths.Count);
    }

    [Fact]
    public void NormalizeProject_SkipsFailedScans()
    {
        using var h = new Harness();
        h.Engine.ScanLufs = path => path.Contains("broken") ? double.NaN : -10.0;
        var a = TestShow.Track("a");
        var broken = TestShow.Track("broken");
        var project = TestShow.Project("Main", TestShow.Entry(a), TestShow.Entry(broken));
        h.Submit(new LoadShow([a, broken], [project], project.Id));
        EnableGlobal(h);

        var seq = h.Submit(new NormalizeProject(project.Id));

        Assert.Null(h.RejectionOf(seq));
        Assert.Equal(2, h.Engine.ScannedPaths.Count);
    }

    [Fact]
    public void NormalizeProject_AllFailed_Rejects()
    {
        using var h = new Harness();
        h.Engine.ScanLufs = _ => double.NaN;
        var a = TestShow.Track("a");
        var project = TestShow.Project("Main", TestShow.Entry(a));
        h.Submit(new LoadShow([a], [project], project.Id));
        EnableGlobal(h);

        var seq = h.Submit(new NormalizeProject(project.Id));

        Assert.Equal("lufs-scan-failed", h.RejectionOf(seq)!.Reason);
    }

    [Fact]
    public void NormalizeProject_UnknownProject_Rejects()
    {
        using var h = new Harness();
        EnableGlobal(h);

        var seq = h.Submit(new NormalizeProject(ProjectId.New()));

        Assert.Equal("unknown-playlist", h.RejectionOf(seq)!.Reason);
    }

    [Fact]
    public void NormalizeProject_DisabledGlobal_Rejects()
    {
        using var h = new Harness();
        var a = TestShow.Track("a");
        var project = TestShow.Project("Main", TestShow.Entry(a));
        h.Submit(new LoadShow([a], [project], project.Id));

        var seq = h.Submit(new NormalizeProject(project.Id));

        Assert.Equal("normalize-disabled", h.RejectionOf(seq)!.Reason);
        Assert.Empty(h.Engine.ScannedPaths);
    }

    [Fact]
    public void NormalizeProject_PlayingMember_PushesLiveVoice()
    {
        using var h = new Harness();
        h.Engine.ScanLufs = _ => -10.0;
        var a = TestShow.Track("a");
        var b = TestShow.Track("b");
        var project = TestShow.Project("Main", TestShow.Entry(a), TestShow.Entry(b));
        h.Submit(new LoadShow([a, b], [project], project.Id));
        EnableGlobal(h);
        h.Submit(new PlayTrack(b.Id));
        var handle = h.Engine.Last!.Handle;

        var seq = h.Submit(new NormalizeProject(project.Id));

        Assert.Null(h.RejectionOf(seq));
        var push = Assert.Single(h.Engine.VoiceAudios);
        Assert.Equal(handle, push.Handle);
        Assert.Equal(-10.0, push.Audio.MeasuredLufs);
    }

    [Fact]
    public void TrackTargetOverride_Validation()
    {
        Assert.Equal("lufs-out-of-range", AudioValidation.ValidateTrack(new TrackAudioSettings(0, 0, AudioEq.Flat, NormalizeTargetLufs: -5.0)));
        Assert.Equal("lufs-out-of-range", AudioValidation.ValidateTrack(new TrackAudioSettings(0, 0, AudioEq.Flat, NormalizeTargetLufs: -48.0)));
        Assert.Null(AudioValidation.ValidateTrack(new TrackAudioSettings(0, 0, AudioEq.Flat, NormalizeTargetLufs: -10.0)));
        Assert.Null(AudioValidation.ValidateTrack(TrackAudioSettings.Default));
    }

    [Fact]
    public void AddsToExistingGain()
    {
        Assert.Equal(-9.0, LufsNormalize.AdjustGain(-3, -10.0, -16.0), 9);
    }

    [Fact]
    public void ClampsToBounds()
    {
        Assert.Equal(12.0, LufsNormalize.AdjustGain(10, -20.0, -4.0), 9);
        Assert.Equal(-60.0, LufsNormalize.AdjustGain(-50, -4.0, -30.0), 9);
    }

    [Fact]
    public void ZeroOffset_KeepsGain()
    {
        Assert.Equal(-3.0, LufsNormalize.AdjustGain(-3, -16.0, -16.0), 9);
    }
}
