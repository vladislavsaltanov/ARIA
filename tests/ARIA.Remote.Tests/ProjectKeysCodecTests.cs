namespace Aria.Remote.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;

public sealed class ProjectKeysCodecTests
{
    private static string Json(string command) =>
        "{\"client\":\"c\",\"seq\":1,\"command\":" + command + "}";

    private static bool TryParseType(string type, string fields, out Command? command)
    {
        var ok = CommandCodec.TryParse(Json("{\"type\":\"" + type + "\"" + fields + "}"), out _, out _, out command);
        return ok;
    }

    [Theory]
    [InlineData("create_project")]
    [InlineData("create_playlist")]
    public void CreateProject_Parses(string type)
    {
        Assert.True(TryParseType(type, ",\"name\":\"Main\"", out var command));
        Assert.Equal("Main", Assert.IsType<CreateProject>(command).Name);
    }

    [Theory]
    [InlineData("rename_project")]
    [InlineData("rename_playlist")]
    public void RenameProject_Parses(string type)
    {
        var id = Guid.NewGuid().ToString("N");

        Assert.True(TryParseType(type, ",\"id\":\"" + id + "\",\"name\":\"Main\"", out var command));
        var parsed = Assert.IsType<RenameProject>(command);
        Assert.Equal(id, parsed.Id.Value.ToString("N"));
        Assert.Equal("Main", parsed.Name);
    }

    [Theory]
    [InlineData("delete_project")]
    [InlineData("delete_playlist")]
    public void DeleteProject_Parses(string type)
    {
        var id = Guid.NewGuid().ToString("N");

        Assert.True(TryParseType(type, ",\"id\":\"" + id + "\"", out var command));
        Assert.Equal(id, Assert.IsType<DeleteProject>(command).Id.Value.ToString("N"));
    }

    [Theory]
    [InlineData("set_active_project")]
    [InlineData("set_active_playlist")]
    public void SetActiveProject_Parses(string type)
    {
        var id = Guid.NewGuid().ToString("N");

        Assert.True(TryParseType(type, ",\"id\":\"" + id + "\"", out var command));
        Assert.Equal(id, Assert.IsType<SetActiveProject>(command).Id.Value.ToString("N"));
    }

    [Theory]
    [InlineData("normalize_project")]
    [InlineData("normalize_playlist")]
    public void NormalizeProject_Parses(string type)
    {
        var id = Guid.NewGuid().ToString("N");

        Assert.True(TryParseType(type, ",\"playlist\":\"" + id + "\"", out var command));
        Assert.Equal(id, Assert.IsType<NormalizeProject>(command).Project.Value.ToString("N"));
    }

    [Fact]
    public void AddEntry_ProjectField_Parses()
    {
        var project = Guid.NewGuid().ToString("N");
        var track = Guid.NewGuid().ToString("N");

        Assert.True(TryParseType("add_entry", ",\"project\":\"" + project + "\",\"track\":\"" + track + "\"", out var command));
        var parsed = Assert.IsType<AddEntry>(command);
        Assert.Equal(project, parsed.Project.Value.ToString("N"));
        Assert.Equal(track, parsed.Track.Value.ToString("N"));
    }

    [Fact]
    public void AddEntry_LegacyPlaylistField_Parses()
    {
        var project = Guid.NewGuid().ToString("N");
        var track = Guid.NewGuid().ToString("N");

        Assert.True(TryParseType("add_entry", ",\"playlist\":\"" + project + "\",\"track\":\"" + track + "\"", out var command));
        var parsed = Assert.IsType<AddEntry>(command);
        Assert.Equal(project, parsed.Project.Value.ToString("N"));
        Assert.Equal(track, parsed.Track.Value.ToString("N"));
    }
}
