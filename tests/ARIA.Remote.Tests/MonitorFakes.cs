namespace Aria.Remote.Tests;

using System.Diagnostics;
using Aria.Audio;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Remote.Monitor;

internal sealed class FakeStamps : IOutputStampSource
{
    private readonly long _startTicks = Stopwatch.GetTimestamp();

    public bool Available { get; set; } = true;

    public int DeviceBufferFrames => 0;

    public bool TryReadOutputStamp(out OutputStamp stamp)
    {
        var now = Stopwatch.GetTimestamp();
        var frame = (now - _startTicks) * 48000 / Stopwatch.Frequency / 512 * 512;
        stamp = new OutputStamp(frame, _startTicks + frame * Stopwatch.Frequency / 48000, 0, 0);
        return Available;
    }
}

internal sealed class NoSources : ISourceFactory
{
    public ISampleSource? Open(string filePath, TimeSpan cueIn, TimeSpan? cueOut) => null;
}

internal static class MonitorFeeds
{
    public static MonitorFeed Create(FakeStamps stamps, PlaybackMonitor bindings, List<Track> tracks, MixReportRing? ring = null) =>
        new(ring ?? new MixReportRing(), stamps, new NoSources(), new TrackListSources(() => [.. tracks]), bindings, () => 0.0);
}
