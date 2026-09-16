namespace Aria.Core.Model;

using System.Collections.Immutable;

public sealed record Mention(TrackId Track);

public sealed record ScriptLine(ScriptLineId Id, TimeSpan AtElapsed, string Text, ImmutableArray<Mention> Mentions);

public sealed record Script(ScriptId Id, string Name, ImmutableArray<ScriptLine> Lines, ProjectId? Project = null);

public sealed record TrackDigestEntry(TrackId Track, string DisplayName);

public sealed record TrackDigest(ImmutableArray<TrackDigestEntry> Entries)
{
    public static TrackDigest Empty { get; } = new([]);
}

public static class ScriptFollow
{
    public static ScriptLine? CurrentLine(Script script, TimeSpan elapsed)
    {
        ScriptLine? current = null;
        foreach (var line in script.Lines)
        {
            if (line.AtElapsed > elapsed)
            {
                break;
            }
            current = line;
        }
        return current;
    }
}
