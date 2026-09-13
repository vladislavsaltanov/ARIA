namespace Aria.Remote.Tests;

using Aria.Core.Model;
using Aria.Remote;

public sealed class DefaultEndActionCodecTests
{
    [Theory]
    [InlineData("pause", EndAction.Pause)]
    [InlineData("stop", EndAction.Stop)]
    [InlineData("replay", EndAction.Replay)]
    [InlineData("advance", EndAction.Advance)]
    [InlineData("REPLAY", EndAction.Replay)]
    public void SetDefaultEndAction_ParsesAction(string text, EndAction expected)
    {
        var json = "{\"client\":\"pult-1\",\"seq\":7,\"command\":{\"type\":\"set_default_end_action\",\"end_action\":\"" + text + "\"}}";

        Assert.True(CommandCodec.TryParse(json, out var client, out var seq, out var command));

        Assert.Equal("pult-1", client);
        Assert.Equal(7, seq);
        var parsed = Assert.IsType<Aria.Core.Commands.SetDefaultEndAction>(command);
        Assert.Equal(expected, parsed.Action);
    }

    [Fact]
    public void SetDefaultEndAction_UnknownAction_Throws()
    {
        var json = "{\"client\":\"pult-1\",\"seq\":7,\"command\":{\"type\":\"set_default_end_action\",\"end_action\":\"loop\"}}";

        Assert.Throws<FormatException>(() => CommandCodec.TryParse(json, out _, out _, out _));
    }
}
