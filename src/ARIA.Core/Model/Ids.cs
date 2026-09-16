namespace Aria.Core.Model;

public readonly record struct TrackId(Guid Value)
{
    public static TrackId New() => new(Guid.NewGuid());
}

public readonly record struct EntryId(Guid Value)
{
    public static EntryId New() => new(Guid.NewGuid());
}

public readonly record struct ProjectId(Guid Value)
{
    public static ProjectId New() => new(Guid.NewGuid());
}

public readonly record struct ScriptId(Guid Value)
{
    public static ScriptId New() => new(Guid.NewGuid());
}

public readonly record struct ScriptLineId(Guid Value)
{
    public static ScriptLineId New() => new(Guid.NewGuid());
}
