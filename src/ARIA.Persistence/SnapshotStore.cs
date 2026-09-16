namespace Aria.Persistence;

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aria.Core.Model;
using Aria.Core.State;

public sealed record ShowDocument(
    ImmutableArray<Track> Tracks,
    ImmutableArray<Project> Projects,
    ProjectId? ActiveId,
    ImmutableArray<QueueItem> Queue,
    double MasterGainDb,
    TimeSpan PanicFade,
    TimeSpan ClockElapsed,
    bool ClockRunning,
    ImmutableArray<Script> Scripts,
    DateTimeOffset SavedAt,
    GlobalAudioSettings? Global = null)
{
    public GlobalAudioSettings EffectiveGlobal => Global ?? GlobalAudioSettings.Default;
}

public interface ISnapshotStore : IDisposable
{
    void Save(ShowDocument document);

    ShowDocument? LoadLatest();
}

public sealed class JsonSnapshotStore : ISnapshotStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General);

    private readonly string _path;

    public JsonSnapshotStore(string path) => _path = path;

    public void Save(ShowDocument document)
    {
        var dto = ToDto(document);
        var json = JsonSerializer.Serialize(dto, Options);
        // tmp+move: crash never leaves half-written snapshot.
        var tempPath = _path + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _path, overwrite: true);
    }

    public ShowDocument? LoadLatest()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }
            var json = File.ReadAllText(_path);
            var dto = JsonSerializer.Deserialize<ShowDocumentDto>(json, Options);
            return dto?.ToDomain();
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return null;
        }
    }

    public void Dispose()
    {
    }

    private static ShowDocumentDto ToDto(ShowDocument document) => new()
    {
        Tracks = [.. document.Tracks.Select(TrackMapper.ToDto)],
        Projects = [.. document.Projects.Select(ProjectMapper.ToDto)],
        ActiveId = document.ActiveId?.Value,
        Queue = [.. document.Queue.Select(q => new QueueItemDto
        {
            EntryId = q.EntryId?.Value,
            TrackId = q.TrackId.Value,
            DisplayName = q.DisplayName,
            Color = q.Color,
        })],
        MasterGainDb = document.MasterGainDb,
        Global = AudioMapper.ToDto(document.EffectiveGlobal),
        PanicFadeTicks = document.PanicFade.Ticks,
        ClockElapsedTicks = document.ClockElapsed.Ticks,
        ClockRunning = document.ClockRunning,
        Scripts = [.. document.Scripts.Select(ScriptMapper.ToDto)],
        SavedAt = document.SavedAt,
    };
}

internal sealed class ScriptLineDto
{
    public Guid Id { get; set; }
    public long AtElapsedTicks { get; set; }
    public string Text { get; set; } = string.Empty;
    public List<Guid> Mentions { get; set; } = [];
}

internal sealed class ScriptDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<ScriptLineDto> Lines { get; set; } = [];
}

internal sealed class ShowDocumentDto
{
    public List<TrackDto> Tracks { get; set; } = [];
    public List<ProjectDto> Projects { get; set; } = [];
    [JsonPropertyName("Playlists")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ProjectDto>? LegacyPlaylists { get; set; }
    public Guid? ActiveId { get; set; }
    public List<QueueItemDto> Queue { get; set; } = [];
    public double MasterGainDb { get; set; }
    public long PanicFadeTicks { get; set; }
    public long ClockElapsedTicks { get; set; }
    public bool ClockRunning { get; set; }
    public List<ScriptDto>? Scripts { get; set; }
    public GlobalAudioDto? Global { get; set; }
    public DateTimeOffset SavedAt { get; set; }

    public ShowDocument ToDomain() => new(
        [.. Tracks.Select(TrackMapper.ToDomain)],
        [.. (Projects.Count > 0 ? Projects : (LegacyPlaylists ?? [])).Select(ProjectMapper.ToDomain)],
        ActiveId is null ? null : new ProjectId(ActiveId.Value),
        [.. Queue.Select(q => new QueueItem(
            q.EntryId is null ? null : new EntryId(q.EntryId.Value),
            new TrackId(q.TrackId),
            q.DisplayName,
            q.Color))],
        MasterGainDb,
        new TimeSpan(PanicFadeTicks),
        new TimeSpan(ClockElapsedTicks),
        ClockRunning,
        Scripts is null ? [] : [.. Scripts.Select(ScriptMapper.ToDomain)],
        SavedAt,
        AudioMapper.ToDomain(Global));
}

internal sealed class QueueItemDto
{
    public Guid? EntryId { get; set; }
    public Guid TrackId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Color { get; set; }
}
