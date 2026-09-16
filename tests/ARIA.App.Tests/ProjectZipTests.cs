namespace Aria.App.Tests;

using Aria.App.Services;
using Aria.App.ViewModels;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;
using System.IO.Compression;

public sealed class ProjectZipTests : IDisposable
{
    private readonly CommandBus _bus;
    private readonly string _root;

    public ProjectZipTests()
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
    public void ExportZipToFile_WritesProjectJsonAndAudio()
    {
        var wav = TestWav.Write(Path.Combine(_root, "audio-src"), "one.wav");
        var track = new Track(TrackId.New(), wav, "one", TimeSpan.FromMinutes(1), new TrackDefaults());
        var project = new Project(ProjectId.New(), "Band", [new ProjectEntry(EntryId.New(), track.Id)]);
        _bus.Submit(new ClientId("setup"), 1, new LoadShow([track], [project], project.Id));
        _bus.Submit(new ClientId("setup"), 2, new CreateScript("Утро", project.Id));
        using var scripts = new ScriptPanelViewModel(_bus, () => [track]);
        using var projects = new ProjectsViewModel(_bus, () => [track], scriptExporter: scripts.ExportProjectScripts);
        projects.SelectedProject = projects.Projects.Single(p => p.Name == "Band");
        var zip = Path.Combine(_root, "band.aria.zip");

        projects.ExportZipToFile(zip, projects.SelectedProject!);

        using var archive = ZipFile.OpenRead(zip);
        var names = archive.Entries.Select(e => e.FullName).ToArray();
        Assert.Contains("project.json", names);
        Assert.Contains("audio/one.wav", names);
        var jsonEntry = archive.Entries.Single(e => e.FullName == "project.json");
        using var reader = new StreamReader(jsonEntry.Open());
        var document = ProjectFormat.Import(reader.ReadToEnd());
        Assert.Equal("Band", document.Name);
        Assert.Equal("audio/one.wav", Assert.Single(document.Entries).File);
        Assert.Equal("Утро", Assert.Single(document.Scripts).Name);
    }

    [Fact]
    public async Task ImportZipFile_ExtractsAndBindsTracks()
    {
        var wav = TestWav.Write(Path.Combine(_root, "audio-src"), "one.wav");
        var track = new Track(TrackId.New(), wav, "one", TimeSpan.FromMinutes(1), new TrackDefaults());
        var project = new Project(ProjectId.New(), "Band", [new ProjectEntry(EntryId.New(), track.Id)]);
        _bus.Submit(new ClientId("setup"), 1, new LoadShow([track], [project], project.Id));
        _bus.Submit(new ClientId("setup"), 2, new CreateScript("Утро", project.Id));
        using var scripts = new ScriptPanelViewModel(_bus, () => [track]);
        using var projects = new ProjectsViewModel(_bus, () => [track], scriptExporter: scripts.ExportProjectScripts);
        projects.SelectedProject = projects.Projects.Single(p => p.Name == "Band");
        var zip = Path.Combine(_root, "band.aria.zip");
        projects.ExportZipToFile(zip, projects.SelectedProject!);

        var dest = Path.Combine(_root, "dest");
        var relocated = new Track(TrackId.New(), Path.Combine(dest, "audio", "one.wav"), "one", TimeSpan.FromMinutes(1), new TrackDefaults());
        using var freshBus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        freshBus.Submit(new ClientId("setup"), 1, new LoadShow([relocated], [], null));
        using var freshScripts = new ScriptPanelViewModel(freshBus, () => [relocated]);
        using var freshProjects = new ProjectsViewModel(
            freshBus,
            () => [relocated],
            audioImport: (_, _) => Task.FromResult(new Aria.App.ImportReport(0, 0, [])),
            scriptExporter: freshScripts.ExportProjectScripts);

        var report = await freshProjects.ImportZipFile(zip, dest);

        Assert.NotNull(report);
        Assert.Null(report.Error);
        var imported = freshBus.Snapshot().Show.Projects.Single(p => p.Name == "Band");
        Assert.Equal(relocated.Id, Assert.Single(imported.Entries).TrackId);
        var script = Assert.Single(freshBus.Snapshot().Show.Scripts);
        Assert.Equal("Утро", script.Name);
        Assert.Equal<ProjectId?>(imported.Id, script.Project);
        Assert.Equal(dest, freshProjects.GetProjectDirectory(imported.Id));
        Assert.True(File.Exists(Path.Combine(dest, "audio", "one.wav")));
    }
}
