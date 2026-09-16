namespace Aria.Remote;

using System.Text.Json;
using System.Text.Json.Serialization;
using Aria.Core.Model;

internal sealed class TrackIdConverter : JsonConverter<TrackId>
{
    public override TrackId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, TrackId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

internal sealed class EntryIdConverter : JsonConverter<EntryId>
{
    public override EntryId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, EntryId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

internal sealed class ProjectIdConverter : JsonConverter<ProjectId>
{
    public override ProjectId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, ProjectId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

internal sealed class ScriptIdConverter : JsonConverter<ScriptId>
{
    public override ScriptId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, ScriptId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

internal sealed class ScriptLineIdConverter : JsonConverter<ScriptLineId>
{
    public override ScriptLineId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, ScriptLineId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
