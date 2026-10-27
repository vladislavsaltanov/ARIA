package aria.monitor.core

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import java.io.File

val goldenPlan = RenderPlan(
    generation = 4_294_967_298L,
    rate = 1.0,
    voices = listOf(
        VoicePlan(
            id = 1,
            epoch = 3,
            trackHi = 0x8F3A1C2B4D5E6F70uL.toLong(),
            trackLo = 0x0123456789ABCDEFL,
            gainDb = -6.0,
            anchorUs = 13_000_000_123_456L,
            anchorFrame = 3_000_000_000L,
            playing = true,
            endFrame = 3_480_000_000L,
            fade = FadeTimeline(
                from = 1.0,
                to = 0.0,
                progress = 0.25,
                frames = 96_000,
                curve = "Logarithmic",
                stopAtEnd = true,
            ),
        ),
        VoicePlan(
            id = 2,
            epoch = 1,
            trackHi = 0x0123456789ABCDEFL,
            trackLo = 0xFEDCBA9876543210uL.toLong(),
            gainDb = 0.0,
            anchorUs = 13_000_000_654_321L,
            anchorFrame = 48_000L,
            playing = false,
            endFrame = null,
            fade = null,
        ),
    ),
    grid = GridTimeline(voice = 1, bpm = 128.0, beatsPerBar = 4, firstBeatFrame = 3_000_024_000L),
    test = TestTimeline(anchorUs = 13_000_000_000_000L, intervalUs = 500_000.0),
    backingGain = 0.5f,
    clickGain = 0.25f,
    masterGain = 1.0f,
    masterRampSeconds = 0.001953125f,
    trimUs = -12_500L,
    timeMap = TimeMap(
        baseFrame = 3_000_000_000L,
        hostUsAtBaseFrame = 13_062_500_000_000L,
        usPerFrame = 1_000_000.0 / 48_000.0,
    ),
)

val emptyPlan = RenderPlan(
    generation = 0L,
    rate = 1.0,
    voices = emptyList(),
    grid = null,
    test = null,
    backingGain = 0.0f,
    clickGain = 0.0f,
    masterGain = 1.0f,
    masterRampSeconds = 0.0f,
    trimUs = 0L,
    timeMap = TimeMap(baseFrame = 0L, hostUsAtBaseFrame = 0L, usPerFrame = 1_000_000.0 / 48_000.0),
)

private fun <T : Any> T?.orNull(build: (T) -> JsonElement): JsonElement = if (this == null) JsonNull else build(this)

private fun FadeTimeline.toJson() = JsonObject(
    mapOf(
        "from" to JsonPrimitive(from),
        "to" to JsonPrimitive(to),
        "progress" to JsonPrimitive(progress),
        "frames" to JsonPrimitive(frames),
        "curve" to JsonPrimitive(curve),
        "stopAtEnd" to JsonPrimitive(stopAtEnd),
    ),
)

private fun VoicePlan.toJson() = JsonObject(
    mapOf(
        "id" to JsonPrimitive(id),
        "epoch" to JsonPrimitive(epoch),
        "trackHi" to JsonPrimitive(trackHi),
        "trackLo" to JsonPrimitive(trackLo),
        "gainDb" to JsonPrimitive(gainDb),
        "anchorUs" to JsonPrimitive(anchorUs),
        "anchorFrame" to JsonPrimitive(anchorFrame),
        "playing" to JsonPrimitive(playing),
        "endFrame" to endFrame.orNull { JsonPrimitive(it) },
        "fade" to fade.orNull { it.toJson() },
    ),
)

private fun GridTimeline.toJson() = JsonObject(
    mapOf(
        "voice" to JsonPrimitive(voice),
        "bpm" to JsonPrimitive(bpm),
        "beatsPerBar" to JsonPrimitive(beatsPerBar),
        "firstBeatFrame" to JsonPrimitive(firstBeatFrame),
    ),
)

private fun TestTimeline.toJson() = JsonObject(
    mapOf("anchorUs" to JsonPrimitive(anchorUs), "intervalUs" to JsonPrimitive(intervalUs)),
)

private fun TimeMap.toJson() = JsonObject(
    mapOf(
        "baseFrame" to JsonPrimitive(baseFrame),
        "hostUsAtBaseFrame" to JsonPrimitive(hostUsAtBaseFrame),
        "usPerFrame" to JsonPrimitive(usPerFrame),
    ),
)

fun RenderPlan.toGoldenJson() = JsonObject(
    mapOf(
        "generation" to JsonPrimitive(generation),
        "rate" to JsonPrimitive(rate),
        "voices" to JsonArray(voices.map { it.toJson() }),
        "grid" to grid.orNull { it.toJson() },
        "test" to test.orNull { it.toJson() },
        "backingGain" to JsonPrimitive(backingGain),
        "clickGain" to JsonPrimitive(clickGain),
        "masterGain" to JsonPrimitive(masterGain),
        "masterRampSeconds" to JsonPrimitive(masterRampSeconds),
        "trimUs" to JsonPrimitive(trimUs),
        "timeMap" to timeMap.toJson(),
    ),
)

object GoldenFixtures {
    private val directory = File("../../tests/fixtures/engine")
    private val binaryFile = directory.resolve("plan-v1.bin")
    private val jsonFile = directory.resolve("plan-v1.json")
    private val prettyJson = Json { prettyPrint = true }

    init {
        if (System.getProperty("regenerateGolden") != null) {
            binaryFile.writeBytes(PlanWire.encode(goldenPlan))
            jsonFile.writeText(prettyJson.encodeToString(JsonElement.serializer(), goldenPlan.toGoldenJson()) + "\n")
        }
    }

    fun binary(): ByteArray = binaryFile.readBytes()

    fun json(): JsonElement = Json.parseToJsonElement(jsonFile.readText())
}
