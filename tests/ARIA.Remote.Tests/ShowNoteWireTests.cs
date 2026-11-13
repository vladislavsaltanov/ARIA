namespace Aria.Remote.Tests;

using System.Text.Json;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.State;

public sealed class ShowNoteWireTests
{
    [Fact]
    public void ShowState_SerializesNoteContent_WithKindAndText()
    {
        var trackId = TrackId.New();
        var project = new Project(
            ProjectId.New(),
            "Main",
            [new ProjectEntry(EntryId.New(), new TrackContent(trackId)), new ProjectEntry(EntryId.New(), new NoteContent("cue card"))]);
        var show = new ShowState(
            [project],
            project.Id,
            false,
            new ShowClockState(TimeSpan.Zero, false),
            [],
            TrackDigest.Empty,
            EndAction.Advance);

        var json = JsonSerializer.Serialize(show, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        using var document = JsonDocument.Parse(json);
        var entries = document.RootElement.GetProperty("projects")[0].GetProperty("entries");
        var track = entries[0].GetProperty("content");
        var note = entries[1].GetProperty("content");

        Assert.Equal("track", track.GetProperty("kind").GetString());
        Assert.True(track.TryGetProperty("trackId", out _));
        Assert.False(track.TryGetProperty("text", out _));

        Assert.Equal("note", note.GetProperty("kind").GetString());
        Assert.Equal("cue card", note.GetProperty("text").GetString());
        Assert.False(note.TryGetProperty("trackId", out _));
    }
}
