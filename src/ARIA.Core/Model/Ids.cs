namespace Aria.Core.Model;

public readonly record struct TrackId(Guid Value)
{
    public static TrackId New() => new(Guid.NewGuid());
}

public readonly record struct EntryId(Guid Value)
{
    public static EntryId New() => new(Guid.NewGuid());
}

public readonly record struct PlaylistId(Guid Value)
{
    public static PlaylistId New() => new(Guid.NewGuid());
}
