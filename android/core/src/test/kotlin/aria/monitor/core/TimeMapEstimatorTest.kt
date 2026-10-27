package aria.monitor.core

import kotlin.math.abs
import kotlin.math.roundToLong
import kotlin.random.Random
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

private const val FRAMES_PER_STAMP = 12_000L
private const val WINDOW = 32
private const val TRUE_US_PER_FRAME = 1e6 / (48_000.0 * 1.00002)
private const val NOMINAL_US_PER_FRAME = 1e6 / 48_000.0
private const val HOST_OFFSET_MS = 13_000_000_000.0 - 2_500_000_000.0
private const val FIRST_NS = 2_500_000_000_000_000L
private const val FIRST_FRAME = 1_000_000L
private const val PONG_SPACING_MS = 500.0
private const val CLEAN_TOLERANCE_US_PER_FRAME = 5e-8
private const val JITTER_US = 20.0
private const val SUM_ABS_DEVIATIONS = 256.0
private const val SUM_SQUARED_DEVIATIONS = WINDOW * (WINDOW * WINDOW - 1) / 12.0

private fun stampSeries(
    count: Int,
    firstFrame: Long = FIRST_FRAME,
    firstNs: Long = FIRST_NS,
    usPerFrame: Double = TRUE_US_PER_FRAME,
    jitterUs: (Int) -> Double = { 0.0 },
): List<OutputStamp> =
    List(count) { index ->
        val elapsedUs = index.toLong() * FRAMES_PER_STAMP * usPerFrame + jitterUs(index)
        OutputStamp(firstFrame + index.toLong() * FRAMES_PER_STAMP, firstNs + (elapsedUs * 1000).roundToLong())
    }

private fun syncedClock(startLocalMs: Double, hostOffsetMs: Double, driftPerMs: Double = 0.0): ClockSync {
    val clock = ClockSync()
    for (step in -4..16) {
        val local = startLocalMs + step * PONG_SPACING_MS
        val hostUs = ((local + hostOffsetMs + driftPerMs * (local - startLocalMs)) * 1000).roundToLong()
        clock.onPong(local, hostUs, hostUs, local)
    }
    return clock
}

private fun clockFor(stamps: List<OutputStamp>, driftPerMs: Double = 0.0): ClockSync =
    syncedClock(stamps.first().monotonicNs / 1e6, HOST_OFFSET_MS, driftPerMs)

private fun estimatorWith(stamps: List<OutputStamp>): TimeMapEstimator =
    TimeMapEstimator().also { estimator -> stamps.forEach(estimator::add) }

private fun TimeMap.hostUsAtFrame(frame: Long): Double = hostUsAtBaseFrame + (frame - baseFrame) * usPerFrame

class TimeMapEstimatorTest {
    @Test
    fun needsTwoStampsBeforeMap() {
        val stamps = stampSeries(3)
        val clock = clockFor(stamps)
        val estimator = TimeMapEstimator()

        assertNull(estimator.timeMap(clock))
        estimator.add(stamps[0])
        assertNull(estimator.timeMap(clock))
        estimator.add(stamps[1])
        assertNotNull(estimator.timeMap(clock))
    }

    @Test
    fun recoversSlopeOf1_00002WithinTolerance() {
        val stamps = stampSeries(WINDOW)

        val map = assertNotNull(estimatorWith(stamps).timeMap(clockFor(stamps)))

        assertEquals(TRUE_US_PER_FRAME, map.usPerFrame, CLEAN_TOLERANCE_US_PER_FRAME)
        assertTrue(abs(map.usPerFrame - NOMINAL_US_PER_FRAME) > 100 * CLEAN_TOLERANCE_US_PER_FRAME)
    }

    @Test
    fun recoversSlopeUnderBoundedJitterWithinTheWorstCaseOfLeastSquares() {
        val random = Random(20_000)
        val stamps = stampSeries(WINDOW, jitterUs = { random.nextDouble(-JITTER_US, JITTER_US) })
        val worstCase = JITTER_US * SUM_ABS_DEVIATIONS / (SUM_SQUARED_DEVIATIONS * FRAMES_PER_STAMP)

        val map = assertNotNull(estimatorWith(stamps).timeMap(clockFor(stamps)))

        assertEquals(TRUE_US_PER_FRAME, map.usPerFrame, worstCase + CLEAN_TOLERANCE_US_PER_FRAME)
        assertTrue(abs(map.usPerFrame - NOMINAL_US_PER_FRAME) > worstCase)
    }

    @Test
    fun jitterWorstCaseHoldsForAnAdversarialPattern() {
        val stamps = stampSeries(WINDOW, jitterUs = { index -> if (index < WINDOW / 2) JITTER_US else -JITTER_US })
        val worstCase = JITTER_US * SUM_ABS_DEVIATIONS / (SUM_SQUARED_DEVIATIONS * FRAMES_PER_STAMP)

        val map = assertNotNull(estimatorWith(stamps).timeMap(clockFor(stamps)))

        assertEquals(TRUE_US_PER_FRAME - worstCase, map.usPerFrame, CLEAN_TOLERANCE_US_PER_FRAME)
    }

    @Test
    fun resetDropsHistory() {
        val oldStamps = stampSeries(WINDOW)
        val clock = clockFor(oldStamps)
        val estimator = estimatorWith(oldStamps)
        assertNotNull(estimator.timeMap(clock))

        estimator.reset()

        assertNull(estimator.timeMap(clock))
        val reopened = stampSeries(WINDOW, firstFrame = 5L, firstNs = FIRST_NS + 40_000_000_000L, usPerFrame = NOMINAL_US_PER_FRAME)
        estimator.add(reopened[0])
        assertNull(estimator.timeMap(clock))
        reopened.drop(1).forEach(estimator::add)
        val map = assertNotNull(estimator.timeMap(clockFor(reopened)))
        assertEquals(NOMINAL_US_PER_FRAME, map.usPerFrame, CLEAN_TOLERANCE_US_PER_FRAME)
        assertEquals(reopened.last().framePosition, map.baseFrame)
    }

    @Test
    fun backwardFrameStartsAFreshHistory() {
        val old = stampSeries(WINDOW)
        val clock = clockFor(old)
        val estimator = estimatorWith(old)
        val reopened = stampSeries(2, firstFrame = 5L, firstNs = old.last().monotonicNs + 1_000_000_000L, usPerFrame = NOMINAL_US_PER_FRAME)

        estimator.add(reopened[0])
        assertNull(estimator.timeMap(clock))
        estimator.add(reopened[1])

        val map = assertNotNull(estimator.timeMap(clock))
        assertEquals(NOMINAL_US_PER_FRAME, map.usPerFrame, 1e-6)
        assertEquals(reopened[1].framePosition, map.baseFrame)
    }

    @Test
    fun mapHostUsFromClockOffset() {
        val stamps = stampSeries(WINDOW)

        val map = assertNotNull(estimatorWith(stamps).timeMap(clockFor(stamps)))

        listOf(
            stamps.first().framePosition,
            stamps[WINDOW / 2].framePosition,
            stamps.last().framePosition,
            stamps.last().framePosition + 4 * FRAMES_PER_STAMP,
        ).forEach { frame ->
            val expectedHostUs =
                stamps.first().monotonicNs / 1e3 + (frame - stamps.first().framePosition) * TRUE_US_PER_FRAME +
                    HOST_OFFSET_MS * 1e3
            assertEquals(expectedHostUs, map.hostUsAtFrame(frame), 1.0, "frame $frame")
        }
    }

    @Test
    fun baseFrameIsTheNewestStampFrame() {
        val stamps = stampSeries(10)

        val map = assertNotNull(estimatorWith(stamps).timeMap(clockFor(stamps)))

        assertEquals(stamps.last().framePosition, map.baseFrame)
    }

    @Test
    fun windowKeepsOnlyTheNewest32Stamps() {
        val clean = stampSeries(WINDOW + 1)
        val stamps = clean.mapIndexed { index, stamp -> if (index == 0) stamp.copy(monotonicNs = stamp.monotonicNs + 5_000_000L) else stamp }

        val map = assertNotNull(estimatorWith(stamps).timeMap(clockFor(clean)))

        assertEquals(TRUE_US_PER_FRAME, map.usPerFrame, CLEAN_TOLERANCE_US_PER_FRAME)
    }

    @Test
    fun windowStillCountsTheThirtySecondNewestStamp() {
        val oldestDelayNs = 2_000_000L
        val clean = stampSeries(WINDOW)
        val stamps = clean.mapIndexed { index, stamp -> if (index == 0) stamp.copy(monotonicNs = stamp.monotonicNs + oldestDelayNs) else stamp }
        val expectedShiftUsPerFrame = -(WINDOW - 1) / 2.0 * (oldestDelayNs / 1e3) / (SUM_SQUARED_DEVIATIONS * FRAMES_PER_STAMP)

        val map = assertNotNull(estimatorWith(stamps).timeMap(clockFor(clean)))

        assertEquals(TRUE_US_PER_FRAME + expectedShiftUsPerFrame, map.usPerFrame, 1e-6)
    }

    @Test
    fun staysExactWhenMonotonicNanosAndFramesAreHuge() {
        val stamps = stampSeries(WINDOW, firstFrame = 5_000_000_000L, firstNs = 8_000_000_000_000_000L)

        val map = assertNotNull(estimatorWith(stamps).timeMap(clockFor(stamps)))

        assertEquals(TRUE_US_PER_FRAME, map.usPerFrame, CLEAN_TOLERANCE_US_PER_FRAME)
        assertEquals(stamps.last().framePosition, map.baseFrame)
        val expectedHostUs = stamps.last().monotonicNs / 1e3 + HOST_OFFSET_MS * 1e3
        assertEquals(expectedHostUs, map.hostUsAtBaseFrame.toDouble(), 1.0)
    }

    @Test
    fun clockDriftFlowsIntoUsPerFrame() {
        val stamps = stampSeries(WINDOW)
        val driftPerMs = 1e-4

        val map = assertNotNull(estimatorWith(stamps).timeMap(clockFor(stamps, driftPerMs)))

        assertEquals(TRUE_US_PER_FRAME * (1 + driftPerMs), map.usPerFrame, CLEAN_TOLERANCE_US_PER_FRAME)
    }

    @Test
    fun stampsAtOneFrameGiveNoMap() {
        val stamps = List(5) { index -> OutputStamp(FIRST_FRAME, FIRST_NS + index * 250_000_000L) }

        assertNull(estimatorWith(stamps).timeMap(clockFor(stamps)))
    }

    @Test
    fun timeRunningBackwardsAlongFramesGivesNoMap() {
        val stamps = List(5) { index -> OutputStamp(FIRST_FRAME + index * FRAMES_PER_STAMP, FIRST_NS - index * 250_000_000L) }

        assertNull(estimatorWith(stamps).timeMap(clockFor(stamps)))
    }

    @Test
    fun unsyncedClockGivesNoMap() {
        val stamps = stampSeries(WINDOW)

        assertNull(estimatorWith(stamps).timeMap(ClockSync()))
    }
}
