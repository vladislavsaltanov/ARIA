namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.State;

public sealed class NoteEntryTests
{
    private static (Harness Harness, Project Project, Track First, Track Second) ShowWithNote()
    {
        var h = new Harness();
        var first = TestShow.Track("one");
        var second = TestShow.Track("two");
        var project = TestShow.Project("Main", TestShow.Entry(first), TestShow.Note("после фанфар — свет"), TestShow.Entry(second));
        h.Submit(new LoadShow([first, second], [project], project.Id));
        return (h, project, first, second);
    }

    [Fact]
    public void AddNote_InsertsAtRequestedIndex()
    {
        using var h = new Harness();
        var track = TestShow.Track("one");
        var project = TestShow.Project("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [project], project.Id));

        h.Submit(new AddNoteEntry(project.Id, "  свет гаснет  ", 1));

        var entries = h.Snapshot.Show.Projects[0].Entries;
        Assert.Equal(2, entries.Length);
        var note = Assert.IsType<NoteContent>(entries[1].Content);
        Assert.Equal("свет гаснет", note.Text);
    }

    [Fact]
    public void AddNote_BlankOrOversizedText_IsRejected()
    {
        using var h = new Harness();
        var track = TestShow.Track("one");
        var project = TestShow.Project("Main", TestShow.Entry(track));
        h.Submit(new LoadShow([track], [project], project.Id));

        var blank = h.Submit(new AddNoteEntry(project.Id, "   "));
        var oversized = h.Submit(new AddNoteEntry(project.Id, new string('x', NoteContent.MaxLength + 1)));
        var badIndex = h.Submit(new AddNoteEntry(project.Id, "ok", 5));

        Assert.Equal("bad-text", h.RejectionOf(blank)?.Reason);
        Assert.Equal("bad-text", h.RejectionOf(oversized)?.Reason);
        Assert.Equal("bad-index", h.RejectionOf(badIndex)?.Reason);
        Assert.Single(h.Snapshot.Show.Projects[0].Entries);
    }

    [Fact]
    public void SetNoteText_UpdatesText_UndoRestores()
    {
        using var h = new Harness();
        var track = TestShow.Track("one");
        var note = TestShow.Note("старый текст");
        var project = TestShow.Project("Main", TestShow.Entry(track), note);
        h.Submit(new LoadShow([track], [project], project.Id));

        h.Submit(new SetNoteText(note.Id, "новый текст"));
        Assert.Equal("новый текст", ((NoteContent)h.Snapshot.Show.Projects[0].Entries[1].Content).Text);

        h.Submit(new Undo());
        Assert.Equal("старый текст", ((NoteContent)h.Snapshot.Show.Projects[0].Entries[1].Content).Text);
    }

    [Fact]
    public void SetNoteText_OnTrackEntry_IsRejected()
    {
        using var h = new Harness();
        var track = TestShow.Track("one");
        var entry = TestShow.Entry(track);
        var project = TestShow.Project("Main", entry);
        h.Submit(new LoadShow([track], [project], project.Id));

        var seq = h.Submit(new SetNoteText(entry.Id, "текст"));

        Assert.Equal("track-entry", h.RejectionOf(seq)?.Reason);
    }

    [Fact]
    public void Next_SkipsNote()
    {
        var (h, _, first, second) = ShowWithNote();
        using var _h = h;
        h.Submit(new Play());
        Assert.Equal(first.Id, h.Transport.Current!.TrackId);

        h.Submit(new Next());

        Assert.Equal(second.Id, h.Transport.Current!.TrackId);
    }

    [Fact]
    public void AutoAdvance_SkipsNote()
    {
        var (h, _, first, second) = ShowWithNote();
        using var _h = h;
        h.Submit(new Play());

        h.Engine.End(h.Engine.Created[0].Handle, StreamEndReason.Completed);

        Assert.Equal(second.Id, h.Transport.Current!.TrackId);
    }

    [Fact]
    public void PeekNext_LooksPastNote()
    {
        using var h = new Harness();
        var first = TestShow.Track("one");
        var second = TestShow.Track("two");
        var entries = new[] { TestShow.Entry(first), TestShow.Note("ремарка"), TestShow.Entry(second) };
        var project = TestShow.Project("Main", entries);
        h.Submit(new LoadShow([first, second], [project], project.Id));

        Assert.Equal(first.Id, h.Transport.Next!.TrackId);

        h.Submit(new Play());
        Assert.Equal(second.Id, h.Transport.Next!.TrackId);
    }

    [Fact]
    public void Play_StartsAtTrack_WhenProjectBeginsWithNote()
    {
        using var h = new Harness();
        var track = TestShow.Track("one");
        var project = TestShow.Project("Main", TestShow.Note("до начала"), TestShow.Entry(track));
        h.Submit(new LoadShow([track], [project], project.Id));

        h.Submit(new Play());

        Assert.Equal(track.Id, h.Transport.Current!.TrackId);
    }

    [Fact]
    public void TrailingNote_StopsAfterLastTrack()
    {
        using var h = new Harness();
        var track = TestShow.Track("one");
        var project = TestShow.Project("Main", TestShow.Entry(track), TestShow.Note("конец"));
        h.Submit(new LoadShow([track], [project], project.Id));
        h.Submit(new Play());

        Assert.Null(h.Transport.Next);

        h.Engine.End(h.Engine.Created[0].Handle, StreamEndReason.Completed);

        Assert.Null(h.Transport.Current);
        Assert.Equal(TransportStatus.Stopped, h.Transport.Status);
    }

    [Fact]
    public void CommandsTargetingNoteAsTrack_AreRejected()
    {
        using var h = new Harness();
        var track = TestShow.Track("one");
        var note = TestShow.Note("ремарка");
        var project = TestShow.Project("Main", TestShow.Entry(track), note);
        h.Submit(new LoadShow([track], [project], project.Id));

        var jump = h.Submit(new JumpTo(note.Id));
        var enqueue = h.Submit(new EnqueueEntry(note.Id));
        var overrides = h.Submit(new SetEntryOverrides(note.Id, new ProjectOverrides(Name: "x")));
        var audio = h.Submit(new SetEntryAudio(note.Id, TrackAudioSettings.Default));
        var markers = h.Submit(new SetEntryMarkers(note.Id, []));

        Assert.Equal("note-entry", h.RejectionOf(jump)?.Reason);
        Assert.Equal("note-entry", h.RejectionOf(enqueue)?.Reason);
        Assert.Equal("note-entry", h.RejectionOf(overrides)?.Reason);
        Assert.Equal("note-entry", h.RejectionOf(audio)?.Reason);
        Assert.Equal("note-entry", h.RejectionOf(markers)?.Reason);
        Assert.Empty(h.Transport.Faulted);
    }

    [Fact]
    public void MoveAndRemove_Note_Work()
    {
        var (h, project, _, _) = ShowWithNote();
        using var _h = h;
        var note = h.Snapshot.Show.Projects[0].Entries[1];

        h.Submit(new MoveEntry(note.Id, 2));
        Assert.Equal(note.Id, h.Snapshot.Show.Projects[0].Entries[2].Id);

        h.Submit(new RemoveEntry(note.Id));
        Assert.Equal(2, h.Snapshot.Show.Projects[0].Entries.Length);
        Assert.DoesNotContain(h.Snapshot.Show.Projects[0].Entries, e => e.Id == note.Id);
        Assert.Equal(project.Id, h.Snapshot.Show.Projects[0].Id);
    }

    [Fact]
    public void LoadShow_WithNote_IsAccepted_DigestSkipsNote()
    {
        var (h, project, first, second) = ShowWithNote();
        using var _h = h;
        var snapshot = h.Snapshot.Show;

        Assert.Equal(project.Id, snapshot.ActiveId);
        Assert.Equal(3, Assert.Single(snapshot.Projects).Entries.Length);
        Assert.Equal(
            new[] { first.Id, second.Id }.OrderBy(id => id.Value),
            snapshot.TrackDigest.Entries.Select(e => e.Track).OrderBy(id => id.Value));
        Assert.DoesNotContain(snapshot.Projects[0].Entries, e => e.Content is TrackContent content && content.TrackId == default);
    }

    [Fact]
    public void SetNoteText_NoOp_WhenTextIsUnchanged()
    {
        using var h = new Harness();
        var track = TestShow.Track("one");
        var note = TestShow.Note("текст");
        var project = TestShow.Project("Main", TestShow.Entry(track), note);
        h.Submit(new LoadShow([track], [project], project.Id));
        var version = h.Snapshot.ShowVersion;

        h.Submit(new SetNoteText(note.Id, "  текст  "));

        Assert.Equal(version, h.Snapshot.ShowVersion);
    }
}
