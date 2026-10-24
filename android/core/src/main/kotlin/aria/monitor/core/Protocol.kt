package aria.monitor.core

import kotlinx.serialization.KSerializer
import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.jsonObject

@Serializable
data class Levels(
    val backingDb: Double,
    val backingOn: Boolean,
    val clickDb: Double,
    val clickOn: Boolean,
)

@Serializable
data class FadeTimeline(
    val from: Double,
    val to: Double,
    val progress: Double,
    val frames: Int,
    val curve: String,
    val stopAtEnd: Boolean,
)

@Serializable
data class VoiceTimeline(
    val id: Int,
    val epoch: Int,
    val track: String,
    val kind: String,
    val fallback: Boolean,
    val gainDb: Double,
    val anchorUs: Long,
    val anchorFrame: Long,
    val playing: Boolean,
    val endFrame: Long?,
    val fade: FadeTimeline?,
)

@Serializable
data class GridTimeline(
    val voice: Int,
    val bpm: Double,
    val beatsPerBar: Int,
    val firstBeatFrame: Long,
)

@Serializable
data class NextTimeline(
    val title: String,
    val track: String,
    val kind: String,
    val startFrame: Long,
)

@Serializable
data class DeckTimeline(
    val status: String,
    val title: String?,
    val next: NextTimeline?,
)

@Serializable
data class TestTimeline(
    val anchorUs: Long,
    val intervalUs: Double,
)

sealed interface ServerMessage {
    @Serializable
    data class Welcome(
        val listener: String,
        val role: String,
        val sampleRate: Int,
    ) : ServerMessage

    @Serializable
    data class Pong(
        val t0: Double,
        @SerialName("t1") val t1Us: Long,
        @SerialName("t2") val t2Us: Long,
    ) : ServerMessage

    @Serializable
    data class Timeline(
        val seq: Long,
        val rate: Double,
        val voices: List<VoiceTimeline>,
        val grid: GridTimeline?,
        val deck: DeckTimeline,
        val test: TestTimeline?,
    ) : ServerMessage

    @Serializable
    data class Status(val output: Boolean) : ServerMessage

    data class LevelsUpdate(val levels: Levels) : ServerMessage

    data object Kicked : ServerMessage

    data object Revoked : ServerMessage
}

sealed interface ClientMessage {
    @Serializable
    data class Ping(val t0: Double) : ClientMessage

    @Serializable
    data class Hello(val name: String, val levels: Levels) : ClientMessage

    data class LevelsChanged(val levels: Levels) : ClientMessage

    @Serializable
    data class Stats(
        val offsetUs: Long,
        val uncertaintyUs: Long,
        val rttUs: Long,
        val outputLatencyMs: Double,
        val trimMs: Double,
        val state: String,
    ) : ClientMessage
}

object ProtocolCodec {
    private val json = Json {
        ignoreUnknownKeys = true
        explicitNulls = false
    }

    fun decode(text: String): ServerMessage? =
        try {
            val root = json.parseToJsonElement(text).jsonObject
            when ((root["type"] as? JsonPrimitive)?.contentOrNull) {
                "welcome" -> json.decodeFromJsonElement(ServerMessage.Welcome.serializer(), root)
                "pong" -> json.decodeFromJsonElement(ServerMessage.Pong.serializer(), root)
                "timeline" -> json.decodeFromJsonElement(ServerMessage.Timeline.serializer(), root)
                "status" -> json.decodeFromJsonElement(ServerMessage.Status.serializer(), root)
                "levels" -> ServerMessage.LevelsUpdate(json.decodeFromJsonElement(Levels.serializer(), root))
                "kicked" -> ServerMessage.Kicked
                "revoked" -> ServerMessage.Revoked
                else -> null
            }
        } catch (_: IllegalArgumentException) {
            null
        }

    fun encode(message: ClientMessage): String =
        when (message) {
            is ClientMessage.Ping -> tagged("ping", ClientMessage.Ping.serializer(), message)
            is ClientMessage.Hello -> tagged("hello", ClientMessage.Hello.serializer(), message)
            is ClientMessage.LevelsChanged -> tagged("levels", Levels.serializer(), message.levels)
            is ClientMessage.Stats -> tagged("stats", ClientMessage.Stats.serializer(), message)
        }.toString()

    private fun <T> tagged(type: String, serializer: KSerializer<T>, value: T): JsonObject =
        JsonObject(mapOf("type" to JsonPrimitive(type)) + json.encodeToJsonElement(serializer, value).jsonObject)
}
