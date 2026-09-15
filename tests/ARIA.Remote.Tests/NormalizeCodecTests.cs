namespace Aria.Remote.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;

public sealed class NormalizeCodecTests
{
    private static string Json(string command) =>
        "{\"client\":\"c\",\"seq\":1,\"command\":" + command + "}";

    [Fact]
    public void NormalizeTrack_Parses()
    {
        var track = Guid.NewGuid().ToString("N");

        Assert.True(CommandCodec.TryParse(Json("{\"type\":\"normalize_track\",\"track\":\"" + track + "\"}"), out _, out _, out var command));
        Assert.Equal(track, Assert.IsType<NormalizeTrack>(command).Track.Value.ToString("N"));
    }

    [Fact]
    public void NormalizePlaylist_Parses()
    {
        var id = Guid.NewGuid().ToString("N");

        Assert.True(CommandCodec.TryParse(Json("{\"type\":\"normalize_playlist\",\"playlist\":\"" + id + "\"}"), out _, out _, out var command));
        Assert.Equal(id, Assert.IsType<NormalizePlaylist>(command).Playlist.Value.ToString("N"));
    }

    private static string GlobalJson(string extra = "") =>
        "{\"client\":\"c\",\"seq\":1,\"command\":{\"type\":\"set_global_audio\","
        + "\"pan\":0,\"mono\":false,\"hpf_hz\":0,\"eq\":{\"bands\":["
        + string.Join(",", AudioEq.DefaultFrequencies.Select(f =>
            "{\"freq_hz\":" + f + ",\"gain_db\":0,\"q\":1}"))
        + "]},\"limiter\":{\"enabled\":true,\"threshold_db\":-1,\"release_ms\":100}" + extra + "}}";

    [Fact]
    public void SetGlobalAudio_NewFields_Parse()
    {
        var json = GlobalJson(",\"normalize_target_lufs\":-23,\"normalize_enabled\":true,\"meter_zones\":{\"green_db\":-18,\"yellow_db\":-12,\"red_db\":-4}");

        Assert.True(CommandCodec.TryParse(json, out _, out _, out var command));
        var parsed = Assert.IsType<SetGlobalAudio>(command);
        Assert.Equal(-23, parsed.Value.NormalizeTargetLufs);
        Assert.True(parsed.Value.NormalizeEnabled);
        Assert.Equal(new LufsMeterZones(-18, -12, -4), parsed.Value.EffectiveZones);
    }

    [Fact]
    public void SetGlobalAudio_LegacyJson_UsesDefaults()
    {
        Assert.True(CommandCodec.TryParse(GlobalJson(), out _, out _, out var command));
        var parsed = Assert.IsType<SetGlobalAudio>(command);
        Assert.Equal(-16.0, parsed.Value.NormalizeTargetLufs);
        Assert.False(parsed.Value.NormalizeEnabled);
        Assert.Equal(LufsMeterZones.Default, parsed.Value.EffectiveZones);
    }

    [Fact]
    public void SetGlobalAudio_UnorderedZones_Throws()
    {
        var json = GlobalJson(",\"meter_zones\":{\"green_db\":-9,\"yellow_db\":-15,\"red_db\":-5}");

        Assert.Throws<FormatException>(() => CommandCodec.TryParse(json, out _, out _, out _));
    }
}
