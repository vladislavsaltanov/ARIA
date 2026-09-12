namespace Aria.Core.Tests;

using System.Collections.Immutable;
using Aria.Core.Model;

public sealed class PlaylistFormatTests
{
    [Fact]
    public void ExportImport_RoundTrips_NameOverridesAndTransition()
    {
        var entries = ImmutableArray.Create(
            new PlaylistExportEntry(
                "/audio/one.flac",
                new PlaylistOverrides("Утро", "#FF0000", "опенер", 1.5, EndAction.Advance,
                    new Fade(TimeSpan.FromSeconds(2), FadeCurve.Exponential),
                    new Fade(TimeSpan.FromSeconds(5), FadeCurve.SCurve),
                    TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(170)),
                new PlaylistFileTransition("crossfade", 4)),
            new PlaylistExportEntry("/audio/two.flac"));

        var json = PlaylistFormat.Export("Вечер", entries);
        var document = PlaylistFormat.Import(json);

        Assert.Equal("aria-playlist", document.Format);
        Assert.Equal(1, document.Version);
        Assert.Equal("Вечер", document.Name);
        var first = Assert.Single(document.Entries.Take(1));
        Assert.Equal("/audio/one.flac", first.File);
        Assert.Equal("Утро", first.Name);
        Assert.Equal("#FF0000", first.Color);
        Assert.Equal("опенер", first.Note);
        Assert.Equal(1.5, first.GainDb);
        Assert.Equal("advance", first.End, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(2, first.In?.Seconds);
        Assert.Equal("exponential", first.In?.Curve, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(5, first.Out?.Seconds);
        Assert.Equal(1, first.CueIn);
        Assert.Equal(170, first.CueOut);
        Assert.Equal("crossfade", first.Transition?.Kind, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(4, first.Transition?.Seconds);
        var second = document.Entries[1];
        Assert.Equal("/audio/two.flac", second.File);
        Assert.Null(PlaylistFormat.ToOverrides(second));
    }

    [Fact]
    public void ToOverrides_MapsAllFields()
    {
        var document = PlaylistFormat.Import(PlaylistFormat.Export("Шоу", ImmutableArray.Create(
            new PlaylistExportEntry("/audio/one.flac",
                new PlaylistOverrides(GainDb: -3, EndAction: EndAction.Replay)))));

        var overrides = PlaylistFormat.ToOverrides(document.Entries[0]);
        Assert.NotNull(overrides);
        Assert.Equal(-3, overrides.GainDb);
        Assert.Equal(EndAction.Replay, overrides.EndAction);
    }

    [Theory]
    [InlineData("{\"format\":\"m3u\",\"version\":1,\"name\":\"Шоу\",\"entries\":[{\"file\":\"a.flac\"}]}", "bad-format")]
    [InlineData("{\"format\":\"aria-playlist\",\"version\":2,\"name\":\"Шоу\",\"entries\":[{\"file\":\"a.flac\"}]}", "bad-version")]
    [InlineData("{\"format\":\"aria-playlist\",\"version\":1,\"name\":\"  \",\"entries\":[{\"file\":\"a.flac\"}]}", "bad-name")]
    [InlineData("{\"format\":\"aria-playlist\",\"version\":1,\"name\":\"Шоу\",\"entries\":[]}", "bad-entries")]
    [InlineData("{\"format\":\"aria-playlist\",\"version\":1,\"name\":\"Шоу\",\"entries\":[{\"file\":\"  \"}]}", "bad-entry")]
    [InlineData("{\"format\":\"aria-playlist\",\"version\":1,\"name\":\"Шоу\",\"entries\":[{\"file\":\"a.flac\",\"gainDb\":99}]}", "bad-entry")]
    [InlineData("{\"format\":\"aria-playlist\",\"version\":1,\"name\":\"Шоу\",\"entries\":[{\"file\":\"a.flac\",\"end\":\"взрыв\"}]}", "bad-entry")]
    [InlineData("{\"format\":\"aria-playlist\",\"version\":1,\"name\":\"Шоу\",\"entries\":[{\"file\":\"a.flac\",\"transition\":{\"kind\":\"телепорт\"}}]}", "bad-entry")]
    public void Import_InvalidDocument_Throws(string json, string prefix)
    {
        var exception = Assert.Throws<PlaylistFormatException>(() => PlaylistFormat.Import(json));
        Assert.StartsWith(prefix, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_BrokenJson_Throws()
    {
        Assert.Throws<PlaylistFormatException>(() => PlaylistFormat.Import("не json"));
    }
}
