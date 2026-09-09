namespace Aria.Core.Model;

using System.Collections.Immutable;

public readonly record struct PeakPoint(float Min, float Max);

public sealed record WaveformPeaks(
    TrackId TrackId,
    int PointsPerSecond,
    int SampleRate,
    ImmutableArray<PeakPoint> Points);