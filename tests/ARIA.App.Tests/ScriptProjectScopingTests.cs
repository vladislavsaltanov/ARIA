namespace Aria.App.Tests;

using Aria.App.ViewModels;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;

public sealed class ScriptProjectScopingTests : IDisposable
{
    private static readonly Track FirstTrack = new(
        TrackId.New(), "/audio/one.flac", "one", TimeSpan.FromMinutes(3), new TrackDefaults());

    private static readonly Track SecondTrack = new(
        TrackId.New(), "/audio/two.flac", "two", TimeSpan.FromMinutes(4), new TrackDefaults());

    private readonly CommandBus _bus;
    private readonly ProjectsViewModel _projects;
    private readonly ScriptPanelViewModel _scripts;
    private readonly Project _first;
    private readonly Project _second;

    public ScriptProjectScopingTests()
    {
        _bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        _first = new Project(ProjectId.New(), "A", [new ProjectEntry(EntryId.New(), FirstTrack.Id)]);
        _second = new Project(ProjectId.New(), "B", [new ProjectEntry(EntryId.New(), SecondTrack.Id)]);
        _bus.Submit(new ClientId("setup"), 1, new LoadShow([FirstTrack, SecondTrack], [_first, _second], _first.Id));
        _projects = new ProjectsViewModel(_bus, () => [FirstTrack, SecondTrack]);
        _scripts = new ScriptPanelViewModel(_bus, () => [FirstTrack, SecondTrack]);
    }

    public void Dispose()
    {
        _scripts.Dispose();
        _projects.Dispose();
        _bus.Dispose();
    }

    [Fact]
    public void SwitchActiveProject_ShowsOnlyItsScripts()
    {
        _bus.Submit(new ClientId("setup"), 2, new CreateScript("Утро", _first.Id));
        Assert.Single(_scripts.Scripts);

        _bus.Submit(new ClientId("setup"), 3, new SetActiveProject(_second.Id));

        Assert.Empty(_scripts.Scripts);
        Assert.Null(_scripts.SelectedScript);
        Assert.Empty(_scripts.Lines);

        _bus.Submit(new ClientId("setup"), 4, new SetActiveProject(_first.Id));

        var script = Assert.Single(_scripts.Scripts);
        Assert.Equal("Утро", script.Name);
    }

    [Fact]
    public void SelectingProject_ActivatesIt_SoPanelFollows()
    {
        _bus.Submit(new ClientId("setup"), 2, new CreateScript("Утро", _first.Id));
        Assert.Single(_scripts.Scripts);

        _projects.SelectedProject = _projects.Projects.Single(p => p.Name == "B");

        Assert.Equal(_second.Id, _bus.Snapshot().Show.ActiveId);
        Assert.Empty(_scripts.Scripts);
        Assert.Null(_scripts.SelectedScript);
    }

    [Fact]
    public void Export_EmbedsOnlySelectedProjectScripts()
    {
        _bus.Submit(new ClientId("setup"), 2, new CreateScript("Утро", _first.Id));
        _bus.Submit(new ClientId("setup"), 3, new CreateScript("Вечер", _second.Id));
        _projects.SelectedProject = _projects.Projects.Single(p => p.Name == "B");

        var document = ProjectFormat.Import(_projects.ExportSelectedDocument());

        var embedded = Assert.Single(document.Scripts);
        Assert.Equal("Вечер", embedded.Name);
    }

    [Fact]
    public async Task ImportV2_AssignsScriptsToImportedProject_NotActive()
    {
        _bus.Submit(new ClientId("setup"), 2, new CreateScript("Старый", _first.Id));
        var json = ProjectFormat.Export("Вечер",
            [new ProjectExportEntry("/audio/two.flac")],
            [new ProjectExportScript("Новый", [new ProjectExportScriptLine("1:05", "открывашка", ["/audio/two.flac"])])]);

        var report = await _projects.ImportDocumentAsync(json);

        Assert.Null(report.Error);
        var imported = _bus.Snapshot().Show.Projects.Single(p => p.Name == "Вечер");
        var script = Assert.Single(_bus.Snapshot().Show.Scripts.Where(s => s.Name == "Новый"));
        Assert.Equal<ProjectId?>(imported.Id, script.Project);
        var old = Assert.Single(_bus.Snapshot().Show.Scripts.Where(s => s.Name == "Старый"));
        Assert.Equal<ProjectId?>(_first.Id, old.Project);
    }

    [Fact]
    public async Task ImportV2_NoTracks_CreatesShellProjectWithScripts()
    {
        var json = ProjectFormat.Export("Вечер",
            [new ProjectExportEntry("/audio/missing.flac")],
            [new ProjectExportScript("Новый", [new ProjectExportScriptLine("1:05", "открывашка", [])])]);

        var report = await _projects.ImportDocumentAsync(json);

        Assert.NotNull(report.Error);
        var shell = _bus.Snapshot().Show.Projects.Single(pr => pr.Name == "Вечер");
        var script = Assert.Single(_bus.Snapshot().Show.Scripts.Where(s => s.Name == "Новый"));
        Assert.Equal<ProjectId?>(shell.Id, script.Project);
    }
}
