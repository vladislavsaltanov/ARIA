namespace Aria.App.Tests;

using Aria.App.Services;
using Aria.App.ViewModels;
using Aria.Audio;
using Aria.Core.Runtime;
using Aria.Persistence;

public sealed class LibraryViewModelTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aria-library-vm-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryViewModel_ReactsToImport()
    {
        var wav = TestWav.Write(_directory, "vm-track.wav");
        using var library = new SqliteLibraryStore(Path.Combine(_directory, "library.db"));
        using var waveforms = new SqliteWaveformStore(Path.Combine(_directory, "waveforms.db"));
        var factory = new MiniaudioSourceFactory(8000, 1);
        var importer = new TrackImporter(factory, new WaveformScanner(factory));
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        using var vm = new LibraryViewModel(bus, library, waveforms, importer);

        await vm.ImportAsync([wav]);

        Assert.False(vm.IsBusy);
        var track = Assert.Single(vm.Tracks);
        Assert.Equal("vm-track", track.Name);
        Assert.InRange(track.Duration.TotalSeconds, 0.95, 1.05);
        Assert.Equal(track.Id, vm.Selected?.Id);

        vm.EnqueueTrack(vm.Tracks[0]);

        var item = Assert.Single(bus.Snapshot().Queue.Items);
        Assert.Equal(vm.Tracks[0].Id, item.TrackId);
    }

    [Fact]
    public async Task ImportAsync_SavesPeaks_AndDeduplicatesByPath()
    {
        var wav = TestWav.Write(_directory, "dedupe.wav");
        using var library = new SqliteLibraryStore(Path.Combine(_directory, "library.db"));
        using var waveforms = new SqliteWaveformStore(Path.Combine(_directory, "waveforms.db"));
        var factory = new MiniaudioSourceFactory(8000, 1);
        var importer = new TrackImporter(factory, new WaveformScanner(factory));
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        using var vm = new LibraryViewModel(bus, library, waveforms, importer);

        await vm.ImportAsync([wav]);
        await vm.ImportAsync([wav]);

        Assert.Single(vm.Tracks);
        Assert.Single(library.Load().Tracks);
        var stored = library.Load().Tracks[0];
        Assert.NotNull(waveforms.Load(stored.Id));
    }
}
