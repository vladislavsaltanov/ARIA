namespace Aria.Persistence;

using System.Collections.Immutable;
using System.Text.Json;
using Aria.Core.Model;

internal static class DtoJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General);

    public static string Serialize<T>(T value) where T : class =>
        JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) where T : class =>
        JsonSerializer.Deserialize<T>(json, Options);
}

internal sealed class MarkerDto
{
    public string Name { get; set; } = string.Empty;
    public long PositionTicks { get; set; }
    public int Action { get; set; }
}

internal sealed class TrackDto
{
    public Guid Id { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string DefaultName { get; set; } = string.Empty;
    public long DurationTicks { get; set; }
    public double GainDb { get; set; }
    public int EndAction { get; set; }
    public long? FadeInTicks { get; set; }
    public int? FadeInCurve { get; set; }
    public long? FadeOutTicks { get; set; }
    public int? FadeOutCurve { get; set; }
    public List<MarkerDto>? Markers { get; set; }
}

internal sealed class OverridesDto
{
    public string? Name { get; set; }
    public string? Color { get; set; }
    public string? Note { get; set; }
    public double? GainDb { get; set; }
    public int? EndAction { get; set; }
    public long? InTicks { get; set; }
    public int? InCurve { get; set; }
    public long? OutTicks { get; set; }
    public int? OutCurve { get; set; }
    public long? CueInTicks { get; set; }
    public long? CueOutTicks { get; set; }
}

internal sealed class EntryDto
{
    public Guid Id { get; set; }
    public Guid TrackId { get; set; }
    public OverridesDto? Overrides { get; set; }
}

internal sealed class PlaylistDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<EntryDto> Entries { get; set; } = [];
}

internal static class TrackMapper
{
    public static TrackDto ToDto(Track track)
    {
        var d = track.Defaults;
        return new TrackDto
        {
            Id = track.Id.Value,
            FilePath = track.FilePath,
            DefaultName = track.DefaultName,
            DurationTicks = track.Duration.Ticks,
            GainDb = d.GainDb,
            EndAction = (int)d.EndAction,
            FadeInTicks = d.In?.Duration.Ticks,
            FadeInCurve = d.In is null ? null : (int)d.In.Curve,
            FadeOutTicks = d.Out?.Duration.Ticks,
            FadeOutCurve = d.Out is null ? null : (int)d.Out.Curve,
            Markers = d.Markers is null
                ? null
                : [.. d.Markers.Value.Select(m => new MarkerDto { Name = m.Name, PositionTicks = m.Position.Ticks, Action = (int)m.Action })],
        };
    }

    public static Track ToDomain(TrackDto dto)
    {
        ImmutableArray<Marker>? markers = dto.Markers is null
            ? null
            : [.. dto.Markers.Select(m => new Marker(m.Name, new TimeSpan(m.PositionTicks), (MarkerAction)m.Action))];
        return new Track(
            new TrackId(dto.Id),
            dto.FilePath,
            dto.DefaultName,
            new TimeSpan(dto.DurationTicks),
            new TrackDefaults(
                GainDb: dto.GainDb,
                EndAction: (EndAction)dto.EndAction,
                In: dto.FadeInTicks is null ? null : new Fade(new TimeSpan(dto.FadeInTicks.Value), (FadeCurve)dto.FadeInCurve!.Value),
                Out: dto.FadeOutTicks is null ? null : new Fade(new TimeSpan(dto.FadeOutTicks.Value), (FadeCurve)dto.FadeOutCurve!.Value),
                Markers: markers));
    }
}

internal static class PlaylistMapper
{
    public static OverridesDto? ToDto(PlaylistOverrides? overrides) =>
        overrides is null
            ? null
            : new OverridesDto
            {
                Name = overrides.Name,
                Color = overrides.Color,
                Note = overrides.Note,
                GainDb = overrides.GainDb,
                EndAction = overrides.EndAction is null ? null : (int)overrides.EndAction,
                InTicks = overrides.In?.Duration.Ticks,
                InCurve = overrides.In is null ? null : (int)overrides.In.Curve,
                OutTicks = overrides.Out?.Duration.Ticks,
                OutCurve = overrides.Out is null ? null : (int)overrides.Out.Curve,
                CueInTicks = overrides.CueIn?.Ticks,
                CueOutTicks = overrides.CueOut?.Ticks,
            };

    public static PlaylistOverrides? ToDomain(OverridesDto? dto) =>
        dto is null
            ? null
            : new PlaylistOverrides(
                Name: dto.Name,
                Color: dto.Color,
                Note: dto.Note,
                GainDb: dto.GainDb,
                EndAction: dto.EndAction is null ? null : (EndAction)dto.EndAction,
                In: dto.InTicks is null ? null : new Fade(new TimeSpan(dto.InTicks.Value), (FadeCurve)dto.InCurve!.Value),
                Out: dto.OutTicks is null ? null : new Fade(new TimeSpan(dto.OutTicks.Value), (FadeCurve)dto.OutCurve!.Value),
                CueIn: dto.CueInTicks is null ? null : new TimeSpan(dto.CueInTicks.Value),
                CueOut: dto.CueOutTicks is null ? null : new TimeSpan(dto.CueOutTicks.Value));

    public static EntryDto ToDto(PlaylistEntry entry) => new()
    {
        Id = entry.Id.Value,
        TrackId = entry.TrackId.Value,
        Overrides = ToDto(entry.Overrides),
    };

    public static PlaylistEntry ToDomain(EntryDto dto) => new(
        new EntryId(dto.Id),
        new TrackId(dto.TrackId),
        ToDomain(dto.Overrides));

    public static PlaylistDto ToDto(Playlist playlist) => new()
    {
        Id = playlist.Id.Value,
        Name = playlist.Name,
        Entries = [.. playlist.Entries.Select(ToDto)],
    };

    public static Playlist ToDomain(PlaylistDto dto) => new(
        new PlaylistId(dto.Id),
        dto.Name,
        [.. dto.Entries.Select(ToDomain)]);
}

internal static class ScriptMapper
{
    public static ScriptDto ToDto(Script script) => new()
    {
        Id = script.Id.Value,
        Name = script.Name,
        Lines = [.. script.Lines.Select(line => new ScriptLineDto
        {
            Id = line.Id.Value,
            AtElapsedTicks = line.AtElapsed.Ticks,
            Text = line.Text,
            Mentions = [.. line.Mentions.Select(m => m.Track.Value)],
        })],
    };

    public static Script ToDomain(ScriptDto dto) => new(
        new ScriptId(dto.Id),
        dto.Name,
        [.. dto.Lines.Select(line => new ScriptLine(
            new ScriptLineId(line.Id),
            new TimeSpan(line.AtElapsedTicks),
            line.Text,
            [.. line.Mentions.Select(id => new Mention(new TrackId(id)))]))]);
}
