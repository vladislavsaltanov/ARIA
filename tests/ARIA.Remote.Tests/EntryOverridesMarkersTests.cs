namespace Aria.Remote.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;

public sealed class EntryOverridesMarkersTests
{
    private static string Json(string command) =>
        "{\"client\":\"c\",\"seq\":1,\"command\":" + command + "}";

    [Fact]
    public void SetEntryOverrides_WithMarkers_ParsesPositionAndName()
    {
        var entry = Guid.NewGuid().ToString("N");
        var json = Json(
            "{\"type\":\"set_entry_overrides\",\"entry\":\"" + entry + "\",\"overrides\":{"
            + "\"name\":\"Буря\",\"markers\":[{\"name\":\"гром\",\"position_ticks\":300000000,\"action\":0}]}}");

        Assert.True(CommandCodec.TryParse(json, out _, out _, out var command));
        var parsed = Assert.IsType<SetEntryOverrides>(command);
        Assert.Equal(entry, parsed.Entry.Value.ToString("N"));
        Assert.Equal("Буря", parsed.Overrides!.Name);
        var marker = Assert.Single(parsed.Overrides.Markers!.Value);
        Assert.Equal("гром", marker.Name);
        Assert.Equal(TimeSpan.FromSeconds(30), marker.Position);
        Assert.Equal(MarkerAction.Stop, marker.Action);
    }

    [Fact]
    public void SetEntryOverrides_WithoutMarkers_ParsesMarkersAsNull()
    {
        var entry = Guid.NewGuid().ToString("N");
        var json = Json(
            "{\"type\":\"set_entry_overrides\",\"entry\":\"" + entry + "\",\"overrides\":{\"name\":\"Тишина\"}}");

        Assert.True(CommandCodec.TryParse(json, out _, out _, out var command));
        var parsed = Assert.IsType<SetEntryOverrides>(command);
        Assert.Equal("Тишина", parsed.Overrides!.Name);
        Assert.Null(parsed.Overrides.Markers);
    }
}
