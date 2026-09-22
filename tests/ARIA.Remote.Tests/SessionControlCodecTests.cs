namespace Aria.Remote.Tests;

using Aria.Core.Commands;
using Aria.Core.Playback;

public sealed class SessionControlCodecTests
{
    [Fact]
    public void RenameSession_Parses()
    {
        var json = "{\"client\":\"c\",\"seq\":1,\"command\":{\"type\":\"rename_session\",\"session\":7,\"name\":\"Drums\"}}";
        Assert.True(CommandCodec.TryParse(json, out _, out _, out var command));
        var parsed = Assert.IsType<RenameSession>(command);
        Assert.Equal(new PreviewSessionHandle(7), parsed.Session);
        Assert.Equal("Drums", parsed.Name);
    }

    [Fact]
    public void SetSessionBackingGain_Parses()
    {
        var json = "{\"client\":\"c\",\"seq\":1,\"command\":{\"type\":\"set_session_backing_gain\",\"session\":7,\"gain_db\":-6}}";
        Assert.True(CommandCodec.TryParse(json, out _, out _, out var command));
        var parsed = Assert.IsType<SetSessionBackingGain>(command);
        Assert.Equal(new PreviewSessionHandle(7), parsed.Session);
        Assert.Equal(-6, parsed.GainDb);
    }

    [Fact]
    public void CloseSession_Parses()
    {
        var json = "{\"client\":\"c\",\"seq\":1,\"command\":{\"type\":\"close_session\",\"session\":7}}";
        Assert.True(CommandCodec.TryParse(json, out _, out _, out var command));
        var parsed = Assert.IsType<CloseSession>(command);
        Assert.Equal(new PreviewSessionHandle(7), parsed.Session);
    }
}
