namespace Aria.Remote.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;

public sealed class EntryOverridesBackingCodecTests
{
    private static string Json(string command) =>
        "{\"client\":\"c\",\"seq\":1,\"command\":" + command + "}";

    [Fact]
    public void ParseOverrides_Backing_Guid()
    {
        var entry = Guid.NewGuid();
        var backing = Guid.NewGuid();
        var json = Json(
            "{\"type\":\"set_entry_overrides\",\"entry\":\"" + entry + "\",\"overrides\":{\"name\":\"Песня\",\"backing\":\"" + backing + "\"}}");

        Assert.True(CommandCodec.TryParse(json, out _, out _, out var command));
        var parsed = Assert.IsType<SetEntryOverrides>(command);
        Assert.Equal(new TrackId(backing), parsed.Overrides!.Backing);
        Assert.Equal("Песня", parsed.Overrides.Name);
    }

    [Fact]
    public void ParseOverrides_NoBacking_Null()
    {
        var json = Json(
            "{\"type\":\"set_entry_overrides\",\"entry\":\"" + Guid.NewGuid() + "\",\"overrides\":{\"name\":\"Песня\"}}");

        Assert.True(CommandCodec.TryParse(json, out _, out _, out var command));
        Assert.Null(Assert.IsType<SetEntryOverrides>(command).Overrides!.Backing);
    }

    [Fact]
    public void AppJs_PreservesBacking()
    {
        using var stream = typeof(RemoteHost).Assembly.GetManifestResourceStream("Aria.Remote.RemoteClient.app.js");
        using var reader = new StreamReader(stream!);

        Assert.Contains("overrides.backing = entry.overrides.backing", reader.ReadToEnd());
    }
}
