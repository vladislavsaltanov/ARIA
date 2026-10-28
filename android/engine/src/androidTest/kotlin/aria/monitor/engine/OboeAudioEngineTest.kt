package aria.monitor.engine

import aria.monitor.core.CHUNK_FRAMES
import aria.monitor.core.ChunkKey
import aria.monitor.core.EngineEvent
import aria.monitor.core.POOL_SLOTS
import aria.monitor.core.RenderPlan
import aria.monitor.core.TimeMap
import aria.monitor.core.VoicePlan
import aria.monitor.core.guidHiLo
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Assume.assumeTrue
import org.junit.Before
import org.junit.Test

private const val TRACK = "01234567-89ab-cdef-0123-456789abcdef"
private const val FRAMES = 4800
private const val BYTES_PER_FRAME = 4
private const val SETTLE_MS = 150L
private const val POLL_MS = 50L
private const val REOPEN_DEADLINE_NS = 3_000_000_000L
private const val US_PER_FRAME = 1_000_000.0 / 48_000.0

private fun keyOf(chunk: Int) = ChunkKey(TRACK, chunk)

private fun pcm(frames: Int) = ByteArray(frames * BYTES_PER_FRAME) { (it and 0x7F).toByte() }

private fun planAt(generation: Long, nowUs: Long, voices: List<VoicePlan> = emptyList()) = RenderPlan(
    generation = generation,
    rate = 1.0,
    voices = voices,
    grid = null,
    test = null,
    backingGain = 1f,
    clickGain = 1f,
    masterGain = 1f,
    masterRampSeconds = 0f,
    trimUs = 0,
    timeMap = TimeMap(baseFrame = 0, hostUsAtBaseFrame = nowUs, usPerFrame = US_PER_FRAME),
)

private fun voicePlaying(nowUs: Long): VoicePlan {
    val (hi, lo) = guidHiLo(TRACK)
    return VoicePlan(
        id = 1, epoch = 1, trackHi = hi, trackLo = lo, gainDb = 0.0, anchorUs = nowUs, anchorFrame = 0,
        playing = true, endFrame = null, fade = null,
    )
}

private fun nowUs() = System.nanoTime() / 1000

class OboeAudioEngineTest {
    private lateinit var engine: OboeAudioEngine

    @Before
    fun create() {
        engine = OboeAudioEngine()
    }

    @After
    fun release() {
        engine.release()
    }

    @Test
    fun startReturnsTrueAndStopIsClean() {
        assertTrue(engine.start())
        engine.stop()
        assertNull(engine.sampleStamp())
    }

    @Test
    fun publishEmptyPlanKeepsRunning500ms() {
        val events = mutableListOf<EngineEvent>()
        engine.setEventListener { events += it }
        assertTrue(engine.start())
        engine.publish(planAt(1, nowUs()))
        Thread.sleep(SETTLE_MS)
        val first = engine.sampleStamp()
        assumeTrue("no output timestamps on this device", first != null)
        Thread.sleep(500)
        val second = engine.sampleStamp()
        assertNotNull(second)
        assertTrue(second!!.framePosition > first!!.framePosition)
        assertTrue(engine.xrunCount >= 0)
        assertTrue(events.isEmpty())
    }

    @Test
    fun stampsAreMonotonicWhenAvailable() {
        assertTrue(engine.start())
        Thread.sleep(SETTLE_MS)
        val first = engine.sampleStamp()
        assumeTrue("no output timestamps on this device", first != null)
        var previous = first!!
        repeat(10) {
            Thread.sleep(50)
            val next = engine.sampleStamp() ?: return@repeat
            assertTrue(next.framePosition >= previous.framePosition)
            assertTrue(next.monotonicNs >= previous.monotonicNs)
            previous = next
        }
        assertTrue(previous.framePosition > first.framePosition)
    }

    @Test
    fun writeChunkThenPublishDoesNotCrash() {
        assertTrue(engine.start())
        assertTrue(engine.write(keyOf(0), pcm(FRAMES), FRAMES))
        engine.markEmpty(keyOf(1))
        engine.retain(setOf(keyOf(0), keyOf(1)))
        val now = nowUs()
        engine.publish(planAt(1, now, listOf(voicePlaying(now))))
        Thread.sleep(300)
        engine.retain(emptySet())
        engine.publish(planAt(2, nowUs()))
        Thread.sleep(SETTLE_MS)
        assertTrue(engine.xrunCount >= 0)
        engine.stop()
    }

    @Test
    fun engineCanBeRestarted() {
        assertTrue(engine.start())
        engine.stop()
        assertTrue(engine.start())
        engine.publish(planAt(1, nowUs()))
        Thread.sleep(SETTLE_MS)
        engine.stop()
    }

    @Test
    fun startingARunningEngineRestartsItAndStaysRunning() {
        assertTrue(engine.start())
        assertTrue(engine.start())
        Thread.sleep(SETTLE_MS)
        val first = engine.sampleStamp()
        assumeTrue("no output timestamps on this device", first != null)
        Thread.sleep(SETTLE_MS)
        assertTrue(engine.sampleStamp()!!.framePosition > first!!.framePosition)
    }

    @Test
    fun stopTwiceIsHarmless() {
        assertTrue(engine.start())
        engine.stop()
        engine.stop()
        assertNull(engine.sampleStamp())
    }

    @Test
    fun stopWithoutStartIsHarmless() {
        engine.stop()
        assertNull(engine.sampleStamp())
        assertEquals(0, engine.xrunCount)
        engine.growBuffer()
    }

    @Test
    fun growBufferOnARunningEngineKeepsItRunning() {
        assertTrue(engine.start())
        repeat(3) { engine.growBuffer() }
        Thread.sleep(SETTLE_MS)
        assertTrue(engine.xrunCount >= 0)
    }

    @Test
    fun writeIsIdempotentForResidentKey() {
        repeat(3 * POOL_SLOTS) { assertTrue(engine.write(keyOf(0), pcm(FRAMES), FRAMES)) }
        for (chunk in 1 until POOL_SLOTS) assertTrue(engine.write(keyOf(chunk), pcm(FRAMES), FRAMES))
    }

    @Test
    fun writeRejectsInconsistentFrames() {
        assertFalse(engine.write(keyOf(0), pcm(FRAMES), FRAMES + 1))
        assertFalse(engine.write(keyOf(0), pcm(FRAMES), FRAMES - 1))
        assertFalse(engine.write(keyOf(0), ByteArray(0), FRAMES))
        assertFalse(engine.write(keyOf(0), pcm(FRAMES), -1))
        assertFalse(engine.write(keyOf(0), ByteArray((CHUNK_FRAMES + 1) * BYTES_PER_FRAME), CHUNK_FRAMES + 1))
        for (chunk in 0 until POOL_SLOTS) assertTrue(engine.write(keyOf(chunk), pcm(FRAMES), FRAMES))
    }

    @Test
    fun writeOfATrackThatIsNotAGuidIsRejectedWithoutThrowing() {
        assertFalse(engine.write(ChunkKey("not-a-guid", 0), pcm(FRAMES), FRAMES))
        engine.markEmpty(ChunkKey("not-a-guid", 0))
        engine.retain(setOf(ChunkKey("not-a-guid", 0)))
        for (chunk in 0 until POOL_SLOTS) assertTrue(engine.write(keyOf(chunk), pcm(FRAMES), FRAMES))
    }

    @Test
    fun retainedChunksSurviveAPublishAndUnretainedOnesAreReclaimedOnceTheCallbackAcks() {
        assertTrue(engine.start())
        val all = (0 until POOL_SLOTS).map { keyOf(it) }
        all.forEach { assertTrue(engine.write(it, pcm(FRAMES), FRAMES)) }
        engine.retain(all.toSet())
        engine.publish(planAt(1, nowUs()))
        Thread.sleep(SETTLE_MS)
        assertFalse(engine.write(keyOf(POOL_SLOTS), pcm(FRAMES), FRAMES))
        engine.retain(all.drop(1).toSet())
        Thread.sleep(SETTLE_MS)
        assertTrue(engine.write(keyOf(POOL_SLOTS), pcm(FRAMES), FRAMES))
    }

    @Test
    fun aDisconnectedStreamIsReopenedAndTheListenerHearsItOncePerReopen() {
        val events = mutableListOf<EngineEvent>()
        engine.setEventListener { events += it }
        assertTrue(engine.start())
        Thread.sleep(SETTLE_MS)
        engine.simulateDisconnectForTests()
        val deadline = System.nanoTime() + REOPEN_DEADLINE_NS
        while (events.isEmpty() && System.nanoTime() < deadline) {
            engine.sampleStamp()
            Thread.sleep(POLL_MS)
        }
        assertEquals(listOf(EngineEvent.StreamReopened), events)
        Thread.sleep(SETTLE_MS)
        val first = engine.sampleStamp()
        assumeTrue("no output timestamps on this device", first != null)
        Thread.sleep(SETTLE_MS)
        assertTrue(engine.sampleStamp()!!.framePosition > first!!.framePosition)
        assertEquals(1, events.size)
    }

    @Test
    fun callsAfterReleaseAreHarmless() {
        assertTrue(engine.start())
        engine.release()
        engine.release()
        assertFalse(engine.start())
        engine.stop()
        engine.publish(planAt(1, nowUs()))
        assertFalse(engine.write(keyOf(0), pcm(FRAMES), FRAMES))
        engine.markEmpty(keyOf(1))
        engine.retain(setOf(keyOf(0)))
        assertNull(engine.sampleStamp())
        assertEquals(0, engine.xrunCount)
        engine.growBuffer()
    }
}
