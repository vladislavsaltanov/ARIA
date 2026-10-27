package aria.monitor.core

import java.nio.ByteBuffer
import java.nio.ByteOrder
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertTrue

private const val MAGIC = 0x4C505241
private const val OFFSET_MAGIC = 0
private const val OFFSET_VERSION = 4
private const val OFFSET_GENERATION = 8
private const val OFFSET_RATE = 16
private const val OFFSET_VOICE_COUNT = 24
private const val OFFSET_FIRST_VOICE = 28
private const val OFFSET_VOICE_PLAYING = OFFSET_FIRST_VOICE + 4 + 4 + 8 + 8 + 8 + 8 + 8
private const val OFFSET_VOICE_END_FRAME_PRESENT = OFFSET_VOICE_PLAYING + 1
private const val OFFSET_EMPTY_GRID_PRESENT = 28
private const val OFFSET_EMPTY_TEST_PRESENT = 29
private const val OFFSET_EMPTY_BACKING_GAIN = 30
private const val OFFSET_EMPTY_CLICK_GAIN = 34
private const val OFFSET_EMPTY_MASTER_GAIN = 38
private const val OFFSET_EMPTY_MASTER_RAMP = 42
private const val OFFSET_EMPTY_TRIM_US = 46
private const val OFFSET_EMPTY_TIME_MAP = 54
private const val EMPTY_PLAN_BYTES = 78

private fun le(bytes: ByteArray): ByteBuffer = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN)

private fun ByteArray.withInt(offset: Int, value: Int): ByteArray =
    copyOf().also { le(it).putInt(offset, value) }

private fun ByteArray.withByte(offset: Int, value: Int): ByteArray =
    copyOf().also { it[offset] = value.toByte() }

private fun goldenBytes(): ByteArray = PlanWire.encode(goldenPlan)

class PlanWireTest {
    @Test
    fun roundTripPreservesEveryField() {
        val voice = goldenPlan.voices.first()
        assertTrue(voice.anchorFrame > Int.MAX_VALUE)
        assertTrue(voice.endFrame != null && voice.fade != null)
        assertTrue(goldenPlan.grid != null && goldenPlan.test != null)

        assertEquals(goldenPlan, PlanWire.decode(PlanWire.encode(goldenPlan)))
    }

    @Test
    fun roundTripKeepsEachOptionalIndependent() {
        val voices = goldenPlan.voices
        val onlyEnd = voices[0].copy(fade = null)
        val onlyFade = voices[0].copy(endFrame = null)
        val plans = listOf(
            goldenPlan.copy(voices = listOf(onlyEnd, onlyFade), grid = null, test = goldenPlan.test),
            goldenPlan.copy(voices = listOf(onlyFade, onlyEnd), grid = goldenPlan.grid, test = null),
            goldenPlan.copy(grid = null, test = null),
        )
        plans.forEach { assertEquals(it, PlanWire.decode(PlanWire.encode(it))) }
    }

    @Test
    fun emptyPlanRoundTrips() {
        assertEquals(emptyPlan, PlanWire.decode(PlanWire.encode(emptyPlan)))
    }

    @Test
    fun moreThanEightVoicesRoundTrip() {
        val voices = (1..12).map { goldenPlan.voices[it % 2].copy(id = it, anchorFrame = it * 3_000_000_000L) }
        val plan = goldenPlan.copy(voices = voices)

        assertEquals(plan, PlanWire.decode(PlanWire.encode(plan)))
    }

    @Test
    fun goldenBytesMatchFixture() {
        assertContentEquals(GoldenFixtures.binary(), goldenBytes())
    }

    @Test
    fun goldenJsonMatchesReferencePlan() {
        assertEquals(goldenPlan.toGoldenJson(), GoldenFixtures.json())
    }

    @Test
    fun goldenFixtureDecodesToReferencePlan() {
        assertEquals(goldenPlan, PlanWire.decode(GoldenFixtures.binary()))
    }

    @Test
    fun headerIsAsciiARPLThenVersionOneAsLittleEndianU32() {
        val bytes = goldenBytes()

        assertEquals(1, PlanWire.VERSION)
        assertContentEquals("ARPL".toByteArray(Charsets.US_ASCII), bytes.copyOfRange(OFFSET_MAGIC, OFFSET_VERSION))
        assertEquals(MAGIC, le(bytes).getInt(OFFSET_MAGIC))
        assertContentEquals(byteArrayOf(1, 0, 0, 0), bytes.copyOfRange(OFFSET_VERSION, OFFSET_GENERATION))
    }

    @Test
    fun emptyPlanIs78BytesWithGenerationRateVoiceCountGridTestGainsTrimTimeMapInOrder() {
        val bytes = PlanWire.encode(emptyPlan.copy(generation = 9L, rate = 0.5, backingGain = 0.25f, clickGain = 0.5f, masterGain = 2.0f, masterRampSeconds = 1.0f, trimUs = -3L, timeMap = TimeMap(7L, 8L, 0.125)))
        val buffer = le(bytes)

        assertEquals(EMPTY_PLAN_BYTES, bytes.size)
        assertEquals(9L, buffer.getLong(OFFSET_GENERATION))
        assertEquals(0.5, buffer.getDouble(OFFSET_RATE))
        assertEquals(0, buffer.getInt(OFFSET_VOICE_COUNT))
        assertEquals(0, bytes[OFFSET_EMPTY_GRID_PRESENT].toInt())
        assertEquals(0, bytes[OFFSET_EMPTY_TEST_PRESENT].toInt())
        assertEquals(0.25f, buffer.getFloat(OFFSET_EMPTY_BACKING_GAIN))
        assertEquals(0.5f, buffer.getFloat(OFFSET_EMPTY_CLICK_GAIN))
        assertEquals(2.0f, buffer.getFloat(OFFSET_EMPTY_MASTER_GAIN))
        assertEquals(1.0f, buffer.getFloat(OFFSET_EMPTY_MASTER_RAMP))
        assertEquals(-3L, buffer.getLong(OFFSET_EMPTY_TRIM_US))
        assertEquals(7L, buffer.getLong(OFFSET_EMPTY_TIME_MAP))
        assertEquals(8L, buffer.getLong(OFFSET_EMPTY_TIME_MAP + 8))
        assertEquals(0.125, buffer.getDouble(OFFSET_EMPTY_TIME_MAP + 16))
    }

    @Test
    fun voiceRecordIsIdEpochTrackHiTrackLoGainAnchorUsAnchorFramePlayingEndFrameFade() {
        val voice = goldenPlan.voices.first()
        val buffer = le(goldenBytes())
        buffer.position(OFFSET_FIRST_VOICE)

        assertEquals(goldenPlan.voices.size, buffer.getInt(OFFSET_VOICE_COUNT))
        assertEquals(voice.id, buffer.getInt())
        assertEquals(voice.epoch, buffer.getInt())
        assertEquals(voice.trackHi, buffer.getLong())
        assertEquals(voice.trackLo, buffer.getLong())
        assertEquals(voice.gainDb, buffer.getDouble())
        assertEquals(voice.anchorUs, buffer.getLong())
        assertEquals(voice.anchorFrame, buffer.getLong())
        assertEquals(OFFSET_VOICE_PLAYING, buffer.position())
        assertEquals(1, buffer.get().toInt())
        assertEquals(1, buffer.get().toInt())
        assertEquals(voice.endFrame, buffer.getLong())
        assertEquals(1, buffer.get().toInt())
        val fade = checkNotNull(voice.fade)
        assertEquals(fade.from, buffer.getDouble())
        assertEquals(fade.to, buffer.getDouble())
        assertEquals(fade.progress, buffer.getDouble())
        assertEquals(fade.frames, buffer.getInt())
        assertEquals(fade.curve.length, buffer.getInt())
        val curve = ByteArray(fade.curve.length).also { buffer.get(it) }
        assertEquals(fade.curve, String(curve, Charsets.UTF_8))
        assertEquals(1, buffer.get().toInt())
    }

    @Test
    fun decodeRejectsBadMagicAndVersion() {
        val bytes = goldenBytes()

        assertFailsWith<IllegalArgumentException> { PlanWire.decode(bytes.withInt(OFFSET_MAGIC, 0x4C505242)) }
        assertFailsWith<IllegalArgumentException> { PlanWire.decode(bytes.withInt(OFFSET_VERSION, PlanWire.VERSION + 1)) }
        assertFailsWith<IllegalArgumentException> { PlanWire.decode(bytes.withInt(OFFSET_VERSION, 0)) }
        assertFailsWith<IllegalArgumentException> { PlanWire.decode(ByteArray(0)) }
    }

    @Test
    fun decodeRejectsTruncatedAtEveryLength() {
        val bytes = goldenBytes()

        for (length in 0 until bytes.size) {
            assertFailsWith<IllegalArgumentException>("prefix of $length bytes") {
                PlanWire.decode(bytes.copyOf(length))
            }
        }
    }

    @Test
    fun decodeRejectsTrailingBytes() {
        assertFailsWith<IllegalArgumentException> { PlanWire.decode(goldenBytes() + byteArrayOf(0)) }
        assertFailsWith<IllegalArgumentException> { PlanWire.decode(PlanWire.encode(emptyPlan) + byteArrayOf(0)) }
    }

    @Test
    fun decodeRejectsOversizedListCount() {
        val bytes = goldenBytes()

        assertFailsWith<IllegalArgumentException> { PlanWire.decode(bytes.withInt(OFFSET_VOICE_COUNT, -1)) }
        assertFailsWith<IllegalArgumentException> { PlanWire.decode(bytes.withInt(OFFSET_VOICE_COUNT, Int.MAX_VALUE)) }
        assertFailsWith<IllegalArgumentException> { PlanWire.decode(bytes.withInt(OFFSET_VOICE_COUNT, goldenPlan.voices.size + 1)) }
        assertFailsWith<IllegalArgumentException> { PlanWire.decode(PlanWire.encode(emptyPlan).withInt(OFFSET_VOICE_COUNT, 1_000_000)) }
    }

    @Test
    fun decodeRejectsOversizedStringLength() {
        val bytes = goldenBytes()
        val curveLengthOffset = OFFSET_VOICE_END_FRAME_PRESENT + 1 + 8 + 1 + 8 + 8 + 8 + 4

        assertEquals(checkNotNull(goldenPlan.voices.first().fade).curve.length, le(bytes).getInt(curveLengthOffset))
        assertFailsWith<IllegalArgumentException> { PlanWire.decode(bytes.withInt(curveLengthOffset, -1)) }
        assertFailsWith<IllegalArgumentException> { PlanWire.decode(bytes.withInt(curveLengthOffset, Int.MAX_VALUE)) }
    }

    @Test
    fun decodeRejectsBooleanAndPresenceBytesOtherThanZeroOrOne() {
        val bytes = goldenBytes()

        assertFailsWith<IllegalArgumentException> { PlanWire.decode(bytes.withByte(OFFSET_VOICE_PLAYING, 2)) }
        assertFailsWith<IllegalArgumentException> { PlanWire.decode(bytes.withByte(OFFSET_VOICE_END_FRAME_PRESENT, 0xFF)) }
    }
}
