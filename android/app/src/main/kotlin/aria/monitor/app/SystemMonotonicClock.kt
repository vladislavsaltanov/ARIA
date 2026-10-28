package aria.monitor.app

import aria.monitor.core.MonotonicClock

private const val NANOS_PER_MILLI = 1_000_000.0

internal class SystemMonotonicClock(private val nanoTime: () -> Long = System::nanoTime) : MonotonicClock {
    override fun nowMs(): Double = nanoTime() / NANOS_PER_MILLI
}
