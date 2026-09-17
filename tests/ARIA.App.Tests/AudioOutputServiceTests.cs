namespace Aria.App.Tests;

using Aria.App.Services;
using Aria.Audio;

public sealed class AudioOutputServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aria-output-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Devices_ListsSystemDefaultFirst()
    {
        var service = new AudioOutputService(new StubLister(() => [new OutputDevice("a", "Speakers", true)]), Store());

        Assert.Equal(AudioOutputService.SystemDefaultId, Assert.Single(service.Devices, d => d.Name == "System").Id);
        Assert.Equal("Speakers", service.Devices[1].Name);
    }

    [Fact]
    public void Select_KnownDevice_PersistsAndRestores()
    {
        var store = Store();
        var selecting = new AudioOutputService(new StubLister(Devices), store);
        Assert.True(selecting.Select("b"));

        var restored = new AudioOutputService(new StubLister(Devices), store);

        Assert.Equal("b", restored.SelectedId);
    }

    [Fact]
    public void Select_UnknownId_ReturnsFalse()
    {
        var service = new AudioOutputService(new StubLister(Devices), Store());

        Assert.False(service.Select("nope"));
        Assert.Equal(AudioOutputService.SystemDefaultId, service.SelectedId);
    }

    [Fact]
    public void Refresh_VanishedDevice_FallsBackToSystem()
    {
        var current = Devices();
        var service = new AudioOutputService(new StubLister(() => current), Store());
        Assert.True(service.Select("b"));
        current = [new OutputDevice("a", "Speakers", true)];

        service.Refresh();

        Assert.Equal(AudioOutputService.SystemDefaultId, service.SelectedId);
        Assert.DoesNotContain(service.Devices, d => d.Id == "b");
    }

    [Fact]
    public void SelectPreview_KnownDevice_PersistsWithoutTouchingMain()
    {
        var store = Store();
        var selecting = new AudioOutputService(new StubLister(Devices), store);
        Assert.True(selecting.SelectPreview("b"));

        var restored = new AudioOutputService(new StubLister(Devices), store);

        Assert.Equal("b", restored.SelectedPreviewId);
        Assert.Equal(AudioOutputService.SystemDefaultId, restored.SelectedId);
    }

    [Fact]
    public void Refresh_VanishedPreviewDevice_FallsBackToSystem()
    {
        var current = Devices();
        var service = new AudioOutputService(new StubLister(() => current), Store());
        Assert.True(service.SelectPreview("b"));
        current = [new OutputDevice("a", "Speakers", true)];

        service.Refresh();

        Assert.Equal(AudioOutputService.SystemDefaultId, service.SelectedPreviewId);
    }

    private static IReadOnlyList<OutputDevice> Devices() =>
        [new OutputDevice("a", "Speakers", true), new OutputDevice("b", "Headphones", false)];

    private AppSettingsStore Store() => new(Path.Combine(_directory, "settings.json"));

    private sealed class StubLister(Func<IReadOnlyList<OutputDevice>> list) : IAudioOutputLister
    {
        public IReadOnlyList<OutputDevice> ListPlaybackDevices() => list();
    }
}
