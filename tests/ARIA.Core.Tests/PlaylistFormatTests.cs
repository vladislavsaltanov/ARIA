namespace Aria.Core.Tests;

using System.Collections.Immutable;
using Aria.Core.Model;

public sealed class ProjectFormatTests
{
    [Fact]
    public void ExportImport_RoundTrips_NameOverridesAndTransition()
    {
        var entries = ImmutableArray.Create(
            new ProjectExportEntry(
                "/audio/one.flac",
                new ProjectOverrides("Утро", "#FF0000", "опенер", 1.5, EndAction.Advance,
                    new Fade(TimeSpan.FromSeconds(2), FadeCurve.Exponential),
                    new Fade(TimeSpan.FromSeconds(5), FadeCurve.SCurve),
                    TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(170)),
                new ProjectFileTransition("crossfade", 4)),
            new ProjectExportEntry("/audio/two.flac"));

        var json = ProjectFormat.Export("Вечер", entries);
        var document = ProjectFormat.Import(json);

        Assert.Equal("aria-project", document.Format);
        Assert.Equal(2, document.Version);
        Assert.Empty(document.Scripts);
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
        Assert.Null(ProjectFormat.ToOverrides(second));
    }

    [Fact]
    public void Export_WritesV2()
    {
        var json = ProjectFormat.Export("Вечер", [new ProjectExportEntry("/audio/one.flac")]);
        var document = ProjectFormat.Import(json);

        Assert.Equal("aria-project", document.Format);
        Assert.Equal(2, document.Version);
        Assert.Equal("Вечер", document.Name);
        Assert.Equal("/audio/one.flac", Assert.Single(document.Entries).File);
        Assert.Empty(document.Scripts);
    }

    [Fact]
    public void Import_LegacyV1_MigratesEntriesWithoutLoss()
    {
        var json = """
            {
                "format": "aria-playlist", "version": 1, "name": "Вечер",
                "entries": [
                    {
                        "file": "/audio/one.flac", "name": "Утро", "color": "#FF0000",
                        "note": "опенер", "gainDb": 1.5, "end": "advance",
                        "in": { "seconds": 2, "curve": "exponential" },
                        "out": { "seconds": 5, "curve": "s" },
                        "cueIn": 1, "cueOut": 170,
                        "transition": { "kind": "crossfade", "seconds": 4 }
                    },
                    { "file": "/audio/two.flac" }
                ]
            }
            """;
        var document = ProjectFormat.Import(json);

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
        Assert.Equal("/audio/two.flac", document.Entries[1].File);
        Assert.Empty(document.Scripts);
    }

    [Fact]
    public void ExportImport_RoundTrips_Scripts()
    {
        var scripts = new[]
        {
            new ProjectExportScript("Утро", new[]
            {
                new ProjectExportScriptLine("1:05", "открывашка", ["/audio/one.flac"]),
                new ProjectExportScriptLine("2:00", null, null),
            }),
            new ProjectExportScript("Финал", []),
        };
        var json = ProjectFormat.Export("Вечер", [new ProjectExportEntry("/audio/one.flac")], scripts);
        var document = ProjectFormat.Import(json);

        Assert.Equal("aria-project", document.Format);
        Assert.Equal(2, document.Version);
        Assert.Equal(2, document.Scripts.Length);
        Assert.Equal("Утро", document.Scripts[0].Name);
        Assert.Equal(2, document.Scripts[0].Lines?.Length);
        Assert.Equal("1:05", document.Scripts[0].Lines?[0].At);
        Assert.Equal("открывашка", document.Scripts[0].Lines?[0].Text);
        Assert.Equal("/audio/one.flac", Assert.Single(document.Scripts[0].Lines?[0].Tracks!));
        Assert.Equal("2:00", document.Scripts[0].Lines?[1].At);
        Assert.Equal("Финал", document.Scripts[1].Name);
        Assert.Empty(document.Scripts[1].Lines ?? []);
    }

    [Theory]
    [InlineData("{\"format\":\"aria-project\",\"version\":2,\"name\":\"Шоу\",\"entries\":[{\"file\":\"a.flac\"}],\"scripts\":[{\"lines\":[]}]}}", "bad-script")]
    [InlineData("{\"format\":\"aria-project\",\"version\":2,\"name\":\"Шоу\",\"entries\":[{\"file\":\"a.flac\"}],\"scripts\":[{\"name\":\"  \",\"lines\":[]}]}}", "bad-script")]
    [InlineData("{\"format\":\"aria-project\",\"version\":2,\"name\":\"Шоу\",\"entries\":[{\"file\":\"a.flac\"}],\"scripts\":[{\"name\":\"Утро\"}]}}", "bad-script")]
    [InlineData("{\"format\":\"aria-project\",\"version\":1,\"name\":\"Шоу\",\"entries\":[{\"file\":\"a.flac\"}]}", "bad-version")]
    public void Import_V2_InvalidDocument_Throws(string json, string prefix)
    {
        var exception = Assert.Throws<ProjectFormatException>(() => ProjectFormat.Import(json));
        Assert.StartsWith(prefix, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToOverrides_MapsAllFields()
    {
        var document = ProjectFormat.Import(ProjectFormat.Export("Шоу", ImmutableArray.Create(
            new ProjectExportEntry("/audio/one.flac",
                new ProjectOverrides(GainDb: -3, EndAction: EndAction.Replay)))));

        var overrides = ProjectFormat.ToOverrides(document.Entries[0]);
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
        var exception = Assert.Throws<ProjectFormatException>(() => ProjectFormat.Import(json));
        Assert.StartsWith(prefix, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_BrokenJson_Throws()
    {
        Assert.Throws<ProjectFormatException>(() => ProjectFormat.Import("не json"));
    }
}
