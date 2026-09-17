namespace Aria.App.Tests;

using Aria.App.Services;
using Aria.Audio;
using Aria.App.ViewModels;
using Aria.App.ViewModels.Settings;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;
using Aria.Core.State;

public sealed class AudioSettingsVmTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"aria-audio-row-{Guid.NewGuid():N}.json");
    private readonly string _hotkeysPath = Path.Combine(Path.GetTempPath(), $"aria-audio-hk-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_settingsPath))
        {
            File.Delete(_settingsPath);
        }
        if (File.Exists(_hotkeysPath))
        {
            File.Delete(_hotkeysPath);
        }
    }

    [Fact]
    public void AudioSection_EqChange_SubmitsExactGlobal()
    {
        var submitted = new List<Command>();
        var section = new AudioSectionVm(submitted.Add);

        section.EqBands[3].GainDb = 6;

        var global = Assert.IsType<SetGlobalAudio>(Assert.Single(submitted)).Value;
        Assert.Equal(6, global.Eq.Bands[3].GainDb);
        Assert.Equal(0, global.Eq.Bands[0].GainDb);
        Assert.Equal(AudioEq.DefaultFrequencies, global.Eq.Bands.Select(b => b.FrequencyHz).ToArray());
    }

    [Fact]
    public void AudioSection_EqGain_ClampsToRange()
    {
        var submitted = new List<Command>();
        var section = new AudioSectionVm(submitted.Add);

        section.EqBands[0].GainDb = 20;

        Assert.Equal(15, section.EqBands[0].GainDb);
        Assert.Equal(15, Assert.IsType<SetGlobalAudio>(Assert.Single(submitted)).Value.Eq.Bands[0].GainDb);
    }

    [Fact]
    public void AudioSection_Limiter_ClampsAndSubmits()
    {
        var submitted = new List<Command>();
        var section = new AudioSectionVm(submitted.Add);

        section.LimiterThresholdDb = -30;
        section.LimiterReleaseMs = 5000;

        Assert.Equal(-24, section.LimiterThresholdDb);
        Assert.Equal(1000, section.LimiterReleaseMs);
        Assert.All(submitted, c => Assert.IsType<SetGlobalAudio>(c));
        var last = Assert.IsType<SetGlobalAudio>(submitted[^1]).Value;
        Assert.Equal(-24, last.Limiter.ThresholdDb);
        Assert.Equal(1000, last.Limiter.ReleaseMs);
    }

    [Fact]
    public void AudioSection_PanAndHpf_Clamp()
    {
        var submitted = new List<Command>();
        var section = new AudioSectionVm(submitted.Add);

        section.Pan = 2;
        section.HpfHz = 500;

        Assert.Equal(1, section.Pan);
        Assert.Equal(400, section.HpfHz);
    }

    [Fact]
    public void AudioSection_NormalizeTarget_ClampsAndSubmits()
    {
        var submitted = new List<Command>();
        var section = new AudioSectionVm(submitted.Add);

        section.NormalizeTargetLufs = -48;

        Assert.Equal(-36, section.NormalizeTargetLufs);
        Assert.Equal(-36, Assert.IsType<SetGlobalAudio>(Assert.Single(submitted)).Value.NormalizeTargetLufs);
    }

    [Fact]
    public void AudioSection_Zones_Submit()
    {
        var submitted = new List<Command>();
        var section = new AudioSectionVm(submitted.Add);

        section.ZoneGreenDb = -18;
        section.ZoneYellowDb = -12;
        section.ZoneRedDb = -4;

        var last = Assert.IsType<SetGlobalAudio>(submitted[^1]).Value;
        Assert.Equal(new LufsMeterZones(-18, -12, -4), last.EffectiveZones);
    }

    [Fact]
    public void AudioSection_ApplyMixer_SyncsTargetAndZones()
    {
        var section = new AudioSectionVm(_ => { });
        var global = GlobalAudioSettings.Default with { NormalizeTargetLufs = -23.0, MeterZones = new LufsMeterZones(-18.0, -12.0, -4.0) };

        section.ApplyMixer(new MixerState(0, false, TimeSpan.FromMilliseconds(100), Smoothing.Default, global));

        Assert.Equal(-23.0, section.NormalizeTargetLufs);
        Assert.Equal(-18.0, section.ZoneGreenDb);
        Assert.Equal(-12.0, section.ZoneYellowDb);
        Assert.Equal(-4.0, section.ZoneRedDb);
    }

    [Fact]
    public void AudioSection_MeasureProject_SubmitsActiveId()
    {
        var submitted = new List<Command>();
        var id = ProjectId.New();
        var section = new AudioSectionVm(submitted.Add, () => id);

        section.MeasureProjectCommand.Execute(null);

        var command = Assert.IsType<NormalizeProject>(Assert.Single(submitted));
        Assert.Equal(id, command.Project);
        Assert.Equal("Замер выполняется…", section.NormalizeProjectStatus);

        section.OnShow();

        Assert.Equal("Готово", section.NormalizeProjectStatus);
    }

    [Fact]
    public void AudioSection_MeasureProject_WithoutActive_SubmitsNothing()
    {
        var submitted = new List<Command>();
        var section = new AudioSectionVm(submitted.Add, () => null);

        section.MeasureProjectCommand.Execute(null);

        Assert.Empty(submitted);
    }

    [Fact]
    public void TrackAudio_OwnTarget_SubmitsOverride()
    {
        var submitted = new List<Command>();
        var editor = new TrackAudioVm(submitted.Add, TrackId.New(), new TrackAudioSettings(0, 0, AudioEq.Flat, true, -20.0));

        editor.OwnTargetLufs = true;
        editor.TargetLufs = -10.0;

        var last = Assert.IsType<SetTrackAudio>(submitted[^1]);
        Assert.Equal(-10.0, last.Audio.NormalizeTargetLufs);
        Assert.Contains("+10.0", editor.NormalizeStatus.Replace(',', '.'));
    }

    [Fact]
    public void TrackAudio_OwnTargetOff_SubmitsNull()
    {
        var submitted = new List<Command>();
        var initial = new TrackAudioSettings(0, 0, AudioEq.Flat, true, -10.0, -12.0);
        var editor = new TrackAudioVm(submitted.Add, TrackId.New(), initial);

        Assert.True(editor.OwnTargetLufs);
        Assert.Equal(-12.0, editor.TargetLufs);

        editor.OwnTargetLufs = false;

        Assert.Null(Assert.IsType<SetTrackAudio>(submitted[^1]).Audio.NormalizeTargetLufs);
    }

    [Fact]
    public void AudioSection_Mono_Submits()
    {
        var submitted = new List<Command>();
        var section = new AudioSectionVm(submitted.Add);

        section.Mono = true;

        Assert.True(Assert.IsType<SetGlobalAudio>(Assert.Single(submitted)).Value.Mono);
    }

    [Fact]
    public void AudioSection_ApplyMixer_ReflectsWithoutSubmitting()
    {
        var submitted = new List<Command>();
        var section = new AudioSectionVm(submitted.Add);
        var eq = new AudioEq([.. AudioEq.DefaultFrequencies.Select((f, i) => new EqBand(f, i - 3, 1))]);

        section.ApplyMixer(new MixerState(0, false, TimeSpan.FromMilliseconds(100), Smoothing.Default, new GlobalAudioSettings(0.5, true, 120, eq, new LimiterSettings(false, -6, 200))));

        Assert.Equal(-3, section.EqBands[0].GainDb);
        Assert.Equal(3, section.EqBands[6].GainDb);
        Assert.Equal(0.5, section.Pan);
        Assert.True(section.Mono);
        Assert.Equal(120, section.HpfHz);
        Assert.False(section.LimiterEnabled);
        Assert.Empty(submitted);
    }

    [Fact]
    public void AudioSection_HasSevenBandsWithLabels()
    {
        var section = new AudioSectionVm(_ => { });

        Assert.Equal(7, section.EqBands.Count);
        Assert.Equal("63", section.EqBands[0].Label);
        Assert.Equal("12 кГц", section.EqBands[6].Label);
    }

    [Fact]
    public void AudioSection_PreviewGain_ClampsAndSubmits()
    {
        var submitted = new List<Command>();
        var section = new AudioSectionVm(submitted.Add);

        section.PreviewGainDb = -100;

        Assert.Equal(-80, section.PreviewGainDb);
        Assert.Equal(-80, Assert.IsType<SetPreviewGain>(Assert.Single(submitted)).GainDb);
    }

    [Fact]
    public void AudioSection_PreviewMuted_Submits()
    {
        var submitted = new List<Command>();
        var section = new AudioSectionVm(submitted.Add);

        section.PreviewMuted = true;

        Assert.True(Assert.IsType<SetPreviewMuted>(Assert.Single(submitted)).Muted);
    }

    [Fact]
    public void TrackAudio_TrackMode_SubmitsSetTrackAudio()
    {
        var submitted = new List<Command>();
        var trackId = TrackId.New();
        var editor = new TrackAudioVm(submitted.Add, trackId);

        editor.GainDb = -6;
        editor.Pan = -0.5;
        editor.EqBands[1].GainDb = 3;

        var last = Assert.IsType<SetTrackAudio>(submitted[^1]);
        Assert.Equal(trackId, last.Track);
        Assert.Equal(-6, last.Audio.GainDb);
        Assert.Equal(-0.5, last.Audio.Pan);
        Assert.Equal(3, last.Audio.Eq.Bands[1].GainDb);
    }

    [Fact]
    public void TrackAudio_Gain_Clamps()
    {
        var submitted = new List<Command>();
        var editor = new TrackAudioVm(submitted.Add, TrackId.New());

        editor.GainDb = -100;

        Assert.Equal(-60, editor.GainDb);
        Assert.Equal(-60, Assert.IsType<SetTrackAudio>(Assert.Single(submitted)).Audio.GainDb);
    }

    [Fact]
    public void TrackAudio_EntryMode_SubmitsSetEntryAudio()
    {
        var submitted = new List<Command>();
        var entryId = EntryId.New();
        var editor = new TrackAudioVm(submitted.Add, TrackId.New(), entryId: entryId);

        editor.GainDb = 2;

        var command = Assert.IsType<SetEntryAudio>(Assert.Single(submitted));
        Assert.Equal(entryId, command.Entry);
        Assert.Equal(2, command.Audio!.GainDb);
    }

    [Fact]
    public void TrackAudio_EntryInherit_SubmitsNull()
    {
        var submitted = new List<Command>();
        var editor = new TrackAudioVm(submitted.Add, TrackId.New(), entryId: EntryId.New());

        editor.InheritTrackSettings = true;

        var command = Assert.IsType<SetEntryAudio>(Assert.Single(submitted));
        Assert.Null(command.Audio);
    }

    [Fact]
    public void TrackAudio_DefaultsToFlat()
    {
        var editor = new TrackAudioVm(_ => { }, TrackId.New());

        Assert.Equal(0, editor.GainDb);
        Assert.Equal(0, editor.Pan);
        Assert.All(editor.EqBands, b => Assert.Equal(0, b.GainDb));
        Assert.Equal(7, editor.EqBands.Count);
    }

    [Fact]
    public void TrackAudio_PreviewCommands_Submit()
    {
        var submitted = new List<Command>();
        var trackId = TrackId.New();
        var editor = new TrackAudioVm(submitted.Add, trackId);

        editor.PreviewCommand.Execute(null);
        editor.StopPreviewCommand.Execute(null);

        Assert.Equal(trackId, Assert.IsType<StartPreviewTrack>(submitted[0]).Track);
        Assert.IsType<StopPreview>(submitted[1]);
    }

    [Fact]
    public void TrackAudio_Normalize_InvokesHookAndEnables()
    {
        var submitted = new List<Command>();
        var trackId = TrackId.New();
        var editor = new TrackAudioVm(submitted.Add, trackId);
        var calls = new List<TrackId>();
        editor.NormalizeRequest = calls.Add;

        editor.NormalizeCommand.Execute(null);

        Assert.Equal(trackId, Assert.Single(calls));
        Assert.True(editor.NormalizeEnabled);
        var toggle = Assert.IsType<SetTrackAudio>(submitted[0]);
        Assert.True(toggle.Audio.NormalizeEnabled);
    }

    [Fact]
    public void TrackAudio_NormalizeToggle_SubmitsAndReportsStatus()
    {
        var submitted = new List<Command>();
        var editor = new TrackAudioVm(submitted.Add, TrackId.New());

        Assert.Equal("Нормализация выключена", editor.NormalizeStatus);

        editor.NormalizeEnabled = true;

        Assert.Equal("Включена, измерение не выполнено", editor.NormalizeStatus);

        var measured = new TrackAudioVm(submitted.Add, TrackId.New(), new TrackAudioSettings(0, 0, AudioEq.Flat, true, -10.0), null, -16.0);

        Assert.Equal("Измерено -10.0 LUFS, поправка -6.0 дБ", measured.NormalizeStatus);
    }

    [Fact]
    public void TrackAudio_Normalize_GlobalOff_DoesNothing()
    {
        var submitted = new List<Command>();
        var editor = new TrackAudioVm(submitted.Add, TrackId.New(), null, null, -16.0, false);
        var calls = 0;
        editor.NormalizeRequest = _ => calls++;

        editor.NormalizeCommand.Execute(null);

        Assert.Equal(0, calls);
        Assert.Empty(submitted);
        Assert.Equal("Выключена в настройках Звука", editor.NormalizeStatus);
    }

    [Fact]
    public void TrackAudio_RefreshMeasurement_UpdatesStatus()
    {
        double? measured = null;
        var editor = new TrackAudioVm(_ => { }, TrackId.New(), new TrackAudioSettings(0, 0, AudioEq.Flat, true, null), null, -16.0, true, () => measured);

        Assert.Equal("Включена, измерение не выполнено", editor.NormalizeStatus);

        measured = -10.0;
        editor.RefreshMeasurement();

        Assert.Equal("Измерено -10.0 LUFS, поправка -6.0 дБ", editor.NormalizeStatus);
    }

    [Fact]
    public void TrackAudio_Normalize_WithoutHook_DoesNotThrow()
    {
        var editor = new TrackAudioVm(_ => { }, TrackId.New());

        editor.NormalizeCommand.Execute(null);
    }

    [Fact]
    public void TrackAudio_Normalize_DefaultHook_SubmitsCommand()
    {
        var submitted = new List<Command>();
        var trackId = TrackId.New();
        var editor = new TrackAudioVm(submitted.Add, trackId);

        editor.NormalizeCommand.Execute(null);

        var toggle = Assert.IsType<SetTrackAudio>(submitted[0]);
        Assert.True(toggle.Audio.NormalizeEnabled);
        var command = Assert.IsType<NormalizeTrack>(Assert.Single(submitted, c => c is NormalizeTrack));
        Assert.Equal(trackId, command.Track);
    }

    [Fact]
    public void AudioSection_Outputs_ListsDevicesAndSelection()
    {
        var service = new AudioOutputService(
            new OutputStubLister(() => [new OutputDevice("a", "Speakers", true), new OutputDevice("b", "Headphones", false)]),
            new AppSettingsStore(_settingsPath));
        var section = new AudioSectionVm(_ => { }, () => null, service);

        Assert.Equal(["", "a", "b"], section.Outputs.Select(d => d.Id));
        Assert.Equal(service.SelectedId, section.SelectedOutputId);
    }

    [Fact]
    public void AudioSection_SelectOutput_RoutesToService()
    {
        var service = new AudioOutputService(
            new OutputStubLister(() => [new OutputDevice("a", "Speakers", true), new OutputDevice("b", "Headphones", false)]),
            new AppSettingsStore(_settingsPath));
        var section = new AudioSectionVm(_ => { }, () => null, service);

        section.SelectedOutputId = "b";

        Assert.Equal("b", service.SelectedId);
    }

    [Fact]
    public void AudioSection_SelectPreviewOutput_RoutesToService()
    {
        var service = new AudioOutputService(
            new OutputStubLister(() => [new OutputDevice("a", "Speakers", true), new OutputDevice("b", "Headphones", false)]),
            new AppSettingsStore(_settingsPath));
        var section = new AudioSectionVm(_ => { }, () => null, service);

        section.SelectedPreviewOutputId = "b";

        Assert.Equal("b", service.SelectedPreviewId);
        Assert.Equal(AudioOutputService.SystemDefaultId, service.SelectedId);
    }

    [Fact]
    public void SettingsViewModel_ExposesAudio_AndSyncsFromMixer()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        using var viewModel = new SettingsViewModel(
            bus, new HotkeyService(HotkeyConfig.Default, _ => { }), _hotkeysPath,
            new AppSettingsStore(_settingsPath));

        Assert.NotNull(viewModel.Audio);
        bus.Submit(new ClientId("setup"), 7, new SetGlobalAudio(new GlobalAudioSettings(-0.5, false, 80, AudioEq.Flat, LimiterSettings.Default)));

        Assert.Equal(-0.5, viewModel.Audio.Pan);
        Assert.Equal(80, viewModel.Audio.HpfHz);
    }

    private sealed class OutputStubLister(Func<IReadOnlyList<OutputDevice>> list) : IAudioOutputLister
    {
        public IReadOnlyList<OutputDevice> ListPlaybackDevices() => list();
    }
}
