package aria.monitor.core

import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import kotlin.math.abs
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

@Serializable
private data class ClockPong(val t0: Double, val t1Us: Long, val t2Us: Long, val t3: Double)

@Serializable
private data class ClockQuery(val localMs: Double, val hostUs: Double)

@Serializable
private data class ClockAfter(
    val offsetAt: Double,
    val ready: Boolean,
    val locked: Boolean,
    val uncertaintyUs: Long,
    val hostUsAt: Double,
    val localMsAt: Double,
)

@Serializable
private data class ClockStep(val pong: ClockPong, val query: ClockQuery, val after: ClockAfter)

@Serializable
private data class ClockSequence(val name: String, val steps: List<ClockStep>)

private val sequences: List<ClockSequence> by lazy {
    val text = checkNotNull(ClockSyncParityTest::class.java.getResource("/parity/clock.json")) { "parity/clock.json missing" }.readText()
    Json.decodeFromString<List<ClockSequence>>(text)
}

private val expectedSequenceNames = setOf("drift120", "sevenPongs", "slowLink", "stalePong", "marginTie")

private fun assertSame(label: String, expected: Double, actual: Double) {
    assertTrue(expected == actual, "$label: expected $expected but was $actual")
}

private fun verifySequence(name: String): Int {
    val sequence = checkNotNull(sequences.singleOrNull { it.name == name }) { "sequence $name missing or duplicated" }
    val clock = ClockSync()
    var verified = 0
    sequence.steps.forEachIndexed { index, step ->
        val label = "$name[$index]"
        val pong = step.pong
        clock.onPong(pong.t0, pong.t1Us, pong.t2Us, pong.t3)
        assertSame("$label offsetAt", step.after.offsetAt, clock.offsetAt(step.query.localMs))
        assertEquals(step.after.ready, clock.ready, "$label ready")
        assertEquals(step.after.locked, clock.locked, "$label locked")
        assertEquals(step.after.uncertaintyUs, clock.uncertaintyUs, "$label uncertaintyUs")
        assertSame("$label hostUsAt", step.after.hostUsAt, clock.hostUsAt(step.query.localMs))
        assertSame("$label localMsAt", step.after.localMsAt, clock.localMsAt(step.query.hostUs))
        verified += 1
    }
    return verified
}

private const val HOST_OFFSET_MS = 5_400_000.0

private fun ClockSync.pong(t0: Double, delayMs: Double, backExtraMs: Double = 0.0) {
    val out = delayMs / 2
    val back = delayMs / 2 + backExtraMs
    val hostUs = Math.round((t0 + out + HOST_OFFSET_MS) * 1000)
    onPong(t0, hostUs, hostUs, t0 + out + back)
}

class ClockSyncParityTest {
    @Test
    fun driftSequenceMatchesJs() {
        assertEquals(120, verifySequence("drift120"))
    }

    @Test
    fun sevenPongSequenceMatchesJs() {
        assertEquals(7, verifySequence("sevenPongs"))
    }

    @Test
    fun slowLinkSequenceMatchesJs() {
        verifySequence("slowLink")
    }

    @Test
    fun stalePongSequenceMatchesJs() {
        verifySequence("stalePong")
    }

    @Test
    fun marginTieSequenceMatchesJs() {
        verifySequence("marginTie")
    }

    @Test
    fun everyRecordOfEverySequenceIsVerified() {
        assertEquals(expectedSequenceNames, sequences.map { it.name }.toSet(), "vector file sequences")
        assertEquals(sequences.size, expectedSequenceNames.size, "duplicate sequence names")
        assertTrue(sequences.all { it.steps.isNotEmpty() }, "empty sequence in vector file")
        val verified = expectedSequenceNames.sumOf { verifySequence(it) }
        assertEquals(sequences.sumOf { it.steps.size }, verified)
        assertTrue(verified >= 120 + 7, "vector file shrank to $verified records")
    }

    @Test
    fun vectorHostTimesExceedUnsigned32Bit() {
        val tooSmall = sequences.flatMap { it.steps }.filter { it.pong.t1Us <= UInt.MAX_VALUE.toLong() || it.pong.t2Us <= UInt.MAX_VALUE.toLong() }
        assertTrue(tooSmall.isEmpty(), "${tooSmall.size} vector records have host time within 32 bits")
    }

    @Test
    fun outlierSampleMovesOffsetUnder0_2ms() {
        val clock = ClockSync()
        repeat(40) { index -> clock.pong(1000.0 + index * 500.0, 4.0 + (index % 3) * 0.2) }
        val queryMs = 22_000.0
        val before = clock.offsetAt(queryMs)
        clock.pong(21_000.0, 4.0, backExtraMs = 50.0)
        val after = clock.offsetAt(queryMs)
        assertEquals(HOST_OFFSET_MS, before, 0.01)
        assertTrue(abs(after - before) < 0.2, "outlier moved offset by ${after - before} ms")
    }

    @Test
    fun notLockedBefore8Samples() {
        val clock = ClockSync()
        repeat(7) { index ->
            clock.pong(1000.0 + index * 500.0, 4.0)
            assertTrue(clock.ready, "ready after ${index + 1} samples")
            assertFalse(clock.locked, "locked after ${index + 1} samples")
        }
        clock.pong(4500.0, 4.0)
        assertTrue(clock.locked)
    }

    @Test
    fun notLockedWhenMinDelayOver40ms() {
        val clock = ClockSync()
        repeat(20) { index -> clock.pong(1000.0 + index * 500.0, 45.0 + (index % 4)) }
        assertTrue(clock.ready)
        assertFalse(clock.locked)
        assertEquals(45.0, clock.minDelayMs)
        assertEquals(22_500L, clock.uncertaintyUs)
    }

    @Test
    fun lockedRequiresMinDelayStrictlyBelow40ms() {
        val atLimit = ClockSync()
        repeat(10) { index -> atLimit.pong(1000.0 + index * 500.0, 40.0) }
        assertFalse(atLimit.locked)
        val below = ClockSync()
        repeat(10) { index -> below.pong(1000.0 + index * 500.0, 39.5) }
        assertTrue(below.locked)
    }

    @Test
    fun freshClockIsNotReady() {
        val clock = ClockSync()
        assertFalse(clock.ready)
        assertFalse(clock.locked)
        assertNull(clock.uncertaintyUs)
        assertNull(clock.minDelayMs)
        assertSame("offsetAt", 0.0, clock.offsetAt(123_456.0))
        assertSame("hostUsAt", 5_000.0, clock.hostUsAt(5.0))
        assertSame("localMsAt", 5.0, clock.localMsAt(5_000.0))
    }

    @Test
    fun uncertaintyRoundsTiesTowardPositiveInfinity() {
        val positive = ClockSync()
        positive.pong(1000.0, 0.125)
        assertEquals(63L, positive.uncertaintyUs)
        val negative = ClockSync()
        negative.onPong(1000.0, 5_400_000_000_000L, 5_400_000_000_125L, 1000.0)
        assertEquals(-0.125, negative.minDelayMs)
        assertEquals(-62L, negative.uncertaintyUs)
    }
}
