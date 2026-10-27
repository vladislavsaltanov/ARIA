package aria.monitor.core

import kotlin.math.roundToLong

private const val WINDOW_STAMPS = 32
private const val NANOS_PER_MILLI = 1e6
private const val CLOCK_RATE_PROBE_MS = 10_000.0

class TimeMapEstimator {
    private val stamps = ArrayDeque<OutputStamp>(WINDOW_STAMPS + 1)

    @Synchronized
    fun add(stamp: OutputStamp) {
        if (stamps.isNotEmpty() && stamp.framePosition < stamps.last().framePosition) stamps.clear()
        stamps.addLast(stamp)
        if (stamps.size > WINDOW_STAMPS) stamps.removeFirst()
    }

    @Synchronized
    fun reset() {
        stamps.clear()
    }

    @Synchronized
    fun timeMap(clock: ClockSync): TimeMap? {
        if (stamps.size < 2 || !clock.ready) return null
        val oldest = stamps.first()
        val newest = stamps.last()
        val frames = DoubleArray(stamps.size) { (stamps[it].framePosition - oldest.framePosition).toDouble() }
        val nanos = DoubleArray(stamps.size) { (stamps[it].monotonicNs - oldest.monotonicNs).toDouble() }
        val meanFrames = frames.average()
        val meanNanos = nanos.average()
        var covariance = 0.0
        var variance = 0.0
        for (index in frames.indices) {
            val frameDeviation = frames[index] - meanFrames
            covariance += frameDeviation * (nanos[index] - meanNanos)
            variance += frameDeviation * frameDeviation
        }
        if (variance == 0.0) return null
        val nanosPerFrame = covariance / variance
        val nanosAtNewest = meanNanos + nanosPerFrame * (frames.last() - meanFrames)
        val localMsAtNewest = oldest.monotonicNs / NANOS_PER_MILLI + nanosAtNewest / NANOS_PER_MILLI
        val probeEndMs = localMsAtNewest + CLOCK_RATE_PROBE_MS
        val hostUsAtNewest = clock.hostUsAt(localMsAtNewest)
        val hostUsPerLocalMs = (clock.hostUsAt(probeEndMs) - hostUsAtNewest) / (probeEndMs - localMsAtNewest)
        val usPerFrame = hostUsPerLocalMs * nanosPerFrame / NANOS_PER_MILLI
        if (!usPerFrame.isFinite() || usPerFrame <= 0.0) return null
        return TimeMap(newest.framePosition, hostUsAtNewest.roundToLong(), usPerFrame)
    }
}
