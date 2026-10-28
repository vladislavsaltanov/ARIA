package aria.monitor.core

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.double
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.long
import kotlin.math.abs
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

private const val DB_TO_GAIN_ULPS = 2

private data class Vector(val fn: String, val args: JsonArray, val expected: JsonElement) {
    val label: String get() = "$fn(${args.joinToString(", ")})"
}

private val vectors: List<Vector> by lazy {
    val text = checkNotNull(TimelineParityTest::class.java.getResource("/parity/timeline.json")) { "parity/timeline.json missing" }.readText()
    Json.parseToJsonElement(text).jsonArray.map {
        val entry = it.jsonObject
        Vector(entry.getValue("fn").jsonPrimitive.content, entry.getValue("args").jsonArray, entry.getValue("expected"))
    }
}

private fun JsonElement.double(): Double = jsonPrimitive.double

private fun JsonElement.voiceOrNull(): VoiceTimeline? =
    if (this is JsonNull) null else Json.decodeFromJsonElement(VoiceTimeline.serializer(), this)

private fun JsonElement.gridOrNull(): GridTimeline? =
    if (this is JsonNull) null else Json.decodeFromJsonElement(GridTimeline.serializer(), this)

private fun JsonElement.fadeOrNull(): FadeTimeline? =
    if (this is JsonNull) null else Json.decodeFromJsonElement(FadeTimeline.serializer(), this)

private fun assertExact(vector: Vector, expected: Double, actual: Double, what: String = "result") {
    assertTrue(expected == actual, "${vector.label}: $what expected $expected but was $actual")
}

private fun assertWithinUlps(vector: Vector, expected: Double, actual: Double) {
    assertTrue(abs(expected - actual) <= DB_TO_GAIN_ULPS * Math.ulp(expected), "${vector.label}: expected $expected but was $actual")
}

private val checks: Map<String, (Vector) -> Unit> = mapOf(
    "frameAt" to { vector ->
        val args = vector.args
        val voice = checkNotNull(args[0].voiceOrNull()) { vector.label }
        assertExact(vector, vector.expected.double(), frameAt(voice, args[1].double(), args[2].double()))
    },
    "hostUsOf" to { vector ->
        val args = vector.args
        val voice = checkNotNull(args[0].voiceOrNull()) { vector.label }
        assertExact(vector, vector.expected.double(), hostUsOf(voice, args[1].double(), args[2].double()))
    },
    "curve" to { vector ->
        val args = vector.args
        assertExact(vector, vector.expected.double(), curve(args[0].jsonPrimitive.content, args[1].double()))
    },
    "fadeLevel" to { vector ->
        val args = vector.args
        assertExact(vector, vector.expected.double(), fadeLevel(args[0].fadeOrNull(), args[1].double()))
    },
    "beatsBetween" to { vector ->
        val args = vector.args
        val actual = beatsBetween(args[0].gridOrNull(), args[1].voiceOrNull(), args[2].double(), args[3].double(), args[4].double())
        val expected = vector.expected.jsonArray
        assertEquals(expected.size, actual.size, "${vector.label}: beat count")
        expected.forEachIndexed { position, element ->
            val beat = element.jsonObject
            val got = actual[position]
            assertEquals(beat.getValue("index").jsonPrimitive.long, got.index, "${vector.label}: index at $position")
            assertEquals(beat.getValue("accent").jsonPrimitive.boolean, got.accent, "${vector.label}: accent at $position")
            assertExact(vector, beat.getValue("hostUs").double(), got.hostUs, "hostUs at $position")
        }
    },
    "dbToGain" to { vector ->
        assertWithinUlps(vector, vector.expected.double(), dbToGain(vector.args[0].double()))
    },
)

private fun verifyAll(fn: String) {
    val selected = vectors.filter { it.fn == fn }
    assertTrue(selected.isNotEmpty(), "no vectors for $fn")
    val check = checkNotNull(checks[fn]) { "no check for $fn" }
    selected.forEach(check)
}

class TimelineParityTest {
    private fun playingVoice(anchorUs: Long = 0L, anchorFrame: Long = 0L) = VoiceTimeline(
        id = 1,
        epoch = 0,
        track = "t",
        kind = "entry",
        fallback = false,
        gainDb = 0.0,
        anchorUs = anchorUs,
        anchorFrame = anchorFrame,
        playing = true,
        endFrame = null,
        fade = null,
    )

    private fun grid(bpm: Double = 120.0, beatsPerBar: Int = 4) = GridTimeline(voice = 1, bpm = bpm, beatsPerBar = beatsPerBar, firstBeatFrame = 0L)

    @Test
    fun frameAtMatchesVectors() = verifyAll("frameAt")

    @Test
    fun hostUsOfMatchesVectors() = verifyAll("hostUsOf")

    @Test
    fun curveMatchesVectors() = verifyAll("curve")

    @Test
    fun fadeLevelMatchesVectors() = verifyAll("fadeLevel")

    @Test
    fun beatsBetweenMatchesVectors() = verifyAll("beatsBetween")

    @Test
    fun dbToGainMatchesVectors() = verifyAll("dbToGain")

    @Test
    fun everyVectorFunctionIsKnownToTheTest() {
        val unknown = vectors.map { it.fn }.toSet() - checks.keys
        assertTrue(unknown.isEmpty(), "vector file contains functions without a Kotlin check: $unknown")
    }

    @Test
    fun beatsBetweenEmptyWhenBpmZero() {
        assertEquals(emptyList(), beatsBetween(grid(bpm = 0.0), playingVoice(), 0.0, 2_000_000.0, 48000.0))
    }

    @Test
    fun beatsBetweenEmptyWhenBpmIsNaN() {
        assertEquals(emptyList(), beatsBetween(grid(bpm = Double.NaN), playingVoice(), 0.0, 2_000_000.0, 48000.0))
    }

    @Test
    fun beatsBetweenEmptyWhenNoVoice() {
        assertEquals(emptyList(), beatsBetween(grid(), null, 0.0, 2_000_000.0, 48000.0))
    }
}
