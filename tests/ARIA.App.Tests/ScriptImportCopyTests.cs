namespace Aria.App.Tests;

using Aria.App.ViewModels;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;

public sealed class ScriptImportCopyTests : IDisposable
{
    private static readonly Track TestTrack = new(
        TrackId.New(), "/audio/test.flac", "test", TimeSpan.FromMinutes(3), new TrackDefaults());

    private readonly CommandBus _bus;
    private readonly string _root;

    public ScriptImportCopyTests()
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
    public void ImportDocument_CopiesFile_BesideProjectFile()
    {
        var projectDir = Directory.CreateDirectory(Path.Combine(_root, "proj")).FullName;
        var srcDir = Directory.CreateDirectory(Path.Combine(_root, "src")).FullName;
        var project = new Project(ProjectId.New(), "Main", [new ProjectEntry(EntryId.New(), TestTrack.Id)]);
        _bus.Submit(new ClientId("setup"), 1, new LoadShow([TestTrack], [project], project.Id));
        using var viewModel = new ScriptPanelViewModel(_bus, () => [TestTrack], projectDirSource: _ => projectDir);
        var json = """{"format":"aria-script","version":1,"name":"Вечер","lines":[{"at":"1:05","text":"открывашка"}]}""";
        var sourcePath = Path.Combine(srcDir, "evening.aria-script.json");
        File.WriteAllText(sourcePath, json);

        var report = viewModel.ImportDocument(json, sourcePath);

        Assert.Null(report.Error);
        Assert.True(File.Exists(Path.Combine(projectDir, "evening.aria-script.json")));
        var script = Assert.Single(_bus.Snapshot().Show.Scripts);
        Assert.Equal<ProjectId?>(project.Id, script.Project);
    }

    [Fact]
    public void ImportDocument_WithoutProjectDir_ImportsMemoryOnly()
    {
        var project = new Project(ProjectId.New(), "Main", [new ProjectEntry(EntryId.New(), TestTrack.Id)]);
        _bus.Submit(new ClientId("setup"), 1, new LoadShow([TestTrack], [project], project.Id));
        using var viewModel = new ScriptPanelViewModel(_bus, () => [TestTrack]);
        var json = """{"format":"aria-script","version":1,"name":"Вечер","lines":[]}""";

        var report = viewModel.ImportDocument(json);

        Assert.Null(report.Error);
        Assert.Single(_bus.Snapshot().Show.Scripts);
    }

    [Fact]
    public async Task ImportProject_RemembersSourceDirectory()
    {
        var projectDir = Directory.CreateDirectory(Path.Combine(_root, "proj")).FullName;
        using var projects = new ProjectsViewModel(_bus, () => [TestTrack]);
        var json = ProjectFormat.Export("Вечер", [new ProjectExportEntry("/audio/test.flac")]);

        await projects.ImportDocumentAsync(json, projectDir);

        var imported = _bus.Snapshot().Show.Projects.Single(p => p.Name == "Вечер");
        Assert.Equal(projectDir, projects.GetProjectDirectory(imported.Id));
    }

    [Fact]
    public async Task FinishExportAsync_RemembersDirectory()
    {
        var projectDir = Directory.CreateDirectory(Path.Combine(_root, "proj")).FullName;
        var project = new Project(ProjectId.New(), "Main", [new ProjectEntry(EntryId.New(), TestTrack.Id)]);
        _bus.Submit(new ClientId("setup"), 1, new LoadShow([TestTrack], [project], project.Id));
        using var projects = new ProjectsViewModel(_bus, () => [TestTrack]);
        using var sink = new MemoryStream();

        await projects.FinishExportAsync(() => Task.FromResult<Stream>(sink), "Main.aria-project.json", projectDir);

        Assert.Equal(projectDir, projects.GetProjectDirectory(project.Id));
    }
}
