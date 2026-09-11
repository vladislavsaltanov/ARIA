namespace Aria.App.Tests;

using Aria.App.ViewModels;
using Aria.Audio;
using Aria.Core.Commands;

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
    public async Task ImportAsync_PopulatesTracks_AndReportsCounts()
    {
        await using var host = new AppHost(_directory, null, () => new NullSink(8000, 1), () => new MiniaudioSourceFactory(8000, 1));
        await host.StartAsync();
        using var vm = new LibraryViewModel(host.Bus, host.Library!, host.ImportTracksAsync);
        var wav = TestWav.Write(_directory, "vm-track.wav");

        await vm.ImportAsync([wav]);

        Assert.False(vm.IsBusy);
        var track = Assert.Single(vm.Tracks);
        Assert.Equal("vm-track", track.Name);
        Assert.InRange(track.Duration.TotalSeconds, 0.95, 1.05);
        Assert.Equal(track.Id, vm.Selected?.Id);
        Assert.Equal("добавлено 1, пропущено 0", vm.StatusText);

        vm.EnqueueTrack(vm.Tracks[0]);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline && host.Bus.Snapshot().Queue.Items.Length == 0)
        {
            await Task.Delay(25);
        }
        var item = Assert.Single(host.Bus.Snapshot().Queue.Items);
        Assert.Equal(vm.Tracks[0].Id, item.TrackId);
    }

    [Fact]
    public async Task ImportAsync_Duplicate_ReportsSkipped()
    {
        await using var host = new AppHost(_directory, null, () => new NullSink(8000, 1), () => new MiniaudioSourceFactory(8000, 1));
        await host.StartAsync();
        using var vm = new LibraryViewModel(host.Bus, host.Library!, host.ImportTracksAsync);
        var wav = TestWav.Write(_directory, "dedupe.wav");

        await vm.ImportAsync([wav]);
        await vm.ImportAsync([wav]);

        Assert.Single(vm.Tracks);
        Assert.Single(host.Library!.Load().Tracks);
        Assert.Equal("добавлено 0, пропущено 1", vm.StatusText);
        Assert.NotNull(host.Waveforms!.Load(vm.Tracks[0].Id));
    }

    [Fact]
    public async Task SearchText_FiltersTracks_BySubstring()
    {
        await using var host = new AppHost(_directory, null, () => new NullSink(8000, 1), () => new MiniaudioSourceFactory(8000, 1));
        await host.StartAsync();
        using var vm = new LibraryViewModel(host.Bus, host.Library!, host.ImportTracksAsync);
        await vm.ImportAsync([TestWav.Write(_directory, "alpha-song.wav"), TestWav.Write(_directory, "beta-song.wav")]);
        Assert.Equal(2, vm.FilteredTracks.Count);

        vm.SearchText = "alpha";

        var found = Assert.Single(vm.FilteredTracks);
        Assert.Contains("alpha", found.Name, StringComparison.OrdinalIgnoreCase);

        vm.SearchText = string.Empty;

        Assert.Equal(2, vm.FilteredTracks.Count);
    }

}
