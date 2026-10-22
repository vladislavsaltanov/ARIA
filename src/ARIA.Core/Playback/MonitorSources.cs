namespace Aria.Core.Playback;

using System.Collections.Immutable;
using Aria.Core.Model;

public sealed record MonitorSource(TrackId Track, string FilePath, double GainDb, BeatGrid? Grid);

public interface IMonitorSources
{
    MonitorSource? Find(TrackId track);
}

public sealed class TrackListSources(Func<ImmutableArray<Track>> tracks) : IMonitorSources
{
    public MonitorSource? Find(TrackId track)
    {
        foreach (var candidate in tracks())
        {
            if (candidate.Id == track)
            {
                var defaults = candidate.Defaults;
                return new MonitorSource(candidate.Id, candidate.FilePath, defaults.GainDb + (defaults.Audio?.GainDb ?? 0.0), defaults.Grid);
            }
        }
        return null;
    }
}
