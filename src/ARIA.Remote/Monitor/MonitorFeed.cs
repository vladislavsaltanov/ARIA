namespace Aria.Remote.Monitor;

using Aria.Audio;
using Aria.Core.Playback;

public sealed record MonitorFeed(
    MixReportRing Reports,
    IOutputStampSource Stamps,
    ISourceFactory Decoders,
    IMonitorSources Sources,
    PlaybackMonitor Bindings,
    Func<double> HallTrimMs);
