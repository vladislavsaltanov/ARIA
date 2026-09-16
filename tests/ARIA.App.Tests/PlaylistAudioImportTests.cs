namespace Aria.App.Tests;

using Aria.App.ViewModels;
using Aria.Audio;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;

public sealed class ProjectAudioImportTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aria-playlist-drop-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task ImportAudioFilesAsync_ExternalFile_AddsTrackAndProjectEntry()
    {
        await using var host = new AppHost(_directory, null, () => new NullSink(8000, 1), () => new MiniaudioSourceFactory(8000, 1));
        await host.StartAsync();
        host.Submit(new CreateProject("DropTarget"));
        await PollAsync(() => host.Bus.Snapshot().Show.Projects.Length == 1);
        using var projects = new ProjectsViewModel(
            host.Bus,
            () => host.Library!.Load().Tracks,
            audioImport: host.ImportTracksAsync);
        await PollAsync(() => projects.SelectedProject is not null);

        var external = TestWav.Write(Path.Combine(_directory, "incoming"), "external-drop.wav");
        var ids = await projects.ImportAudioFilesAsync([external]);

        var trackId = Assert.Single(ids);
        await PollAsync(() => host.Bus.Snapshot().Show.Projects[0].Entries.Length == 1);
        var entries = host.Bus.Snapshot().Show.Projects[0].Entries;
        Assert.Equal(trackId, entries[0].TrackId);
        Assert.Contains(host.Library!.Load().Tracks, t => t.Id == trackId);
    }

    [Fact]
    public async Task ImportAudioFilesAsync_UnmatchedFile_RaisesIncompleteEvent()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var track = new Track(TrackId.New(), "/audio/test.flac", "test", TimeSpan.FromMinutes(3), new TrackDefaults());
        var project = new Project(ProjectId.New(), "Main", []);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([track], [project], project.Id));
        using var vm = new ProjectsViewModel(bus, () => [track],
            audioImport: (_, _) => Task.FromResult(new Aria.App.ImportReport(0, 0, ["/audio/missing.wav"])));
        string? message = null;
        vm.AudioImportIncomplete += m => message = m;

        var ids = await vm.ImportAudioFilesAsync(["/audio/missing.wav"]);

        Assert.Empty(ids);
        Assert.Equal("импортировано: 0, пропущено: 0, ошибок: 1", vm.ProjectIoStatus);
        Assert.Contains("missing.wav", message);
    }

    [Fact]
    public async Task ImportAudioFilesAsync_WithoutImporter_StatusClearsAfterTtl()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var track = new Track(TrackId.New(), "/audio/test.flac", "test", TimeSpan.FromMinutes(3), new TrackDefaults());
        var project = new Project(ProjectId.New(), "Main", []);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([track], [project], project.Id));
        using var vm = new ProjectsViewModel(bus, () => [track], transientStatusTtl: TimeSpan.FromMilliseconds(50));

        var ids = await vm.ImportAudioFilesAsync(["/audio/new.wav"]);

        Assert.Empty(ids);
        Assert.Equal("импорт недоступен", vm.ProjectIoStatus);
        await Task.Delay(500);
        Assert.Equal(string.Empty, vm.ProjectIoStatus);
    }

    [Fact]
    public async Task ImportAudioFilesAsync_PartialMatch_ReportsCounts_AndRaises()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var track = new Track(TrackId.New(), "/audio/test.flac", "test", TimeSpan.FromMinutes(3), new TrackDefaults());
        var project = new Project(ProjectId.New(), "Main", []);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([track], [project], project.Id));
        using var vm = new ProjectsViewModel(bus, () => [track],
            audioImport: (_, _) => Task.FromResult(new Aria.App.ImportReport(1, 0, ["/audio/missing.wav"])));
        string? message = null;
        vm.AudioImportIncomplete += m => message = m;

        var ids = await vm.ImportAudioFilesAsync([track.FilePath, "/audio/missing.wav"]);

        Assert.Single(ids);
        Assert.Equal("импортировано: 1, пропущено: 0, ошибок: 1", vm.ProjectIoStatus);
        Assert.Contains("missing.wav", message);
    }

    [Fact]
    public async Task ImportAudioFilesAsync_SuccessStatus_ClearsAfterTtl()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var track = new Track(TrackId.New(), "/audio/test.flac", "test", TimeSpan.FromMinutes(3), new TrackDefaults());
        var project = new Project(ProjectId.New(), "Main", []);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([track], [project], project.Id));
        using var vm = new ProjectsViewModel(bus, () => [track],
            audioImport: (_, _) => Task.FromResult(new Aria.App.ImportReport(1, 0, [])),
            transientStatusTtl: TimeSpan.FromMilliseconds(20));

        var ids = await vm.ImportAudioFilesAsync([track.FilePath]);

        Assert.Single(ids);
        Assert.Equal("импортировано: 1, пропущено: 0, ошибок: 0", vm.ProjectIoStatus);
        await Task.Delay(500);
        Assert.Equal(string.Empty, vm.ProjectIoStatus);
    }

    [Fact]
    public async Task ImportAudioFilesAsync_Silent_SkipsProgressAndStatus()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var track = new Track(TrackId.New(), "/audio/test.flac", "test", TimeSpan.FromMinutes(3), new TrackDefaults());
        var found = new Track(TrackId.New(), "/audio/found.wav", "found", TimeSpan.FromMinutes(2), new TrackDefaults());
        var project = new Project(ProjectId.New(), "Main", []);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([track, found], [project], project.Id));
        IProgress<string>? captured = new Progress<string>(_ => { });
        using var vm = new ProjectsViewModel(bus, () => [track, found],
            audioImport: (_, progress) =>
            {
                captured = progress;
                return Task.FromResult(new Aria.App.ImportReport(1, 0, []));
            });

        var ids = await vm.ImportAudioFilesAsync([found.FilePath], silent: true);

        Assert.Equal(found.Id, Assert.Single(ids));
        Assert.Null(captured);
        Assert.Equal(string.Empty, vm.ProjectIoStatus);
    }

    private static async Task PollAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }
            await Task.Delay(25);
        }
        throw new TimeoutException("condition not met");
    }
}
