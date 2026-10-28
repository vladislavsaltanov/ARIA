package aria.monitor.app

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

class SystemMonotonicClockTest {
    @Test
    fun nanosecondsConvertToFractionalMilliseconds() {
        assertEquals(1.5, SystemMonotonicClock { 1_500_000L }.nowMs())
        assertEquals(0.000001, SystemMonotonicClock { 1L }.nowMs())
        assertEquals(2_000_000_000.25, SystemMonotonicClock { 2_000_000_000_250_000L }.nowMs())
    }

    @Test
    fun defaultClockReadsSystemNanoTime() {
        val before = System.nanoTime() / 1_000_000.0
        val now = SystemMonotonicClock().nowMs()
        val after = System.nanoTime() / 1_000_000.0

        assertTrue(now in before..after)
    }

    @Test
    fun timeNeverGoesBackwardsAndAdvances() {
        val clock = SystemMonotonicClock()
        val start = clock.nowMs()
        var previous = start

        repeat(10_000) {
            val now = clock.nowMs()
            assertTrue(now >= previous)
            previous = now
        }
        Thread.sleep(5)

        assertTrue(clock.nowMs() - start >= 4.0)
    }
}
