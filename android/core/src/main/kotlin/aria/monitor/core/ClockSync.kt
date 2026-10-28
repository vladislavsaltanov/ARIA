package aria.monitor.core

import kotlin.math.min

private const val WINDOW_SAMPLES = 64
private const val WINDOW_MS = 30000.0
private const val REGRESSION_SPAN_MS = 5000.0
private const val GOOD_MARGIN_MS = 1.0
private const val LOCK_SAMPLES = 8
private const val LOCK_MAX_DELAY_MS = 40.0

interface MonotonicClock {
    fun nowMs(): Double
}

private data class ClockSample(val delay: Double, val offset: Double, val local: Double)

private data class ClockFit(
    val meanLocal: Double,
    val meanOffset: Double,
    val slope: Double,
    val minDelay: Double,
    val goodCount: Int,
    val recentCount: Int,
) {
    companion object {
        val Empty = ClockFit(0.0, 0.0, 0.0, Double.POSITIVE_INFINITY, 0, 0)
    }
}

class ClockSync {
    private val samples = ArrayList<ClockSample>(WINDOW_SAMPLES + 1)

    @Volatile
    private var fit = ClockFit.Empty

    fun onPong(t0Ms: Double, t1Us: Long, t2Us: Long, t3Ms: Double) {
        val t1 = t1Us.toDouble() / 1000
        val t2 = t2Us.toDouble() / 1000
        val delay = t3Ms - t0Ms - (t2 - t1)
        val offset = (t1 - t0Ms + (t2 - t3Ms)) / 2
        samples.add(ClockSample(delay, offset, (t0Ms + t3Ms) / 2))
        if (samples.size > WINDOW_SAMPLES) samples.removeAt(0)
        refit(t3Ms)
    }

    private fun refit(now: Double) {
        val recent = samples.filter { now - it.local <= WINDOW_MS }
        if (recent.isEmpty()) return
        val minDelay = recent.fold(Double.POSITIVE_INFINITY) { lowest, sample -> min(lowest, sample.delay) }
        val good = recent.filter { it.delay <= minDelay + GOOD_MARGIN_MS }
        val count = good.size
        val meanLocal = good.fold(0.0) { sum, sample -> sum + sample.local } / count
        val meanOffset = good.fold(0.0) { sum, sample -> sum + sample.offset } / count
        var slope = 0.0
        if (count >= 2 && good[count - 1].local - good[0].local >= REGRESSION_SPAN_MS) {
            var covariance = 0.0
            var variance = 0.0
            for (sample in good) {
                val dx = sample.local - meanLocal
                covariance += dx * (sample.offset - meanOffset)
                variance += dx * dx
            }
            slope = if (variance > 0.0) covariance / variance else 0.0
        }
        fit = ClockFit(meanLocal, meanOffset, slope, minDelay, count, recent.size)
    }

    fun offsetAt(localMs: Double): Double {
        val current = fit
        return current.meanOffset + current.slope * (localMs - current.meanLocal)
    }

    val ready: Boolean
        get() = fit.goodCount > 0

    val locked: Boolean
        get() {
            val current = fit
            return current.recentCount >= LOCK_SAMPLES && current.minDelay < LOCK_MAX_DELAY_MS
        }

    val minDelayMs: Double?
        get() = fit.minDelay.takeIf { it.isFinite() }

    val uncertaintyUs: Long?
        get() = minDelayMs?.let { Math.round((it / 2) * 1000) }

    fun hostUsAt(localMs: Double): Double = (localMs + offsetAt(localMs)) * 1000

    fun localMsAt(hostUs: Double): Double {
        val host = hostUs / 1000
        var local = host - offsetAt(host)
        local = host - offsetAt(local)
        return local
    }
}
