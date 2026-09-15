namespace Aria.Remote.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;

public sealed class NormalizeCodecTests
{
    private static string Json(double target) =>
        "{\"client\":\"c\",\"seq\":1,\"command\":{\"type\":\"normalize_track_to_lufs\","
        + "\"track\":\"" + Guid.NewGuid().ToString("N") + "\",\"target_lufs\":" + target + "}}";

    [Fact]
    public void NormalizeTrackToLufs_Parses()
    {
        Assert.True(CommandCodec.TryParse(Json(-16), out _, out _, out var command));
        var parsed = Assert.IsType<NormalizeTrackToLufs>(command);
        Assert.Equal(-16, parsed.TargetLufs);
    }

    [Fact]
    public void NormalizeTrackToLufs_TooHot_Throws()
    {
        Assert.Throws<FormatException>(() => CommandCodec.TryParse(Json(-6), out _, out _, out _));
    }

    [Fact]
    public void NormalizeTrackToLufs_TooQuiet_Throws()
    {
        Assert.Throws<FormatException>(() => CommandCodec.TryParse(Json(-48), out _, out _, out _));
    }
}
