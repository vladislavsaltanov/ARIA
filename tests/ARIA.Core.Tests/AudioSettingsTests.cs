namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.State;

public sealed class AudioSettingsTests
{
    [Fact]
    public void Flat_HasSevenBandsAtDefaultFrequencies()
    {
        Assert.Equal(7, AudioEq.Flat.Bands.Length);
        Assert.Equal(AudioEq.DefaultFrequencies, AudioEq.Flat.Bands.Select(b => b.FrequencyHz).ToArray());
        Assert.All(AudioEq.Flat.Bands, b => Assert.Equal(0, b.GainDb));
    }

    [Fact]
    public void AudioEq_WrongBandCount_Throws()
    {
        Assert.Throws<ArgumentException>(() => new AudioEq([]));
        Assert.Throws<ArgumentException>(() => new AudioEq([.. AudioEq.Flat.Bands, new EqBand(15000, 0, 1)]));
    }

    [Fact]
    public void Resolve_NoOverrides_InheritsTrackAudio()
    {
        var audio = new TrackAudioSettings(-3, 0.5, AudioEq.Flat);
        var track = TestShow.Track("a") with { Defaults = TestShow.Track("a").Defaults with { Audio = audio } };
        var entry = TestShow.Entry(track);

        var settings = EffectiveSettings.Resolve(entry, track);

        Assert.Equal(audio, settings.Audio);
    }

    [Fact]
    public void Resolve_EntryOverride_WinsOverTrack()
    {
        var trackAudio = new TrackAudioSettings(-3, 0, AudioEq.Flat);
        var entryAudio = new TrackAudioSettings(2, -0.5, AudioEq.Flat);
        var track = TestShow.Track("a") with { Defaults = TestShow.Track("a").Defaults with { Audio = trackAudio } };
        var entry = TestShow.Entry(track, new PlaylistOverrides(Audio: entryAudio));

        var settings = EffectiveSettings.Resolve(entry, track);

        Assert.Equal(entryAudio, settings.Audio);
    }

    [Fact]
    public void ForTrack_UsesTrackAudio()
    {
        var audio = new TrackAudioSettings(-6, 0.25, AudioEq.Flat);
        var track = TestShow.Track("a") with { Defaults = TestShow.Track("a").Defaults with { Audio = audio } };

        Assert.Equal(audio, EffectiveSettings.ForTrack(track).Audio);
    }

    [Fact]
    public void Resolve_NoAudioAnywhere_FallsBackToDefault()
    {
        var track = TestShow.Track("a");

        Assert.Equal(TrackAudioSettings.Default, EffectiveSettings.Resolve(TestShow.Entry(track), track).Audio);
        Assert.Equal(TrackAudioSettings.Default, EffectiveSettings.ForTrack(track).Audio);
    }

    [Theory]
    [InlineData(1.5, "pan-out-of-range")]
    [InlineData(-1.5, "pan-out-of-range")]
    public void ValidateGlobal_BadPan_ReturnsReason(double pan, string reason)
    {
        Assert.Equal(reason, AudioValidation.ValidateGlobal(GlobalAudioSettings.Default with { Pan = pan }));
    }

    [Fact]
    public void ValidateGlobal_BadHpf_ReturnsReason()
    {
        Assert.Equal("hpf-out-of-range", AudioValidation.ValidateGlobal(GlobalAudioSettings.Default with { HpfHz = 500 }));
        Assert.Equal("hpf-out-of-range", AudioValidation.ValidateGlobal(GlobalAudioSettings.Default with { HpfHz = -10 }));
    }

    [Fact]
    public void ValidateGlobal_BadLimiter_ReturnsReason()
    {
        var badThreshold = GlobalAudioSettings.Default with { Limiter = new LimiterSettings(true, 3, 100) };
        var badRelease = GlobalAudioSettings.Default with { Limiter = new LimiterSettings(true, -6, 5000) };
        Assert.Equal("limiter-out-of-range", AudioValidation.ValidateGlobal(badThreshold));
        Assert.Equal("limiter-out-of-range", AudioValidation.ValidateGlobal(badRelease));
    }

    [Fact]
    public void ValidateGlobal_BadEqGain_ReturnsReason()
    {
        var bands = AudioEq.Flat.Bands.SetItem(3, new EqBand(1000, 20, 1));
        Assert.Equal("eq-out-of-range", AudioValidation.ValidateGlobal(GlobalAudioSettings.Default with { Eq = new AudioEq(bands) }));
    }

    [Fact]
    public void ValidateGlobal_Valid_ReturnsNull()
    {
        Assert.Null(AudioValidation.ValidateGlobal(GlobalAudioSettings.Default));
    }

    [Fact]
    public void ValidateTrack_BadGain_ReturnsReason()
    {
        Assert.Equal("gain-out-of-range", AudioValidation.ValidateTrack(new TrackAudioSettings(-70, 0, AudioEq.Flat)));
        Assert.Equal("gain-out-of-range", AudioValidation.ValidateTrack(new TrackAudioSettings(20, 0, AudioEq.Flat)));
    }

    [Fact]
    public void ValidateTrack_BadPan_ReturnsReason()
    {
        Assert.Equal("pan-out-of-range", AudioValidation.ValidateTrack(new TrackAudioSettings(0, 2, AudioEq.Flat)));
    }

    [Fact]
    public void ValidateTrack_Valid_ReturnsNull()
    {
        Assert.Null(AudioValidation.ValidateTrack(TrackAudioSettings.Default));
    }

    [Fact]
    public void MixerState_Default_GlobalIsDefault()
    {
        using var h = new Harness();

        Assert.Equal(GlobalAudioSettings.Default, h.Snapshot.Mixer.Global);
    }

    [Fact]
    public void SetGlobalAudio_EmitsMixerDeltaCarryingValue()
    {
        using var h = new Harness();
        var value = GlobalAudioSettings.Default with { Pan = -0.5, Mono = true, HpfHz = 80 };

        var seq = h.Submit(new SetGlobalAudio(value));

        Assert.Null(h.RejectionOf(seq));
        Assert.Equal(value, h.Snapshot.Mixer.Global);
        Assert.Equal(value, h.Events.OfType<MixerDelta>().Last().State.Global);
    }

    [Fact]
    public void SetGlobalAudio_BadPan_RejectsKeepsOld()
    {
        using var h = new Harness();

        var seq = h.Submit(new SetGlobalAudio(GlobalAudioSettings.Default with { Pan = 2 }));

        Assert.Equal("pan-out-of-range", h.RejectionOf(seq)!.Reason);
        Assert.Equal(GlobalAudioSettings.Default, h.Snapshot.Mixer.Global);
    }

    [Fact]
    public void SetGlobalAudio_BadLimiter_Rejects()
    {
        using var h = new Harness();
        var value = GlobalAudioSettings.Default with { Limiter = new LimiterSettings(true, -6, 5000) };

        var seq = h.Submit(new SetGlobalAudio(value));

        Assert.Equal("limiter-out-of-range", h.RejectionOf(seq)!.Reason);
    }

    [Fact]
    public void SetTrackAudio_UpdatesTrackDefaults_EmitsShow()
    {
        using var h = new Harness();
        var track = TestShow.Track("a");
        var playlist = TestShow.Playlist("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [playlist], playlist.Id));
        var audio = new TrackAudioSettings(-4, 0.5, AudioEq.Flat);

        var seq = h.Submit(new SetTrackAudio(track.Id, audio));

        Assert.Null(h.RejectionOf(seq));
        Assert.NotEmpty(h.Events.OfType<ShowDelta>());
        var resolved = EffectiveSettings.ForTrack(h.Snapshot.Show.Playlists[0].Entries[0].TrackId == track.Id ? track with { Defaults = track.Defaults with { Audio = audio } } : track);
        Assert.Equal(audio, resolved.Audio);
    }

    [Fact]
    public void SetTrackAudio_UnknownTrack_Rejects()
    {
        using var h = new Harness();

        var seq = h.Submit(new SetTrackAudio(TrackId.New(), TrackAudioSettings.Default));

        Assert.Equal("unknown-track", h.RejectionOf(seq)!.Reason);
    }

    [Fact]
    public void SetTrackAudio_BadGain_Rejects()
    {
        using var h = new Harness();
        var track = TestShow.Track("a");
        var playlist = TestShow.Playlist("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [playlist], playlist.Id));

        var seq = h.Submit(new SetTrackAudio(track.Id, new TrackAudioSettings(-70, 0, AudioEq.Flat)));

        Assert.Equal("gain-out-of-range", h.RejectionOf(seq)!.Reason);
    }

    [Fact]
    public void SetEntryAudio_SetsOverride_Resolves()
    {
        using var h = new Harness();
        var trackAudio = new TrackAudioSettings(-3, 0, AudioEq.Flat);
        var track = TestShow.Track("a") with { Defaults = TestShow.Track("a").Defaults with { Audio = trackAudio } };
        var entry = TestShow.Entry(track);
        var playlist = TestShow.Playlist("Main", entry);
        h.Submit(new LoadShow([track], [playlist], playlist.Id));
        var entryAudio = new TrackAudioSettings(2, -0.5, AudioEq.Flat);

        var seq = h.Submit(new SetEntryAudio(entry.Id, entryAudio));

        Assert.Null(h.RejectionOf(seq));
        var stored = h.Snapshot.Show.Playlists[0].Entries[0];
        Assert.Equal(entryAudio, stored.Overrides!.Audio);
        Assert.Equal(entryAudio, EffectiveSettings.Resolve(stored, track with { Defaults = track.Defaults }).Audio);
    }

    [Fact]
    public void SetEntryAudio_Null_ClearsToInherited()
    {
        using var h = new Harness();
        var trackAudio = new TrackAudioSettings(-3, 0, AudioEq.Flat);
        var track = TestShow.Track("a") with { Defaults = TestShow.Track("a").Defaults with { Audio = trackAudio } };
        var entry = TestShow.Entry(track, new PlaylistOverrides(Audio: new TrackAudioSettings(2, 0, AudioEq.Flat)));
        var playlist = TestShow.Playlist("Main", entry);
        h.Submit(new LoadShow([track], [playlist], playlist.Id));

        var seq = h.Submit(new SetEntryAudio(entry.Id, null));

        Assert.Null(h.RejectionOf(seq));
        var stored = h.Snapshot.Show.Playlists[0].Entries[0];
        Assert.Null(stored.Overrides?.Audio);
        Assert.Equal(trackAudio, EffectiveSettings.Resolve(stored, track).Audio);
    }

    [Fact]
    public void SetEntryAudio_UnknownEntry_Rejects()
    {
        using var h = new Harness();

        var seq = h.Submit(new SetEntryAudio(EntryId.New(), TrackAudioSettings.Default));

        Assert.Equal("unknown-entry", h.RejectionOf(seq)!.Reason);
    }

    [Fact]
    public void SetEntryAudio_BadPan_Rejects()
    {
        using var h = new Harness();
        var track = TestShow.Track("a");
        var entry = TestShow.Entry(track);
        var playlist = TestShow.Playlist("Main", entry);
        h.Submit(new LoadShow([track], [playlist], playlist.Id));

        var seq = h.Submit(new SetEntryAudio(entry.Id, new TrackAudioSettings(0, 2, AudioEq.Flat)));

        Assert.Equal("pan-out-of-range", h.RejectionOf(seq)!.Reason);
    }
}
