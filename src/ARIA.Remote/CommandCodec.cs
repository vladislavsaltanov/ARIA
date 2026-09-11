namespace Aria.Remote;

using System.Text.Json;
using Aria.Core.Commands;
using Aria.Core.Model;

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
                "remove_from_queue" => new RemoveFromQueue(IntOf(commandElement, "index")),
                "create_playlist" => new CreatePlaylist(StringOf(commandElement, "name")),
                "rename_playlist" => new RenamePlaylist(new PlaylistId(GuidOf(commandElement, "id")), StringOf(commandElement, "name")),
                "delete_playlist" => new DeletePlaylist(new PlaylistId(GuidOf(commandElement, "id"))),
                "set_active_playlist" => new SetActivePlaylist(new PlaylistId(GuidOf(commandElement, "id"))),
                "add_entry" => ParseAddEntry(commandElement),
                "remove_entry" => new RemoveEntry(new EntryId(GuidOf(commandElement, "entry"))),
                "move_entry" => new MoveEntry(new EntryId(GuidOf(commandElement, "entry")), IntOf(commandElement, "new_index")),
                "set_entry_overrides" => ParseSetEntryOverrides(commandElement),
                "move_queue_item" => new MoveQueueItem(IntOf(commandElement, "from"), IntOf(commandElement, "to")),
                "set_master_gain" => new SetMasterGain(DoubleOf(commandElement, "gain_db")),
                "set_muted" => new SetMuted(BoolOf(commandElement, "muted")),
                "set_panic_fade" => new SetPanicFade(TimeSpan.FromMilliseconds(IntOf(commandElement, "duration_ms"))),
                _ => null,
            };
            return true;
        }
    }

    private static AddEntry ParseAddEntry(JsonElement element)
    {
        int? index = null;
        if (element.TryGetProperty("index", out var indexElement) && indexElement.ValueKind == JsonValueKind.Number)
        {
            index = indexElement.GetInt32();
        }
        return new AddEntry(
            new PlaylistId(GuidOf(element, "playlist")),
            new TrackId(GuidOf(element, "track")),
            index);
    }

    private static SetEntryOverrides ParseSetEntryOverrides(JsonElement element)
    {
        PlaylistOverrides? overrides = null;
        if (element.TryGetProperty("overrides", out var overridesElement) && overridesElement.ValueKind == JsonValueKind.Object)
        {
            overrides = ParseOverrides(overridesElement);
        }
        return new SetEntryOverrides(new EntryId(GuidOf(element, "entry")), overrides);
    }

    private static PlaylistOverrides ParseOverrides(JsonElement element)
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
        return new PlaylistOverrides(
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

    private static string StringOf(JsonElement element, string name) =>
        element.GetProperty(name).GetString()!;

    private static int IntOf(JsonElement element, string name) =>
        element.GetProperty(name).GetInt32();

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
