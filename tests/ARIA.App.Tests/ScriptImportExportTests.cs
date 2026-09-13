namespace Aria.App.Tests;

using Aria.App.ViewModels;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;

public sealed class ScriptImportExportTests : IDisposable
{
    private readonly CommandBus _bus;
    private readonly ScriptPanelViewModel _viewModel;

    public ScriptImportExportTests()
    {
        _bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        _bus.Submit(new ClientId("setup"), 1, new LoadShow([], [], null));
        _viewModel = new ScriptPanelViewModel(_bus);
    }

    public void Dispose()
    {
        _viewModel.Dispose();
        _bus.Dispose();
    }

    [Fact]
    public void BadJson_KeepsScripts_AndHidesTechnicalError()
    {
        var failed = new List<string>();
        _viewModel.ScriptImportFailed += failed.Add;

        var report = _viewModel.ImportDocument("{не json");

        Assert.NotNull(report.Error);
        Assert.Empty(_viewModel.Scripts);
        Assert.Null(_viewModel.SelectedScript);
        Assert.Equal("импорт не удался", _viewModel.ScriptIoStatus);
        Assert.DoesNotContain("bad-json", _viewModel.ScriptIoStatus, StringComparison.Ordinal);
        Assert.Contains("bad-json", _viewModel.LastScriptError, StringComparison.Ordinal);
        Assert.Single(failed);
    }

    [Fact]
    public void WrongFormat_KeepsScripts()
    {
        var report = _viewModel.ImportDocument(
            """{"format":"aria-playlist","version":1,"name":"X","entries":[]}""");

        Assert.NotNull(report.Error);
        Assert.Empty(_viewModel.Scripts);
        Assert.Equal("импорт не удался", _viewModel.ScriptIoStatus);
    }

    [Fact]
    public void ImportExport_Roundtrip_PreservesNameTimeText()
    {
        var report = _viewModel.ImportDocument(
            """{"format":"aria-script","version":1,"name":"Вечер","lines":[{"at":"1:05","text":"открывашка"},{"at":"2:00","text":""}]}""");

        Assert.Null(report.Error);
        Assert.Equal("Вечер", report.ScriptName);
        Assert.Equal(2, report.Added);
        var script = Assert.Single(_viewModel.Scripts);
        Assert.Same(script, _viewModel.SelectedScript);
        Assert.Equal(2, _viewModel.Lines.Count);
        Assert.Equal(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(5), _viewModel.Lines[0].AtElapsed);
        Assert.Equal("открывашка", _viewModel.Lines[0].Text);

        var exported = _viewModel.ExportSelectedDocument();
        Assert.Contains("Вечер", exported, StringComparison.Ordinal);
        Assert.Contains("открывашка", exported, StringComparison.Ordinal);

        var second = _viewModel.ImportDocument(exported);

        Assert.Null(second.Error);
        Assert.Equal(2, _viewModel.Scripts.Count);
        Assert.Equal("Вечер 2", second.ScriptName);
        Assert.Equal(2, _viewModel.Lines.Count);
        Assert.Equal("открывашка", _viewModel.Lines[0].Text);
    }

    [Fact]
    public async Task BadPlaylistJson_KeepsPlaylists_AndHidesTechnicalError()
    {
        var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([], [], null));
        using var vm = new PlaylistsViewModel(bus);
        var failed = new List<string>();
        vm.ImportFailed += failed.Add;

        var report = await vm.ImportDocumentAsync("{не json");

        Assert.NotNull(report.Error);
        Assert.Empty(vm.Playlists);
        Assert.Equal("импорт не удался", vm.PlaylistIoStatus);
        Assert.DoesNotContain("bad-json", vm.PlaylistIoStatus, StringComparison.Ordinal);
        Assert.Contains("bad-json", vm.LastImportError, StringComparison.Ordinal);
        Assert.Single(failed);
        bus.Dispose();
    }
}
