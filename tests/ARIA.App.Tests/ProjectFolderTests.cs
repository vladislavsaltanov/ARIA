namespace Aria.App.Tests;

using Aria.App.Services;
using Aria.App.ViewModels;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;

public sealed class ProjectFolderTests : IDisposable
{
    private static readonly Track FirstTrack = new(
        TrackId.New(), "/audio/one.flac", "one", TimeSpan.FromMinutes(3), new TrackDefaults());

    private readonly CommandBus _bus;
    private readonly string _root;

    public ProjectFolderTests()
    {
        _bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        _bus.Dispose();
        Directory.Delete(_root, true);
    }

    [Fact]
    public void SaveProjectToFolder_WritesProjectJsonAndScriptFiles()
    {
        var project = new Project(ProjectId.New(), "Main", [new ProjectEntry(EntryId.New(), FirstTrack.Id)]);
        _bus.Submit(new ClientId("setup"), 1, new LoadShow([FirstTrack], [project], project.Id));
        _bus.Submit(new ClientId("setup"), 2, new CreateScript("Утро", project.Id));
        using var scripts = new ScriptPanelViewModel(_bus, () => [FirstTrack]);
        using var projects = new ProjectsViewModel(_bus, () => [FirstTrack], scriptExporter: scripts.ExportProjectScripts);
        var dir = Path.Combine(_root, "band");
        Directory.CreateDirectory(dir);

        projects.SaveProjectToFolder(dir, projects.SelectedProject!);

        var document = ProjectFormat.Import(File.ReadAllText(ProjectFolder.ProjectPath(dir)));
        Assert.Equal("Main", document.Name);
        Assert.Single(document.Entries);
        Assert.Equal("Утро", Assert.Single(document.Scripts).Name);
        var scriptFiles = Directory.GetFiles(ProjectFolder.ScriptsPath(dir), "*.aria-script.json");
        Assert.Single(scriptFiles);
    }

    [Fact]
    public async Task OpenProjectFolderAsync_ImportsProjectAndScripts()
    {
        var seed = new Project(ProjectId.New(), "Seed", [new ProjectEntry(EntryId.New(), FirstTrack.Id)]);
        _bus.Submit(new ClientId("setup"), 1, new LoadShow([FirstTrack], [seed], seed.Id));
        _bus.Submit(new ClientId("setup"), 2, new CreateScript("Утро", seed.Id));
        using var scripts = new ScriptPanelViewModel(_bus, () => [FirstTrack]);
        using var projects = new ProjectsViewModel(_bus, () => [FirstTrack], scriptExporter: scripts.ExportProjectScripts);
        var dir = Path.Combine(_root, "band");
        Directory.CreateDirectory(dir);
        projects.SelectedProject = projects.Projects.Single(pr => pr.Name == "Seed");
        projects.SaveProjectToFolder(dir, projects.SelectedProject!);

        using var freshBus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        freshBus.Submit(new ClientId("setup"), 1, new LoadShow([FirstTrack], [], null));
        using var freshScripts = new ScriptPanelViewModel(freshBus, () => [FirstTrack]);
        using var freshProjects = new ProjectsViewModel(freshBus, () => [FirstTrack], scriptExporter: freshScripts.ExportProjectScripts);

        var report = await freshProjects.OpenProjectFolderAsync(dir);

        Assert.NotNull(report);
        Assert.Null(report.Error);
        var imported = freshBus.Snapshot().Show.Projects.Single(pr => pr.Name == "Seed");
        Assert.Equal(dir, freshProjects.GetProjectDirectory(imported.Id));
        var script = Assert.Single(freshBus.Snapshot().Show.Scripts);
        Assert.Equal("Утро", script.Name);
        Assert.Equal<ProjectId?>(imported.Id, script.Project);
    }

    [Fact]
    public async Task OpenProjectFolderAsync_MissingFile_ReturnsNull()
    {
        using var projects = new ProjectsViewModel(_bus, () => [FirstTrack]);

        var report = await projects.OpenProjectFolderAsync(Path.Combine(_root, "empty"));

        Assert.Null(report);
    }
}
