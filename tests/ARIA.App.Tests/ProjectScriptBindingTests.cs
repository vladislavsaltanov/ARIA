namespace Aria.App.Tests;

using Aria.App.ViewModels;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;
using System.Collections.Immutable;

public sealed class ProjectScriptBindingTests : IDisposable
{
    private static readonly Track TestTrack = new(
        TrackId.New(), "/audio/test.flac", "test", TimeSpan.FromMinutes(3), new TrackDefaults());

    private readonly CommandBus _bus;
    private readonly ProjectsViewModel _projects;
    private readonly ScriptPanelViewModel _scripts;

    public ProjectScriptBindingTests()
    {
        var project = new Project(ProjectId.New(), "Main", [new ProjectEntry(EntryId.New(), TestTrack.Id)]);
        _bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        _bus.Submit(new ClientId("setup"), 1, new LoadShow([TestTrack], [project], project.Id));
        _projects = new ProjectsViewModel(_bus, () => [TestTrack]);
        _scripts = new ScriptPanelViewModel(_bus, () => [TestTrack]);
    }

    public void Dispose()
    {
        _scripts.Dispose();
        _projects.Dispose();
        _bus.Dispose();
    }

    [Fact]
    public void Export_EmbedsScripts()
    {
        _bus.Submit(new ClientId("setup"), 2, new CreateScript("Утро"));
        var script = _bus.Snapshot().Show.Scripts.Single(s => s.Name == "Утро");
        _bus.Submit(new ClientId("setup"), 3, new AddScriptLine(script.Id, TimeSpan.FromSeconds(65), "открывашка", [TestTrack.Id]));

        var json = _projects.ExportSelectedDocument();
        var document = ProjectFormat.Import(json);

        Assert.Equal("aria-project", document.Format);
        Assert.Equal(2, document.Version);
        var embedded = Assert.Single(document.Scripts);
        Assert.Equal("Утро", embedded.Name);
        var line = Assert.Single(embedded.Lines!);
        Assert.Equal("1:05", line.At);
        Assert.Equal("открывашка", line.Text);
        Assert.Equal("/audio/test.flac", Assert.Single(line.Tracks!));
    }

    [Fact]
    public async Task ImportV2_CreatesProjectAndScripts_WithResolvedMentions()
    {
        var json = ProjectFormat.Export("Вечер",
            [new ProjectExportEntry("/audio/test.flac")],
            [new ProjectExportScript("Утро", [new ProjectExportScriptLine("1:05", "открывашка", ["/audio/test.flac"])])]);

        var report = await _projects.ImportDocumentAsync(json);

        Assert.Null(report.Error);
        Assert.NotNull(_bus.Snapshot().Show.Projects.FirstOrDefault(p => p.Name == "Вечер"));
        var script = Assert.Single(_bus.Snapshot().Show.Scripts.Where(s => s.Name == "Утро"));
        var line = Assert.Single(script.Lines);
        Assert.Equal(TimeSpan.FromSeconds(65), line.AtElapsed);
        Assert.Equal("открывашка", line.Text);
        Assert.Equal(TestTrack.Id, Assert.Single(line.Mentions).Track);
    }

    [Fact]
    public async Task ImportV2_Twice_MergesWithoutDuplicates()
    {
        var json = ProjectFormat.Export("Вечер",
            [new ProjectExportEntry("/audio/test.flac")],
            [new ProjectExportScript("Утро", [new ProjectExportScriptLine("1:05", "открывашка", ["/audio/test.flac"])])]);

        var first = await _projects.ImportDocumentAsync(json);
        var second = await _projects.ImportDocumentAsync(json);

        Assert.Null(first.Error);
        Assert.Null(second.Error);
        var scripts = _bus.Snapshot().Show.Scripts.Where(s => s.Name == "Утро").ToArray();
        Assert.Single(scripts);
        Assert.Single(scripts[0].Lines);
    }

    [Fact]
    public async Task ImportV1_WithoutScripts_BehavesAsBefore()
    {
        var json = """
            {"format":"aria-playlist","version":1,"name":"Ретро","entries":[{"file":"/audio/test.flac"}]}
            """;

        var report = await _projects.ImportDocumentAsync(json);

        Assert.Null(report.Error);
        Assert.Equal("Ретро", report.ProjectName);
        Assert.Equal(1, report.Added);
        Assert.NotNull(_bus.Snapshot().Show.Projects.FirstOrDefault(p => p.Name == "Ретро"));
        Assert.Empty(_bus.Snapshot().Show.Scripts);
    }
}
