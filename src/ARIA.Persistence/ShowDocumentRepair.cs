namespace Aria.Persistence;

using System.Collections.Immutable;
using Aria.Core.Model;
using Aria.Core.State;

public static class ShowDocumentRepair
{
    private const double MasterGainMinDb = -80.0;
    private const double MasterGainMaxDb = 12.0;
    private static readonly TimeSpan PanicFadeMax = TimeSpan.FromMilliseconds(2000);

    public static ShowDocument Repair(ShowDocument document)
    {
        var tracks = DistinctTracks(document.Tracks.IsDefault ? [] : document.Tracks);
        var trackIds = tracks.Select(t => t.Id).ToHashSet();
        var entryIds = new HashSet<EntryId>();
        var projects = (document.Projects.IsDefault ? [] : document.Projects)
            .Select(p => RepairProject(p, trackIds, entryIds))
            .ToImmutableArray();
        var queue = (document.Queue.IsDefault ? [] : document.Queue)
            .Where(q => trackIds.Contains(q.TrackId) && (q.EntryId is not { } entry || entryIds.Contains(entry)))
            .ToImmutableArray();
        var active = document.ActiveId is { } id && projects.Any(p => p.Id == id) ? document.ActiveId : null;
        var gain = double.IsFinite(document.MasterGainDb) ? Math.Clamp(document.MasterGainDb, MasterGainMinDb, MasterGainMaxDb) : 0.0;
        var panicFade = document.PanicFade < TimeSpan.Zero ? TimeSpan.Zero : document.PanicFade > PanicFadeMax ? PanicFadeMax : document.PanicFade;
        var clock = document.ClockElapsed < TimeSpan.Zero ? TimeSpan.Zero : document.ClockElapsed;
        var scripts = document.Scripts.IsDefault ? [] : document.Scripts;

        var unchanged = tracks.SequenceEqual(document.Tracks.IsDefault ? [] : document.Tracks)
            && !document.Projects.IsDefault && projects.SequenceEqual(document.Projects, ProjectEntriesComparer.Instance)
            && !document.Queue.IsDefault && queue.Length == document.Queue.Length
            && active == document.ActiveId
            && gain.Equals(document.MasterGainDb)
            && panicFade == document.PanicFade
            && clock == document.ClockElapsed
            && !document.Scripts.IsDefault;
        if (unchanged)
        {
            return document;
        }
        return document with
        {
            Tracks = tracks,
            Projects = projects,
            ActiveId = active,
            Queue = queue,
            MasterGainDb = gain,
            PanicFade = panicFade,
            ClockElapsed = clock,
            Scripts = scripts,
        };
    }

    private static ImmutableArray<Track> DistinctTracks(ImmutableArray<Track> tracks)
    {
        var seen = new HashSet<TrackId>();
        return [.. tracks.Where(t => t is not null && seen.Add(t.Id))];
    }

    private static Project RepairProject(Project project, HashSet<TrackId> trackIds, HashSet<EntryId> entryIds)
    {
        var entries = project.Entries.IsDefault ? [] : project.Entries;
        var kept = entries.Where(e => trackIds.Contains(e.TrackId) && entryIds.Add(e.Id)).ToImmutableArray();
        return kept.Length == entries.Length && !project.Entries.IsDefault ? project : project with { Entries = kept };
    }

    private sealed class ProjectEntriesComparer : IEqualityComparer<Project>
    {
        public static readonly ProjectEntriesComparer Instance = new();

        public bool Equals(Project? x, Project? y) => ReferenceEquals(x, y);

        public int GetHashCode(Project obj) => obj.GetHashCode();
    }
}
