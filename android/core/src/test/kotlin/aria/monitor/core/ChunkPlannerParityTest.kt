package aria.monitor.core

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.double
import kotlinx.serialization.json.int
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.long
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

private data class StoreVector(val fn: String, val args: JsonArray, val expected: JsonArray) {
    val label: String get() = "$fn(${args.joinToString(", ")})"
}

private val vectors: List<StoreVector> by lazy {
    val text = checkNotNull(ChunkPlannerParityTest::class.java.getResource("/parity/store.json")) { "parity/store.json missing" }.readText()
    Json.parseToJsonElement(text).jsonArray.map {
        val entry = it.jsonObject
        StoreVector(entry.getValue("fn").jsonPrimitive.content, entry.getValue("args").jsonArray, entry.getValue("expected").jsonArray)
    }
}

private fun JsonElement.toWindow(): Window {
    val window = jsonObject
    return Window(window.getValue("track").jsonPrimitive.content, window.getValue("from").jsonPrimitive.long, window.getValue("to").jsonPrimitive.long)
}

private fun JsonElement.toChunkKey(): ChunkKey {
    val key = jsonObject
    return ChunkKey(key.getValue("track").jsonPrimitive.content, key.getValue("chunk").jsonPrimitive.int)
}

private val checks: Map<String, (StoreVector) -> Unit> = mapOf(
    "windowsFor" to { vector ->
        val timeline = Json.decodeFromJsonElement(ServerMessage.Timeline.serializer(), vector.args[0])
        val actual = windowsFor(timeline, vector.args[1].jsonPrimitive.double)
        assertEquals(vector.expected.map { it.toWindow() }, actual, vector.label)
    },
    "chunksFor" to { vector ->
        val window = vector.args[0].toWindow()
        val actual = if (vector.args.size > 1) chunksFor(window, vector.args[1].jsonPrimitive.int) else chunksFor(window)
        assertEquals(vector.expected.map { it.toChunkKey() }, actual, vector.label)
    },
)

private fun verifyAll(fn: String) {
    val selected = vectors.filter { it.fn == fn }
    assertTrue(selected.isNotEmpty(), "no vectors for $fn")
    val check = checkNotNull(checks[fn]) { "no check for $fn" }
    selected.forEach(check)
}

class ChunkPlannerParityTest {
    @Test
    fun windowsForMatchesVectors() = verifyAll("windowsFor")

    @Test
    fun chunksForMatchesVectors() = verifyAll("chunksFor")

    @Test
    fun everyVectorFunctionIsKnownToTheTest() {
        val unknown = vectors.map { it.fn }.toSet() - checks.keys
        assertTrue(unknown.isEmpty(), "vector file contains functions without a Kotlin check: $unknown")
    }

    @Test
    fun chunkFramesMatchesTheServerChunkSize() {
        assertEquals(96000, CHUNK_FRAMES)
    }
}
