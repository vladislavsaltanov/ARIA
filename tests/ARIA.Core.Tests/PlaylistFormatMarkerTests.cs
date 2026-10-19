namespace Aria.Core.Tests;

using System.Collections.Immutable;
using System.Text;
using Aria.Core.Model;

public sealed class ProjectFormatMarkerTests
{
    [Fact]
    public void ExportImport_RoundTrips_EntryMarkers()
    {
        var entries = ImmutableArray.Create(
            new ProjectExportEntry(
                "/audio/one.flac",
                new ProjectOverrides(Markers:
                [
                    new Marker("начало", TimeSpan.FromSeconds(12.5), MarkerAction.Pause),
                    new Marker("финал", TimeSpan.FromSeconds(90), MarkerAction.Pause),
                ])));

        var json = ProjectFormat.Export("Вечер", entries);
        var document = ProjectFormat.Import(json);

        var overrides = ProjectFormat.ToOverrides(document.Entries[0]);
        Assert.NotNull(overrides);
        var markers = overrides.Markers!.Value;
        Assert.Equal(2, markers.Length);
        Assert.Equal("начало", markers[0].Name);
        Assert.Equal(TimeSpan.FromSeconds(12.5), markers[0].Position);
        Assert.Equal(MarkerAction.Pause, markers[0].Action);
        Assert.Equal("финал", markers[1].Name);
        Assert.Equal(TimeSpan.FromSeconds(90), markers[1].Position);
    }

    [Fact]
    public void Import_LegacyStopAction_MapsToPause()
    {
        const string json = "{\"format\":\"aria-project\",\"version\":2,\"name\":\"Шоу\",\"entries\":[{\"file\":\"a.flac\",\"markers\":[{\"name\":\"привал\",\"position\":10,\"action\":\"stop\"}]}]}";
        var document = ProjectFormat.Import(json);
        var overrides = ProjectFormat.ToOverrides(document.Entries[0]);
        Assert.Equal(MarkerAction.Pause, Assert.Single(overrides!.Markers!.Value).Action);
    }

    [Fact]
    public void Import_EmptyMarkerName_ThrowsBadMarker()
    {
        const string json = "{\"format\":\"aria-project\",\"version\":2,\"name\":\"Шоу\",\"entries\":[{\"file\":\"a.flac\",\"markers\":[{\"name\":\"\",\"position\":10,\"action\":\"stop\"}]}]}";

        var exception = Assert.Throws<ProjectFormatException>(() => ProjectFormat.Import(json));

        Assert.StartsWith("bad-marker:", exception.Message, StringComparison.Ordinal);
        Assert.Contains("marker-name-empty", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_TooManyMarkers_ThrowsBadMarker()
    {
        var markers = new StringBuilder();
        for (var i = 0; i < 65; i++)
        {
            if (i > 0)
            {
                markers.Append(',');
            }
            markers.Append("{\"name\":\"метка ").Append(i).Append("\",\"position\":").Append(i).Append(",\"action\":\"stop\"}");
        }
        var json = $"{{\"format\":\"aria-project\",\"version\":2,\"name\":\"Шоу\",\"entries\":[{{\"file\":\"a.flac\",\"markers\":[{markers}]}}]}}";

        var exception = Assert.Throws<ProjectFormatException>(() => ProjectFormat.Import(json));

        Assert.StartsWith("bad-marker: marker-too-many", exception.Message, StringComparison.Ordinal);
    }
}
