namespace Aria.Core.Tests;

using System.Collections.Immutable;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.State;

public sealed class ScriptTests : IDisposable
{
    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void CreateScript_AddsScriptToShow()
    {
        var t1 = TestShow.Track();
        var p = TestShow.Project("Main", TestShow.Entry(t1));
        _harness.Submit(new LoadShow([t1], [p], p.Id));

        _harness.Submit(new CreateScript("Вечер"));

        var show = _harness.Snapshot.Show;
        var script = Assert.Single(show.Scripts);
        Assert.Equal("Вечер", script.Name);
        Assert.Empty(script.Lines);
    }

    [Fact]
    public void CreateScript_BindsActiveProject()
    {
        var t1 = TestShow.Track();
        var p = TestShow.Project("Main", TestShow.Entry(t1));
        _harness.Submit(new LoadShow([t1], [p], p.Id));

        _harness.Submit(new CreateScript("Вечер"));

        var script = Assert.Single(_harness.Snapshot.Show.Scripts);
        Assert.Equal<ProjectId?>(p.Id, script.Project);
    }

    [Fact]
    public void CreateScript_ExplicitProject_BindsIt()
    {
        var t1 = TestShow.Track();
        var a = TestShow.Project("A", TestShow.Entry(t1));
        var b = TestShow.Project("B", TestShow.Entry(t1));
        _harness.Submit(new LoadShow([t1], [a, b], a.Id));

        _harness.Submit(new CreateScript("Вечер", b.Id));

        var script = Assert.Single(_harness.Snapshot.Show.Scripts);
        Assert.Equal<ProjectId?>(b.Id, script.Project);
    }

    [Fact]
    public void DeleteProject_RemovesItsScripts_KeepsOthers()
    {
        var t1 = TestShow.Track();
        var a = TestShow.Project("A", TestShow.Entry(t1));
        var b = TestShow.Project("B", TestShow.Entry(t1));
        _harness.Submit(new LoadShow([t1], [a, b], a.Id));
        _harness.Submit(new CreateScript("Утро", a.Id));
        _harness.Submit(new CreateScript("Вечер", b.Id));

        _harness.Submit(new DeleteProject(a.Id));

        var script = Assert.Single(_harness.Snapshot.Show.Scripts);
        Assert.Equal("Вечер", script.Name);
    }

    [Fact]
    public void LoadShow_AdoptsOrphanScripts_ToActive()
    {
        var t1 = TestShow.Track();
        var a = TestShow.Project("A", TestShow.Entry(t1));
        var b = TestShow.Project("B", TestShow.Entry(t1));
        var orphan = new Script(ScriptId.New(), "Сирота", [], ProjectId.New());
        _harness.Submit(new LoadShow([t1], [a, b], b.Id, [orphan]));

        var script = Assert.Single(_harness.Snapshot.Show.Scripts);
        Assert.Equal<ProjectId?>(b.Id, script.Project);
    }

    [Fact]
    public void LoadShow_MigratesUnassignedScripts_ToActive()
    {
        var t1 = TestShow.Track();
        var p = TestShow.Project("Main", TestShow.Entry(t1));
        var legacy = new Script(ScriptId.New(), "Старый", []);
        _harness.Submit(new LoadShow([t1], [p], p.Id, [legacy]));

        var script = Assert.Single(_harness.Snapshot.Show.Scripts);
        Assert.Equal<ProjectId?>(p.Id, script.Project);
    }

    [Fact]
    public void CreateScript_BlankName_Rejects()
    {
        var seq = _harness.Submit(new CreateScript("  "));

        Assert.Equal("bad-name", _harness.RejectionOf(seq)?.Reason);
        Assert.Empty(_harness.Snapshot.Show.Scripts);
    }

    [Fact]
    public void RenameScript_UpdatesName()
    {
        _harness.Submit(new CreateScript("Вечер"));
        var id = _harness.Snapshot.Show.Scripts[0].Id;

        _harness.Submit(new RenameScript(id, "Утро"));

        Assert.Equal("Утро", _harness.Snapshot.Show.Scripts[0].Name);
    }

    [Fact]
    public void RenameScript_UnknownId_Rejects()
    {
        var seq = _harness.Submit(new RenameScript(ScriptId.New(), "Утро"));

        Assert.Equal("unknown-script", _harness.RejectionOf(seq)?.Reason);
    }

    [Fact]
    public void RenameScript_BlankName_Rejects()
    {
        _harness.Submit(new CreateScript("Вечер"));
        var id = _harness.Snapshot.Show.Scripts[0].Id;

        var seq = _harness.Submit(new RenameScript(id, " "));

        Assert.Equal("bad-name", _harness.RejectionOf(seq)?.Reason);
        Assert.Equal("Вечер", _harness.Snapshot.Show.Scripts[0].Name);
    }

    [Fact]
    public void DeleteScript_RemovesScript()
    {
        _harness.Submit(new CreateScript("Вечер"));
        var id = _harness.Snapshot.Show.Scripts[0].Id;

        _harness.Submit(new DeleteScript(id));

        Assert.Empty(_harness.Snapshot.Show.Scripts);
    }

    [Fact]
    public void DeleteScript_UnknownId_Rejects()
    {
        var seq = _harness.Submit(new DeleteScript(ScriptId.New()));

        Assert.Equal("unknown-script", _harness.RejectionOf(seq)?.Reason);
    }

    [Fact]
    public void AddScriptLine_AppendsLineWithMentions()
    {
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        var p = TestShow.Project("Main", TestShow.Entry(t1));
        _harness.Submit(new LoadShow([t1, t2], [p], p.Id));
        _harness.Submit(new CreateScript("Вечер"));
        var scriptId = _harness.Snapshot.Show.Scripts[0].Id;

        _harness.Submit(new AddScriptLine(scriptId, TimeSpan.FromSeconds(105), "вступление", [t1.Id, t2.Id]));

        var line = Assert.Single(_harness.Snapshot.Show.Scripts[0].Lines);
        Assert.Equal(TimeSpan.FromSeconds(105), line.AtElapsed);
        Assert.Equal("вступление", line.Text);
        Assert.Equal([t1.Id, t2.Id], line.Mentions.Select(m => m.Track));
    }

    [Fact]
    public void AddScriptLine_NegativeTime_Rejects()
    {
        _harness.Submit(new CreateScript("Вечер"));
        var scriptId = _harness.Snapshot.Show.Scripts[0].Id;

        var seq = _harness.Submit(new AddScriptLine(scriptId, TimeSpan.FromSeconds(-1), "x", []));

        Assert.Equal("bad-time", _harness.RejectionOf(seq)?.Reason);
        Assert.Empty(_harness.Snapshot.Show.Scripts[0].Lines);
    }

    [Fact]
    public void AddScriptLine_UnknownScript_Rejects()
    {
        var seq = _harness.Submit(new AddScriptLine(ScriptId.New(), TimeSpan.Zero, "x", []));

        Assert.Equal("unknown-script", _harness.RejectionOf(seq)?.Reason);
    }

    [Fact]
    public void AddScriptLine_DanglingMention_Accepted()
    {
        _harness.Submit(new CreateScript("Вечер"));
        var scriptId = _harness.Snapshot.Show.Scripts[0].Id;

        _harness.Submit(new AddScriptLine(scriptId, TimeSpan.Zero, "удалён", [TrackId.New()]));

        var line = Assert.Single(_harness.Snapshot.Show.Scripts[0].Lines);
        Assert.Single(line.Mentions);
        Assert.Empty(_harness.Snapshot.Show.TrackDigest.Entries);
    }

    [Fact]
    public void UpdateScriptLine_EditsFields()
    {
        var t1 = TestShow.Track("one");
        var t2 = TestShow.Track("two");
        _harness.Submit(new CreateScript("Вечер"));
        var scriptId = _harness.Snapshot.Show.Scripts[0].Id;
        _harness.Submit(new AddScriptLine(scriptId, TimeSpan.Zero, "черновик", [t1.Id]));
        var lineId = _harness.Snapshot.Show.Scripts[0].Lines[0].Id;

        _harness.Submit(new UpdateScriptLine(scriptId, lineId, TimeSpan.FromSeconds(60), "чистовик", [t2.Id]));

        var line = Assert.Single(_harness.Snapshot.Show.Scripts[0].Lines);
        Assert.Equal(TimeSpan.FromSeconds(60), line.AtElapsed);
        Assert.Equal("чистовик", line.Text);
        Assert.Equal([t2.Id], line.Mentions.Select(m => m.Track));
    }

    [Fact]
    public void UpdateScriptLine_UnknownLine_Rejects()
    {
        _harness.Submit(new CreateScript("Вечер"));
        var scriptId = _harness.Snapshot.Show.Scripts[0].Id;

        var seq = _harness.Submit(new UpdateScriptLine(scriptId, ScriptLineId.New(), TimeSpan.Zero, "x", []));

        Assert.Equal("unknown-line", _harness.RejectionOf(seq)?.Reason);
    }

    [Fact]
    public void RemoveScriptLine_RemovesLine()
    {
        _harness.Submit(new CreateScript("Вечер"));
        var scriptId = _harness.Snapshot.Show.Scripts[0].Id;
        _harness.Submit(new AddScriptLine(scriptId, TimeSpan.Zero, "a", []));
        _harness.Submit(new AddScriptLine(scriptId, TimeSpan.FromSeconds(10), "b", []));
        var first = _harness.Snapshot.Show.Scripts[0].Lines[0].Id;

        _harness.Submit(new RemoveScriptLine(scriptId, first));

        var line = Assert.Single(_harness.Snapshot.Show.Scripts[0].Lines);
        Assert.Equal("b", line.Text);
    }

    [Fact]
    public void MoveScriptLine_ReordersLines()
    {
        _harness.Submit(new CreateScript("Вечер"));
        var scriptId = _harness.Snapshot.Show.Scripts[0].Id;
        _harness.Submit(new AddScriptLine(scriptId, TimeSpan.Zero, "a", []));
        _harness.Submit(new AddScriptLine(scriptId, TimeSpan.FromSeconds(10), "b", []));
        _harness.Submit(new AddScriptLine(scriptId, TimeSpan.FromSeconds(20), "c", []));
        var first = _harness.Snapshot.Show.Scripts[0].Lines[0].Id;

        _harness.Submit(new MoveScriptLine(scriptId, first, 2));

        Assert.Equal("b", _harness.Snapshot.Show.Scripts[0].Lines[0].Text);
        Assert.Equal("c", _harness.Snapshot.Show.Scripts[0].Lines[1].Text);
        Assert.Equal("a", _harness.Snapshot.Show.Scripts[0].Lines[2].Text);
    }

    [Fact]
    public void MoveScriptLine_BadIndex_Rejects()
    {
        _harness.Submit(new CreateScript("Вечер"));
        var scriptId = _harness.Snapshot.Show.Scripts[0].Id;
        _harness.Submit(new AddScriptLine(scriptId, TimeSpan.Zero, "a", []));
        var line = _harness.Snapshot.Show.Scripts[0].Lines[0].Id;

        var seq = _harness.Submit(new MoveScriptLine(scriptId, line, 5));

        Assert.Equal("bad-index", _harness.RejectionOf(seq)?.Reason);
    }

    [Fact]
    public void ScriptCommands_WhenLocked_AreRejected()
    {
        _harness.Submit(new SetLocked(true));

        var seq = _harness.Submit(new CreateScript("Вечер"));

        Assert.Equal("locked", _harness.RejectionOf(seq)?.Reason);
    }

    [Fact]
    public void LoadShow_WithoutScripts_LeavesEmptyScripts()
    {
        var t1 = TestShow.Track();
        var p = TestShow.Project("Main", TestShow.Entry(t1));

        _harness.Submit(new LoadShow([t1], [p], p.Id));

        Assert.Empty(_harness.Snapshot.Show.Scripts);
    }

    [Fact]
    public void LoadShow_WithScripts_RestoresScripts()
    {
        var t1 = TestShow.Track("one");
        var scripts = ImmutableArray.Create(
            new Script(ScriptId.New(), "Вечер",
            [
                new ScriptLine(ScriptLineId.New(), TimeSpan.FromSeconds(30), "старт", [new Mention(t1.Id)]),
            ]));
        var p = TestShow.Project("Main", TestShow.Entry(t1));

        _harness.Submit(new LoadShow([t1], [p], p.Id, scripts));

        var restored = Assert.Single(_harness.Snapshot.Show.Scripts);
        Assert.Equal("Вечер", restored.Name);
        Assert.Equal("старт", Assert.Single(restored.Lines).Text);
    }

    [Fact]
    public void TrackDigest_CoversProjectQueueDeckAndMentions()
    {
        var projectTrack = TestShow.Track("in-playlist");
        var libraryTrack = TestShow.Track("library-only");
        var mentionedTrack = TestShow.Track("mentioned");
        var entry = TestShow.Entry(projectTrack);
        var p = TestShow.Project("Main", entry);
        _harness.Submit(new LoadShow([projectTrack, libraryTrack, mentionedTrack], [p], p.Id));
        _harness.Submit(new CreateScript("Вечер"));
        var scriptId = _harness.Snapshot.Show.Scripts[0].Id;
        _harness.Submit(new AddScriptLine(scriptId, TimeSpan.Zero, "строка", [mentionedTrack.Id, TrackId.New()]));
        _harness.Submit(new EnqueueTrack(libraryTrack.Id));

        var digest = _harness.Snapshot.Show.TrackDigest.Entries.ToDictionary(e => e.Track, e => e.DisplayName);

        Assert.Equal("in-playlist", digest[projectTrack.Id]);
        Assert.Equal("library-only", digest[libraryTrack.Id]);
        Assert.Equal("mentioned", digest[mentionedTrack.Id]);
        Assert.Equal(3, digest.Count);
    }

    [Fact]
    public void EnqueueLibraryOnlyTrack_EmitsShowDigestDelta()
    {
        var projectTrack = TestShow.Track("in-playlist");
        var libraryTrack = TestShow.Track("library-only");
        var p = TestShow.Project("Main", TestShow.Entry(projectTrack));
        _harness.Submit(new LoadShow([projectTrack, libraryTrack], [p], p.Id));
        var baseline = _harness.Events.OfType<ShowDelta>().Count();

        _harness.Submit(new EnqueueTrack(libraryTrack.Id));

        var deltas = _harness.Events.OfType<ShowDelta>().Skip(baseline).ToList();
        var latest = Assert.Single(deltas);
        Assert.Contains(latest.State.TrackDigest.Entries, e => e.Track == libraryTrack.Id);
    }

    [Fact]
    public void EnqueueProjectTrack_DoesNotEmitExtraShowDelta()
    {
        var t1 = TestShow.Track("one");
        var p = TestShow.Project("Main", TestShow.Entry(t1));
        _harness.Submit(new LoadShow([t1], [p], p.Id));
        var baseline = _harness.Events.OfType<ShowDelta>().Count();

        _harness.Submit(new EnqueueEntry(p.Entries[0].Id));

        Assert.Equal(baseline, _harness.Events.OfType<ShowDelta>().Count());
    }

    [Fact]
    public void MergeTracks_RenamedTrack_UpdatesDigest()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Project("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        var renamed = t1 with { DefaultName = "uno" };

        h.Submit(new MergeTracks([renamed]));

        Assert.Equal("uno", Assert.Single(h.Snapshot.Show.TrackDigest.Entries).DisplayName);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(30, 0)]
    [InlineData(105, 1)]
    [InlineData(1000, 2)]
    public void Follow_SelectsCurrentLine(int elapsedSeconds, int expectedIndex)
    {
        var script = new Script(ScriptId.New(), "Вечер",
        [
            new ScriptLine(ScriptLineId.New(), TimeSpan.Zero, "a", []),
            new ScriptLine(ScriptLineId.New(), TimeSpan.FromSeconds(100), "b", []),
            new ScriptLine(ScriptLineId.New(), TimeSpan.FromSeconds(200), "c", []),
        ]);

        var current = ScriptFollow.CurrentLine(script, TimeSpan.FromSeconds(elapsedSeconds));

        Assert.Same(script.Lines[expectedIndex], current);
    }

    [Fact]
    public void Follow_BeforeFirstTimedLine_ReturnsNull()
    {
        var script = new Script(ScriptId.New(), "Вечер",
        [
            new ScriptLine(ScriptLineId.New(), TimeSpan.FromSeconds(100), "b", []),
        ]);

        Assert.Null(ScriptFollow.CurrentLine(script, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Follow_EmptyScript_ReturnsNull()
    {
        var script = new Script(ScriptId.New(), "Вечер", []);

        Assert.Null(ScriptFollow.CurrentLine(script, TimeSpan.FromSeconds(10)));
    }
}
