namespace Aria.Remote;

using System.Collections.Immutable;
using System.Text.Json;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;

internal static class CommandCodec
{
    public static bool TryParse(string json, out string client, out long seq, out Command? command)
    {
        client = string.Empty;
        seq = 0;
        command = null;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return false;
        }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            if (!root.TryGetProperty("client", out var clientElement) || clientElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }
            client = clientElement.GetString()!;
            if (!root.TryGetProperty("seq", out var seqElement) || seqElement.ValueKind != JsonValueKind.Number || !seqElement.TryGetInt64(out seq))
            {
                return false;
            }
            if (!root.TryGetProperty("command", out var commandElement) || commandElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            if (!commandElement.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }
            command = typeElement.GetString() switch
            {
                "play" => new Play(),
                "pause" => new Pause(),
                "stop" => new Stop(),
                "next" => new Next(),
                "replay" => new Replay(),
                "panic" => new Panic(),
                "clear_queue" => new ClearQueue(),
                "jump_to" => new JumpTo(new EntryId(GuidOf(commandElement, "entry"))),
                "enqueue_entry" => new EnqueueEntry(new EntryId(GuidOf(commandElement, "entry"))),
                "enqueue_track" => new EnqueueTrack(new TrackId(GuidOf(commandElement, "track"))),
                "play_track" => new PlayTrack(new TrackId(GuidOf(commandElement, "track"))),
                "remove_from_queue" => new RemoveFromQueue(IntOf(commandElement, "index")),
                "create_playlist" or "create_project" => new CreateProject(StringOf(commandElement, "name")),
                "rename_playlist" or "rename_project" => new RenameProject(new ProjectId(GuidOf(commandElement, "id")), StringOf(commandElement, "name")),
                "delete_playlist" or "delete_project" => new DeleteProject(new ProjectId(GuidOf(commandElement, "id"))),
                "set_active_playlist" or "set_active_project" => new SetActiveProject(new ProjectId(GuidOf(commandElement, "id"))),
                "add_entry" => ParseAddEntry(commandElement),
                "remove_entry" => new RemoveEntry(new EntryId(GuidOf(commandElement, "entry"))),
                "move_entry" => new MoveEntry(new EntryId(GuidOf(commandElement, "entry")), IntOf(commandElement, "new_index")),
                "set_entry_overrides" => ParseSetEntryOverrides(commandElement),
                "move_queue_item" => new MoveQueueItem(IntOf(commandElement, "from"), IntOf(commandElement, "to")),
                "set_master_gain" => new SetMasterGain(DoubleOf(commandElement, "gain_db")),
                "set_muted" => new SetMuted(BoolOf(commandElement, "muted")),
                "set_global_audio" => ParseSetGlobalAudio(commandElement),
                "set_track_audio" => ParseSetTrackAudio(commandElement),
                "set_track_bpm" => new SetTrackBpm(new TrackId(GuidOf(commandElement, "track")), DoubleOrNullOf(commandElement, "bpm")),
                "set_entry_audio" => ParseSetEntryAudio(commandElement),
                "start_preview_track" => new StartPreviewTrack(new TrackId(GuidOf(commandElement, "track"))),
                "stop_preview" => new StopPreview(),
                "set_preview_gain" => new SetPreviewGain(DoubleOf(commandElement, "gain_db")),
                "set_preview_muted" => new SetPreviewMuted(BoolOf(commandElement, "muted")),
                "set_click_settings" => ParseSetClickSettings(commandElement),
                "set_click_muted" => new SetClickMuted(new PreviewSessionHandle(IntOf(commandElement, "session")), BoolOf(commandElement, "muted")),
                "start_session_track" => new StartSessionTrack(new PreviewSessionHandle(IntOf(commandElement, "session")), new TrackId(GuidOf(commandElement, "track"))),
                "rename_session" => new RenameSession(new PreviewSessionHandle(IntOf(commandElement, "session")), StringOf(commandElement, "name")),
                "set_session_backing_gain" => new SetSessionBackingGain(new PreviewSessionHandle(IntOf(commandElement, "session")), DoubleOf(commandElement, "gain_db")),
                "set_session_click_gain" => new SetSessionClickGain(new PreviewSessionHandle(IntOf(commandElement, "session")), DoubleOf(commandElement, "gain_db")),
                "close_session" => new CloseSession(new PreviewSessionHandle(IntOf(commandElement, "session"))),
                "normalize_track" => new NormalizeTrack(new TrackId(GuidOf(commandElement, "track"))),
                "normalize_playlist" or "normalize_project" => new NormalizeProject(new ProjectId(GuidOf(commandElement, "playlist"))),
                "seek_to" => new SeekTo(TimeSpan.FromMilliseconds(LongOf(commandElement, "position_ms"))),
                "set_panic_fade" => new SetPanicFade(TimeSpan.FromMilliseconds(IntOf(commandElement, "duration_ms"))),
                "set_default_end_action" => new SetDefaultEndAction(EndActionOf(commandElement, "end_action")),
                "create_script" => new CreateScript(StringOf(commandElement, "name")),
                "rename_script" => new RenameScript(new ScriptId(GuidOf(commandElement, "id")), StringOf(commandElement, "name")),
                "delete_script" => new DeleteScript(new ScriptId(GuidOf(commandElement, "id"))),
                "add_script_line" => new AddScriptLine(
                    new ScriptId(GuidOf(commandElement, "script")),
                    TimeSpan.FromMilliseconds(LongOf(commandElement, "at_ms")),
                    TextOf(commandElement),
                    MentionsOf(commandElement)),
                "update_script_line" => new UpdateScriptLine(
                    new ScriptId(GuidOf(commandElement, "script")),
                    new ScriptLineId(GuidOf(commandElement, "line")),
                    TimeSpan.FromMilliseconds(LongOf(commandElement, "at_ms")),
                    TextOf(commandElement),
                    MentionsOf(commandElement)),
                "remove_script_line" => new RemoveScriptLine(
                    new ScriptId(GuidOf(commandElement, "script")),
                    new ScriptLineId(GuidOf(commandElement, "line"))),
                _ => null,
            };
            return true;
        }
    }

    private static SetGlobalAudio ParseSetGlobalAudio(JsonElement element)
    {
        var value = new GlobalAudioSettings(
            DoubleOf(element, "pan"),
            BoolOf(element, "mono"),
            DoubleOf(element, "hpf_hz"),
            ParseAudioEq(element.GetProperty("eq")),
            ParseLimiter(element.GetProperty("limiter")),
            OptionalDouble(element, "normalize_target_lufs", -16.0),
            ParseMeterZones(element),
            OptionalBool(element, "normalize_enabled", false));
        if (AudioValidation.ValidateGlobal(value) is { } reason)
        {
            throw new FormatException(reason);
        }
        return new SetGlobalAudio(value);
    }

    private static SetTrackAudio ParseSetTrackAudio(JsonElement element) =>
        new(new TrackId(GuidOf(element, "track")), ParseTrackAudio(element));

    private static SetClickSettings ParseSetClickSettings(JsonElement element)
    {
        try
        {
            return new SetClickSettings(
                new PreviewSessionHandle(IntOf(element, "session")),
                new ClickSettings(
                    DoubleOf(element, "bpm"),
                    IntOf(element, "beats_per_bar"),
                    DoubleOf(element, "gain_db"),
                    DoubleOf(element, "offset_ms")));
        }
        catch (ArgumentOutOfRangeException e)
        {
            throw new FormatException(e.Message);
        }
    }

    private static SetEntryAudio ParseSetEntryAudio(JsonElement element)
    {
        TrackAudioSettings? audio = null;
        if (element.TryGetProperty("audio", out var audioElement)
            && audioElement.ValueKind == JsonValueKind.Object)
        {
            audio = ParseTrackAudio(audioElement);
        }
        return new SetEntryAudio(new EntryId(GuidOf(element, "entry")), audio);
    }

    private static TrackAudioSettings ParseTrackAudio(JsonElement element)
    {
        var value = new TrackAudioSettings(
            DoubleOf(element, "gain_db"),
            DoubleOf(element, "pan"),
            ParseAudioEq(element.GetProperty("eq")),
            OptionalBool(element, "normalize_enabled", false),
            OptionalNullableDouble(element, "measured_lufs"),
            OptionalNullableDouble(element, "normalize_target_lufs"));
        if (AudioValidation.ValidateTrack(value) is { } reason)
        {
            throw new FormatException(reason);
        }
        return value;
    }

    private static AudioEq ParseAudioEq(JsonElement element)
    {
        if (!element.TryGetProperty("bands", out var bandsElement)
            || bandsElement.ValueKind != JsonValueKind.Array
            || bandsElement.GetArrayLength() != AudioEq.DefaultFrequencies.Length)
        {
            throw new FormatException("eq-requires-7-bands");
        }
        var builder = ImmutableArray.CreateBuilder<EqBand>();
        foreach (var band in bandsElement.EnumerateArray())
        {
            builder.Add(new EqBand(
                (float)band.GetProperty("freq_hz").GetDouble(),
                (float)band.GetProperty("gain_db").GetDouble(),
                (float)band.GetProperty("q").GetDouble()));
        }
        return new AudioEq(builder.ToImmutable());
    }

    private static bool OptionalBool(JsonElement element, string name, bool fallback) =>
        element.TryGetProperty(name, out var flag) && flag.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? flag.GetBoolean()
            : fallback;

    private static double? OptionalNullableDouble(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number
            ? property.GetDouble()
            : null;

    private static double OptionalDouble(JsonElement element, string name, double fallback) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number
            ? property.GetDouble()
            : fallback;

    private static LufsMeterZones? ParseMeterZones(JsonElement element)
    {
        if (!element.TryGetProperty("meter_zones", out var zones) || zones.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        return new LufsMeterZones(
            OptionalDouble(zones, "green_db", -15.0),
            OptionalDouble(zones, "yellow_db", -9.0),
            OptionalDouble(zones, "red_db", -5.0));
    }

    private static LimiterSettings ParseLimiter(JsonElement element) =>
        new(
            element.GetProperty("enabled").GetBoolean(),
            element.GetProperty("threshold_db").GetDouble(),
            element.GetProperty("release_ms").GetDouble());

    private static AddEntry ParseAddEntry(JsonElement element)
    {
        int? index = null;
        if (element.TryGetProperty("index", out var indexElement) && indexElement.ValueKind == JsonValueKind.Number)
        {
            index = indexElement.GetInt32();
        }
        return new AddEntry(
            new ProjectId(ProjectGuidOf(element)),
            new TrackId(GuidOf(element, "track")),
            index);
    }

    private static Guid ProjectGuidOf(JsonElement element) =>
        element.TryGetProperty("project", out var project) && project.ValueKind == JsonValueKind.String
            ? Guid.Parse(project.GetString()!)
            : GuidOf(element, "playlist");

    private static SetEntryOverrides ParseSetEntryOverrides(JsonElement element)
    {
        ProjectOverrides? overrides = null;
        if (element.TryGetProperty("overrides", out var overridesElement) && overridesElement.ValueKind == JsonValueKind.Object)
        {
            overrides = ParseOverrides(overridesElement);
        }
        return new SetEntryOverrides(new EntryId(GuidOf(element, "entry")), overrides);
    }

    private static ProjectOverrides ParseOverrides(JsonElement element)
    {
        Fade? fadeIn = null;
        if (TicksOf(element, "in_ticks") is { } inTicks)
        {
            fadeIn = new Fade(new TimeSpan(inTicks), (FadeCurve)IntOf(element, "in_curve"));
        }
        Fade? fadeOut = null;
        if (TicksOf(element, "out_ticks") is { } outTicks)
        {
            fadeOut = new Fade(new TimeSpan(outTicks), (FadeCurve)IntOf(element, "out_curve"));
        }
        EndAction? endAction = null;
        if (element.TryGetProperty("end_action", out var endActionElement) && endActionElement.ValueKind == JsonValueKind.String)
        {
            endAction = Enum.TryParse<EndAction>(endActionElement.GetString(), ignoreCase: true, out var parsed) ? parsed : null;
        }
        return new ProjectOverrides(
            Name: StringOrNullOf(element, "name"),
            Color: StringOrNullOf(element, "color"),
            Note: StringOrNullOf(element, "note"),
            GainDb: DoubleOrNullOf(element, "gain_db"),
            EndAction: endAction,
            In: fadeIn,
            Out: fadeOut,
            CueIn: TicksOf(element, "cue_in_ticks") is { } cueIn ? new TimeSpan(cueIn) : null,
            CueOut: TicksOf(element, "cue_out_ticks") is { } cueOut ? new TimeSpan(cueOut) : null);
    }

    private static Guid GuidOf(JsonElement element, string name) =>
        Guid.Parse(element.GetProperty(name).GetString()!);

    private static EndAction EndActionOf(JsonElement element, string name)
    {
        var text = element.GetProperty(name).GetString();
        if (Enum.TryParse<EndAction>(text, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }
        throw new FormatException($"Unknown end_action: {text}");
    }

    private static string StringOf(JsonElement element, string name) =>
        element.GetProperty(name).GetString()!;

    private static int IntOf(JsonElement element, string name) =>
        element.GetProperty(name).GetInt32();

    private static long LongOf(JsonElement element, string name) =>
        element.GetProperty(name).GetInt64();

    private static string TextOf(JsonElement element) =>
        element.TryGetProperty("text", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static ImmutableArray<TrackId> MentionsOf(JsonElement element)
    {
        if (!element.TryGetProperty("mentions", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        var builder = ImmutableArray.CreateBuilder<TrackId>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && Guid.TryParse(item.GetString(), out var id))
            {
                builder.Add(new TrackId(id));
            }
        }
        return builder.ToImmutable();
    }

    private static bool BoolOf(JsonElement element, string name) =>
        element.GetProperty(name).GetBoolean();
    private static double DoubleOf(JsonElement element, string name) =>
        element.GetProperty(name).GetDouble();

    private static string? StringOrNullOf(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double? DoubleOrNullOf(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    private static long? TicksOf(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : null;
}
