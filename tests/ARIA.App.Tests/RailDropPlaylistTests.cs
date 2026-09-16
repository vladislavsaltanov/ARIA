using Aria.App.Tests;
using Aria.App.ViewModels;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;
using System.Collections.Immutable;

public sealed class RailDropProjectTests
{
    [Fact]
    public async Task DropFolder_CreatesProjectNamedAfterFolder_WithItsTracks()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var bandDir = Path.Combine(root, "band");
        Directory.CreateDirectory(bandDir);
        try
        {
            await DropFolder_CreatesProjectNamedAfterFolder_WithItsTracksCore(bandDir);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private async Task DropFolder_CreatesProjectNamedAfterFolder_WithItsTracksCore(string bandDir)
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var first = new Track(TrackId.New(), Path.Combine(bandDir, "track1.flac"), "track1", TimeSpan.FromMinutes(3), new TrackDefaults());
        var second = new Track(TrackId.New(), Path.Combine(bandDir, "track2.flac"), "track2", TimeSpan.FromMinutes(4), new TrackDefaults());
        var main = new Project(ProjectId.New(), "Main", []);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([first, second], [main], main.Id));
        using var vm = new ProjectsViewModel(bus, () => [first, second],
            audioImport: (_, _) => Task.FromResult(new Aria.App.ImportReport(2, 0, [])));

        await vm.ImportDroppedPathsAsync([bandDir]);

        Assert.Equal("band", vm.SelectedProject?.Name);
        Assert.Equal(2, vm.SelectedProject?.Entries.Count);
        Assert.Empty(vm.Projects.Single(p => p.Name == "Main").Entries);
    }

    [Fact]
    public async Task DropFolder_SavesAriaFolderAndActivates()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var bandDir = Path.Combine(root, "band");
        Directory.CreateDirectory(bandDir);
        try
        {
            using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
            var first = new Track(TrackId.New(), Path.Combine(bandDir, "track1.flac"), "track1", TimeSpan.FromMinutes(3), new TrackDefaults());
            var main = new Project(ProjectId.New(), "Main", []);
            bus.Submit(new ClientId("setup"), 1, new LoadShow([first], [main], main.Id));
            using var vm = new ProjectsViewModel(bus, () => [first],
                audioImport: (_, _) => Task.FromResult(new Aria.App.ImportReport(1, 0, [])));

            await vm.ImportDroppedPathsAsync([bandDir]);

            var created = vm.Projects.Single(pr => pr.Name == "band");
            Assert.Equal(created.Id, bus.Snapshot().Show.ActiveId);
            Assert.True(File.Exists(Aria.App.Services.ProjectFolder.ProjectPath(bandDir)));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task DropFolder_WithExistingAria_OpensInsteadOfDuplicating()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var bandDir = Path.Combine(root, "band");
        Directory.CreateDirectory(Path.Combine(bandDir, Aria.App.Services.ProjectFolder.FolderName));
        var track = new Track(TrackId.New(), "/audio/one.flac", "one", TimeSpan.FromMinutes(3), new TrackDefaults());
        File.WriteAllText(
            Aria.App.Services.ProjectFolder.ProjectPath(bandDir),
            ProjectFormat.Export("Saved", [new ProjectExportEntry("/audio/one.flac")]));
        try
        {
            using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
            var main = new Project(ProjectId.New(), "Main", []);
            bus.Submit(new ClientId("setup"), 1, new LoadShow([track], [main], main.Id));
            using var vm = new ProjectsViewModel(bus, () => [track],
                audioImport: (_, _) => Task.FromResult(new Aria.App.ImportReport(0, 0, [])));

            await vm.ImportDroppedPathsAsync([bandDir]);
            await vm.ImportDroppedPathsAsync([bandDir]);

            var opened = vm.Projects.Where(pr => pr.Name == "Saved").ToArray();
            Assert.Single(opened);
            Assert.Equal(opened[0].Id, bus.Snapshot().Show.ActiveId);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task DropFiles_GoesToSelectedProject()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var track = new Track(TrackId.New(), "/audio/single.flac", "single", TimeSpan.FromMinutes(3), new TrackDefaults());
        var main = new Project(ProjectId.New(), "Main", []);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([track], [main], main.Id));
        using var vm = new ProjectsViewModel(bus, () => [track],
            audioImport: (_, _) => Task.FromResult(new Aria.App.ImportReport(1, 0, [])));

        await vm.ImportDroppedPathsAsync(["/audio/single.flac"]);

        Assert.Equal("Main", vm.SelectedProject?.Name);
        Assert.Single(vm.Projects);
        Assert.Single(vm.SelectedProject?.Entries!);
    }

    [Fact]
    public async Task DropFolder_NameClash_Uniquifies()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var bandDir = Path.Combine(root, "band");
        Directory.CreateDirectory(bandDir);
        try
        {
            await DropFolder_NameClash_UniquifiesCore(bandDir);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private async Task DropFolder_NameClash_UniquifiesCore(string bandDir)
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var track = new Track(TrackId.New(), Path.Combine(bandDir, "track1.flac"), "track1", TimeSpan.FromMinutes(3), new TrackDefaults());
        var main = new Project(ProjectId.New(), "Main", []);
        var band = new Project(ProjectId.New(), "band", []);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([track], [main, band], main.Id));
        using var vm = new ProjectsViewModel(bus, () => [track],
            audioImport: (_, _) => Task.FromResult(new Aria.App.ImportReport(1, 0, [])));

        await vm.ImportDroppedPathsAsync([bandDir]);

        Assert.Equal("band 2", vm.SelectedProject?.Name);
        Assert.Equal(3, vm.Projects.Count);
    }
}
