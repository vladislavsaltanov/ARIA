namespace Aria.App.Tests;

using Aria.App.ViewModels;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;
using Aria.Core.State;

public sealed class MentionPlayTests : IDisposable
{
    private static readonly Track LiveTrack = new(
        TrackId.New(), "/audio/live.flac", "трек (лайв)", TimeSpan.FromMinutes(3), new TrackDefaults());

    private static readonly Track PlainTrack = new(
        TrackId.New(), "/audio/plain.flac", "обычный трек", TimeSpan.FromMinutes(4), new TrackDefaults());

    private static readonly Track OrphanTrack = new(
        TrackId.New(), "/audio/orphan.flac", "сирота", TimeSpan.FromMinutes(2), new TrackDefaults());

    private readonly CommandBus _bus;
    private readonly ScriptPanelViewModel _viewModel;

    public MentionPlayTests()
    {
        _bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var project = new Project(ProjectId.New(), "Main", [new ProjectEntry(EntryId.New(), LiveTrack.Id, null), new ProjectEntry(EntryId.New(), PlainTrack.Id, null)]);
        _bus.Submit(new ClientId("setup"), 1, new LoadShow([LiveTrack, PlainTrack, OrphanTrack], [project], project.Id));
        _viewModel = new ScriptPanelViewModel(_bus, () => [LiveTrack, PlainTrack, OrphanTrack]);
    }

    public void Dispose()
    {
        _viewModel.Dispose();
        _bus.Dispose();
    }

    [Fact]
    public void MentionDisplayName_KeepsClosingParen()
    {
        _viewModel.CreateScriptCommand.Execute(null);
        _viewModel.AddLineCommand.Execute(null);
        var line = Assert.Single(_viewModel.Lines);

        _viewModel.InsertMention(line, LiveTrack.Id);

        Assert.Equal("трек (лайв)", Assert.Single(line.StagedMentions).DisplayName);
    }

    [Fact]
    public void ExecuteMention_PlaysTrackImmediately()
    {
        var mention = new ScriptPanelViewModel.MentionVm(LiveTrack.Id, "трек (лайв)", false, "03:00");

        _viewModel.ExecuteMention(mention);

        Assert.Equal(TransportStatus.Playing, _bus.Snapshot().Transport.Status);
        Assert.Equal(LiveTrack.Id, _bus.Snapshot().Transport.Current!.TrackId);
        Assert.Empty(_bus.Snapshot().Queue.Items);
    }

    [Fact]
    public void ExecuteLine_SingleMention_PlaysTrackImmediately()
    {
        _viewModel.CreateScriptCommand.Execute(null);
        _viewModel.AddLineCommand.Execute(null);
        var line = Assert.Single(_viewModel.Lines);
        _viewModel.InsertMention(line, PlainTrack.Id);
        line.EditText = "интро";
        _viewModel.CommitEdit(line);
        var stored = Assert.Single(_viewModel.Lines);

        _viewModel.ExecuteLine(stored);

        Assert.Equal(PlainTrack.Id, _bus.Snapshot().Transport.Current!.TrackId);
    }

    [Fact]
    public void ChooseCandidate_PlaysTrackImmediately()
    {
        _viewModel.CreateScriptCommand.Execute(null);
        _viewModel.AddLineCommand.Execute(null);
        var line = Assert.Single(_viewModel.Lines);
        var mention = new ScriptPanelViewModel.MentionVm(PlainTrack.Id, "обычный трек", false, "04:00");

        _viewModel.ChooseCandidate(line, mention);

        Assert.Equal(PlainTrack.Id, _bus.Snapshot().Transport.Current!.TrackId);
        Assert.False(line.CandidatesVisible);
    }

    [Fact]
    public void ExecuteMention_TrackOutsidePlaylist_DoesNothing()
    {
        var rejections = new List<Rejected>();
        using var subscription = _bus.Subscribe(e =>
        {
            if (e is Rejected rejected)
            {
                rejections.Add(rejected);
            }
        });
        var mention = new ScriptPanelViewModel.MentionVm(OrphanTrack.Id, "сирота", false, "02:00");

        _viewModel.ExecuteMention(mention);

        Assert.NotEqual(TransportStatus.Playing, _bus.Snapshot().Transport.Status);
        Assert.Empty(rejections);
    }

    [Fact]
    public void ExecuteMention_Dangling_DoesNothing()
    {
        var mention = new ScriptPanelViewModel.MentionVm(TrackId.New(), "—", true, "--:--");

        _viewModel.ExecuteMention(mention);

        Assert.NotEqual(TransportStatus.Playing, _bus.Snapshot().Transport.Status);
    }
}
