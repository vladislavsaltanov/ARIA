namespace Aria.App.Tests;

using System.Collections.Immutable;
using Aria.App.ViewModels;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;
using Aria.Core.State;

public sealed class ScriptPanelViewModelTests : IDisposable
{
    private static readonly Track FirstTrack = new(
        TrackId.New(), "/audio/one.flac", "Осенний дождь", TimeSpan.FromMinutes(3), new TrackDefaults());

    private static readonly Track SecondTrack = new(
        TrackId.New(), "/audio/two.flac", "Night Drive", TimeSpan.FromMinutes(4), new TrackDefaults());

    private readonly CommandBus _bus;
    private readonly ScriptPanelViewModel _viewModel;

    public ScriptPanelViewModelTests()
    {
        _bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        _bus.Submit(new ClientId("setup"), 1, new LoadShow([FirstTrack, SecondTrack], [], null));
        _viewModel = new ScriptPanelViewModel(_bus, () => [FirstTrack, SecondTrack]);
    }

    public void Dispose()
    {
        _viewModel.Dispose();
        _bus.Dispose();
    }

    [Fact]
    public void Create_SelectsNewScript_WithDefaultName()
    {
        _viewModel.CreateScriptCommand.Execute(null);

        var script = Assert.Single(_viewModel.Scripts);
        Assert.Equal("Новый сценарий", script.Name);
        Assert.Same(script, _viewModel.SelectedScript);
        Assert.Empty(_viewModel.Lines);
    }

    [Fact]
    public void RenameSelected_UpdatesTabName()
    {
        _viewModel.CreateScriptCommand.Execute(null);
        _viewModel.RenameSelected("Вечер");

        Assert.Equal("Вечер", Assert.Single(_viewModel.Scripts).Name);
    }

    [Fact]
    public void RenameSelected_BlankName_Ignored()
    {
        _viewModel.CreateScriptCommand.Execute(null);
        _viewModel.RenameSelected("  ");

        Assert.Equal("Новый сценарий", Assert.Single(_viewModel.Scripts).Name);
    }

    [Fact]
    public void DeleteSelected_RemovesScript()
    {
        _viewModel.CreateScriptCommand.Execute(null);
        _viewModel.DeleteSelected();

        Assert.Empty(_viewModel.Scripts);
        Assert.Null(_viewModel.SelectedScript);
        Assert.Empty(_viewModel.Lines);
    }

    [Fact]
    public void AddLine_OpensEdit_AndCommitStoresLine()
    {
        _viewModel.CreateScriptCommand.Execute(null);
        _viewModel.AddLineCommand.Execute(null);

        var line = Assert.Single(_viewModel.Lines);
        Assert.True(line.IsEditing);

        line.EditTimeText = "1:45";
        line.EditText = "вступление";
        _viewModel.CommitEdit(line);

        Assert.False(line.IsEditing);
        Assert.Equal(TimeSpan.FromSeconds(105), line.AtElapsed);
        Assert.Equal("1:45", line.DisplayTime);
        Assert.Equal("вступление", line.Text);
        Assert.Single(_bus.Snapshot().Show.Scripts[0].Lines);
    }

    [Fact]
    public void CommitEdit_BadTime_KeepsEditOpen()
    {
        _viewModel.CreateScriptCommand.Execute(null);
        _viewModel.AddLineCommand.Execute(null);
        var line = Assert.Single(_viewModel.Lines);

        line.EditTimeText = "бред";
        _viewModel.CommitEdit(line);

        Assert.True(line.IsEditing);
        var stored = Assert.Single(_bus.Snapshot().Show.Scripts[0].Lines);
        Assert.Equal(TimeSpan.Zero, stored.AtElapsed);
        Assert.Equal(string.Empty, stored.Text);
    }

    [Fact]
    public void MarkNow_SetsEditTimeToClock()
    {
        _viewModel.CreateScriptCommand.Execute(null);
        _viewModel.AddLineCommand.Execute(null);
        var line = Assert.Single(_viewModel.Lines);

        _viewModel.MarkNow(line);

        Assert.Equal("0:00", line.EditTimeText);
    }

    [Fact]
    public void ExecuteLine_SingleMention_EnqueuesTrack()
    {
        _viewModel.CreateScriptCommand.Execute(null);
        AddCommittedLine("1:00", "соло", [FirstTrack.Id]);
        var line = Assert.Single(_viewModel.Lines);

        _viewModel.ExecuteLine(line);

        Assert.Equal(FirstTrack.Id, Assert.Single(_bus.Snapshot().Queue.Items).TrackId);
    }

    [Fact]
    public void ExecuteLine_SeveralMentions_ShowsCandidates_ThenEnqueuesChoice()
    {
        _viewModel.CreateScriptCommand.Execute(null);
        AddCommittedLine("1:00", "дуэт", [FirstTrack.Id, SecondTrack.Id]);
        var line = Assert.Single(_viewModel.Lines);

        _viewModel.ExecuteLine(line);

        Assert.True(line.CandidatesVisible);
        Assert.Equal(2, line.Candidates.Count);
        Assert.Empty(_bus.Snapshot().Queue.Items);

        _viewModel.ChooseCandidate(line, line.Candidates[1]);

        Assert.Equal(SecondTrack.Id, Assert.Single(_bus.Snapshot().Queue.Items).TrackId);
        Assert.False(line.CandidatesVisible);
    }

    [Fact]
    public void ExecuteLine_WithoutMentions_LeavesQueueAlone()
    {
        _viewModel.CreateScriptCommand.Execute(null);
        AddCommittedLine("1:00", "заметка", []);
        var line = Assert.Single(_viewModel.Lines);

        _viewModel.ExecuteLine(line);

        Assert.Empty(_bus.Snapshot().Queue.Items);
    }

    [Fact]
    public void Follow_HighlightsCurrentLine()
    {
        _viewModel.CreateScriptCommand.Execute(null);
        AddCommittedLine("0:00", "a", []);
        AddCommittedLine("1:40", "b", []);
        AddCommittedLine("3:20", "c", []);
        _bus.Submit(new ClientId("clock"), 50, new RestoreShow(
            [FirstTrack, SecondTrack], [], null, [], 0, TimeSpan.FromMilliseconds(100),
            TimeSpan.FromSeconds(150), true, _bus.Snapshot().Show.Scripts));

        Assert.False(_viewModel.Lines[0].IsCurrent);
        Assert.True(_viewModel.Lines[1].IsCurrent);
        Assert.False(_viewModel.Lines[2].IsCurrent);
    }

    [Fact]
    public void WallTimeTip_ProjectsElapsedOntoTimeOfDay()
    {
        _viewModel.CreateScriptCommand.Execute(null);
        AddCommittedLine("1:40", "b", []);
        Assert.Equal("часы не запущены", _viewModel.Lines[0].WallTimeTip);
        _bus.Submit(new ClientId("clock"), 60, new RestoreShow(
            [FirstTrack, SecondTrack], [], null, [], 0, TimeSpan.FromMilliseconds(100),
            TimeSpan.FromSeconds(150), true, _bus.Snapshot().Show.Scripts));
        _bus.Submit(new ClientId("clock"), 61, new StartShowClock());

        var tip = _viewModel.Lines[0].WallTimeTip;
        var projected = DateTime.ParseExact(tip, "HH:mm:ss", null).TimeOfDay;
        var expected = (DateTime.Now - TimeSpan.FromSeconds(150) + TimeSpan.FromSeconds(100)).TimeOfDay;
        Assert.InRange((projected - expected).Duration(), TimeSpan.Zero, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void ExecuteMention_Dangling_DoesNotEnqueue()
    {
        _viewModel.CreateScriptCommand.Execute(null);
        AddCommittedLine("0:10", "удалён", [TrackId.New()]);
        var mention = Assert.Single(Assert.Single(_viewModel.Lines).Mentions);
        Assert.True(mention.IsDangling);
        Rejected? rejection = null;
        using var subscription = _bus.Subscribe(e =>
        {
            if (e is Rejected rejected)
            {
                rejection = rejected;
            }
        });

        _viewModel.ExecuteMention(mention);

        Assert.Null(rejection);
        Assert.Empty(_bus.Snapshot().Queue.Items);
    }

    [Fact]
    public void AddLine_WithoutScripts_CreatesScriptAndOpensLine()
    {
        Assert.Empty(_viewModel.Scripts);

        _viewModel.AddLineCommand.Execute(null);

        Assert.Single(_viewModel.Scripts);
        var line = Assert.Single(_viewModel.Lines);
        Assert.True(line.IsEditing);
    }

    [Fact]
    public void Mention_Tooltip_HasNameAndDuration()
    {
        _viewModel.CreateScriptCommand.Execute(null);
        AddCommittedLine("0:10", "строка", [FirstTrack.Id]);
        var mention = Assert.Single(Assert.Single(_viewModel.Lines).Mentions);

        Assert.False(mention.IsDangling);
        Assert.Equal("Осенний дождь · 03:00", mention.Tooltip);
    }

    [Fact]
    public void SuggestTracks_ReturnsTopFiveSubstringMatches()
    {
        var tracks = Enumerable.Range(0, 7)
            .Select(i => new Track(TrackId.New(), $"/audio/n{i}.flac", $"Ночь {i}", TimeSpan.FromMinutes(2), new TrackDefaults()))
            .ToImmutableArray();
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        using var viewModel = new ScriptPanelViewModel(bus, () => tracks);

        var suggestions = viewModel.SuggestTracks("ночь");

        Assert.Equal(5, suggestions.Count);
        Assert.All(suggestions, t => Assert.Contains("Ночь", t.DefaultName));
        bus.Dispose();
    }

    [Fact]
    public void RemoveLine_DeletesRow()
    {
        _viewModel.CreateScriptCommand.Execute(null);
        AddCommittedLine("0:00", "a", []);
        AddCommittedLine("1:00", "b", []);

        _viewModel.RemoveLine(_viewModel.Lines[0]);

        Assert.Equal("b", Assert.Single(_viewModel.Lines).Text);
    }

    [Fact]
    public void MoveLine_ReordersRows()
    {
        _viewModel.CreateScriptCommand.Execute(null);
        AddCommittedLine("0:00", "a", []);
        AddCommittedLine("1:00", "b", []);

        _viewModel.MoveLine(0, 1);

        Assert.Equal("b", _viewModel.Lines[0].Text);
        Assert.Equal("a", _viewModel.Lines[1].Text);
    }

    [Fact]
    public void CommitOpenEdit_CommitsEditingLine()
    {
        _viewModel.CreateScriptCommand.Execute(null);
        _viewModel.AddLineCommand.Execute(null);
        var line = Assert.Single(_viewModel.Lines);
        line.EditText = "финал";

        _viewModel.CommitOpenEdit();

        Assert.False(line.IsEditing);
        Assert.Equal("финал", line.Text);
    }

    [Fact]
    public void CommitEditAndNewLine_CommitsAndOpensEmptyLine()
    {
        _viewModel.CreateScriptCommand.Execute(null);
        AddCommittedLine("1:00", "первая", []);
        var first = _viewModel.Lines[0];
        first.StartEdit();
        first.EditText = "первая правка";

        _viewModel.CommitEditAndNewLine(first);

        Assert.Equal(2, _viewModel.Lines.Count);
        Assert.False(_viewModel.Lines[0].IsEditing);
        Assert.Equal("первая правка", _viewModel.Lines[0].Text);
        Assert.True(_viewModel.Lines[1].IsEditing);
    }

    [Fact]
    public void ExecuteLine_CommitsOtherLinesEditFirst()
    {
        _viewModel.CreateScriptCommand.Execute(null);
        AddCommittedLine("0:00", "соло", [FirstTrack.Id]);
        AddCommittedLine("1:00", "черновик", []);
        var draft = _viewModel.Lines[1];
        draft.StartEdit();
        draft.EditText = "заметка";
        var solo = _viewModel.Lines[0];

        _viewModel.ExecuteLine(solo);

        Assert.False(draft.IsEditing);
        Assert.Equal("заметка", draft.Text);
        Assert.Equal(FirstTrack.Id, Assert.Single(_bus.Snapshot().Queue.Items).TrackId);
    }

    [Fact]
    public void CommitEdit_WithoutChanges_EmitsNoDelta()
    {
        _viewModel.CreateScriptCommand.Execute(null);
        AddCommittedLine("1:00", "строка", [FirstTrack.Id]);
        var line = _viewModel.Lines[0];
        line.StartEdit();
        var version = _bus.Snapshot().ShowVersion;

        _viewModel.CommitEdit(line);

        Assert.False(line.IsEditing);
        Assert.Equal(version, _bus.Snapshot().ShowVersion);
    }

    [Theory]
    [InlineData("1:45", 105)]
    [InlineData("01:23:45", 5025)]
    [InlineData("0:00", 0)]
    public void ParseLineTime_AcceptsValid(string text, int seconds)
    {
        Assert.True(ScriptPanelViewModel.TryParseLineTime(text, out var value));
        Assert.Equal(TimeSpan.FromSeconds(seconds), value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("бред")]
    [InlineData("1:99")]
    [InlineData("1:2:3:4")]
    public void ParseLineTime_RejectsInvalid(string text)
    {
        Assert.False(ScriptPanelViewModel.TryParseLineTime(text, out _));
    }

    private void AddCommittedLine(string time, string text, IReadOnlyList<TrackId> mentions)
    {
        _viewModel.AddLineCommand.Execute(null);
        var line = _viewModel.Lines[^1];
        line.EditTimeText = time;
        line.EditText = text;
        foreach (var track in mentions)
        {
            _viewModel.InsertMention(line, track);
        }
        _viewModel.CommitEdit(line);
    }

    [Fact]
    public void UnrelatedShowDelta_KeepsEditingLine_WithoutCollectionReset()
    {
        _viewModel.CreateScriptCommand.Execute(null);
        _viewModel.AddLineCommand.Execute(null);
        var line = Assert.Single(_viewModel.Lines);
        Assert.True(line.IsEditing);
        line.EditText = "черновик";
        var lineEvents = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        _viewModel.Lines.CollectionChanged += (_, e) => lineEvents.Add(e.Action);
        var scriptEvents = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        _viewModel.Scripts.CollectionChanged += (_, e) => scriptEvents.Add(e.Action);

        _bus.Submit(new ClientId("tick"), 1, new ResetShowClock());

        Assert.True(line.IsEditing);
        Assert.Same(line, Assert.Single(_viewModel.Lines));
        Assert.Equal("черновик", line.EditText);
        Assert.Empty(lineEvents);
        Assert.Empty(scriptEvents);
    }

    [Fact]
    public void ReorderWhileEditing_DefersCollectionReset_UntilCommit()
    {
        _viewModel.CreateScriptCommand.Execute(null);
        AddCommittedLine("0:00", "a", []);
        AddCommittedLine("1:00", "b", []);
        var first = _viewModel.Lines[0];
        var second = _viewModel.Lines[1];
        _viewModel.BeginEdit(second);
        second.EditText = "черновик";
        Assert.NotNull(_viewModel.SelectedScript);
        var script = _viewModel.SelectedScript!;

        _bus.Submit(new ClientId("ext"), 1, new MoveScriptLine(script.Id, first.Id, 1));

        Assert.Same(first, _viewModel.Lines[0]);
        Assert.Same(second, _viewModel.Lines[1]);
        Assert.True(second.IsEditing);
        Assert.Equal("черновик", second.EditText);

        _viewModel.CommitEdit(second);

        Assert.False(second.IsEditing);
        Assert.Equal("черновик", _viewModel.Lines[0].Text);
        Assert.Equal("a", _viewModel.Lines[1].Text);
    }
}
