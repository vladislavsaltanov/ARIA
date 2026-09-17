namespace Aria.App.Tests;

using Aria.App.Services;
using Aria.App.ViewModels;
using Aria.Core.Model;
using Aria.Core.Runtime;

public sealed class SettingsEndActionTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"aria-endaction-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public void DefaultEndActionIndex_DefaultsToAdvance()
    {
        using var bus = NewBus();
        using var viewModel = NewSettings(bus);

        Assert.Equal(3, viewModel.Playback.DefaultEndActionIndex);
    }

    [Fact]
    public void DefaultEndActionIndex_Change_SubmitsAndPersists()
    {
        using var bus = NewBus();
        using var viewModel = NewSettings(bus);

        viewModel.Playback.DefaultEndActionIndex = 0;

        Assert.Equal(0, viewModel.Playback.DefaultEndActionIndex);
        Assert.Equal(EndAction.Pause, new AppSettingsStore(_path).Load().DefaultEndAction);
    }

    [Fact]
    public void DefaultEndActionIndex_ReloadsStoredValue()
    {
        new AppSettingsStore(_path).Save(new AppSettings(false, "{name}", Smoothing.Default, EndAction.Replay));
        using var bus = NewBus();
        using var viewModel = NewSettings(bus);

        Assert.Equal(2, viewModel.Playback.DefaultEndActionIndex);
    }

    [Fact]
    public void DefaultEndActionIndex_LegacyFileWithoutField_FallsBackToAdvance()
    {
        File.WriteAllText(_path, """{"useFileName":false,"rowFormat":"{name}","smoothing":null}""");
        using var bus = NewBus();
        using var viewModel = NewSettings(bus);

        Assert.Equal(3, viewModel.Playback.DefaultEndActionIndex);
        Assert.Equal(EndAction.Advance, new AppSettingsStore(_path).Load().DefaultEndAction);
    }

    [Fact]
    public void MeterSmoothing_DefaultsToEnabled250ms()
    {
        using var bus = NewBus();
        using var viewModel = NewSettings(bus);

        Assert.True(viewModel.Playback.MeterSmoothingEnabled);
        Assert.Equal(250, viewModel.Playback.MeterSmoothingReleaseMs);
    }

    [Fact]
    public void MeterSmoothing_Change_PersistsAndNotifies()
    {
        using var bus = NewBus();
        AppSettings? applied = null;
        using var viewModel = new SettingsViewModel(
            bus,
            new HotkeyService(HotkeyConfig.Default, _ => { }),
            Path.Combine(Path.GetTempPath(), $"aria-hk-{Guid.NewGuid():N}.json"),
            new AppSettingsStore(_path),
            s => applied = s);

        viewModel.Playback.MeterSmoothingEnabled = false;
        viewModel.Playback.MeterSmoothingReleaseMs = 500;

        Assert.Equal(new MeterSmoothing(false, 500), new AppSettingsStore(_path).Load().MeterSmoothing);
        Assert.Equal(new MeterSmoothing(false, 500), applied?.MeterSmoothing);
    }

    [Fact]
    public void MeterSmoothing_LegacyFileWithoutField_FallsBackToDefault()
    {
        File.WriteAllText(_path, """{"useFileName":false,"rowFormat":"{name}","smoothing":null}""");
        using var bus = NewBus();
        using var viewModel = NewSettings(bus);

        Assert.True(viewModel.Playback.MeterSmoothingEnabled);
        Assert.Equal(250, viewModel.Playback.MeterSmoothingReleaseMs);
    }

    private static CommandBus NewBus() => new(new ShowController(new StubEngine()), BusMode.Inline);

    private SettingsViewModel NewSettings(CommandBus bus) => new(
        bus,
        new HotkeyService(HotkeyConfig.Default, _ => { }),
        Path.Combine(Path.GetTempPath(), $"aria-hk-{Guid.NewGuid():N}.json"),
        new AppSettingsStore(_path));
}
