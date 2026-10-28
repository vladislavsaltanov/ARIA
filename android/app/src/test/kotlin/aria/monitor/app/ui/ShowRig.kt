@file:OptIn(ExperimentalCoroutinesApi::class)

package aria.monitor.app.ui

import aria.monitor.app.SessionSource
import aria.monitor.core.AudioEngine
import aria.monitor.core.AuthResult
import aria.monitor.core.Authenticator
import aria.monitor.core.ChunkKey
import aria.monitor.core.DeckTimeline
import aria.monitor.core.EngineEvent
import aria.monitor.core.GridTimeline
import aria.monitor.core.MonitorSocket
import aria.monitor.core.MonitorTransport
import aria.monitor.core.MonotonicClock
import aria.monitor.core.OutputStamp
import aria.monitor.core.Pairing
import aria.monitor.core.PcmResult
import aria.monitor.core.PcmSource
import aria.monitor.core.RenderPlan
import aria.monitor.core.ServerMessage
import aria.monitor.core.Session
import aria.monitor.core.SettingsStore
import aria.monitor.core.Settings
import aria.monitor.core.SocketListener
import aria.monitor.core.VoiceTimeline
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.test.TestCoroutineScheduler
import kotlinx.coroutines.test.TestScope
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.double
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.util.concurrent.CopyOnWriteArrayList
import kotlin.math.roundToLong

internal const val LOCAL_START_MS = 1_000_000.0
internal const val HOST_AHEAD_MS = 12_000_000_000.0
internal const val WELCOME_JSON = """{"type":"welcome","listener":"L1","role":"listen","sampleRate":48000}"""
internal const val KICKED_JSON = """{"type":"kicked"}"""
internal val SHOW_PAIRING = Pairing("192.168.1.20", 5000, "phone", "secret")

internal fun hostUsAtLocal(localMs: Double): Long = ((localMs + HOST_AHEAD_MS) * 1000).roundToLong()

internal fun timelineJson(timeline: ServerMessage.Timeline): String =
    JsonObject(
        mapOf("type" to JsonPrimitive("timeline")) +
            Json.encodeToJsonElement(ServerMessage.Timeline.serializer(), timeline).jsonObject,
    ).toString()

internal fun gridTimeline(seq: Long = 1, title: String? = "Song"): ServerMessage.Timeline =
    ServerMessage.Timeline(
        seq = seq,
        rate = 48000.0,
        voices = listOf(
            VoiceTimeline(
                id = 1,
                epoch = 1,
                track = "1db4df96-26fe-4f49-a954-fcacd871e9c0",
                kind = "backing",
                fallback = false,
                gainDb = 0.0,
                anchorUs = hostUsAtLocal(LOCAL_START_MS),
                anchorFrame = 0L,
                playing = true,
                endFrame = null,
                fade = null,
            ),
        ),
        grid = GridTimeline(voice = 1, bpm = 120.0, beatsPerBar = 4, firstBeatFrame = 0L),
        deck = DeckTimeline(status = "Playing", title = title, next = null),
        test = null,
    )

internal class TestClock(private val scheduler: TestCoroutineScheduler) : MonotonicClock {
    override fun nowMs(): Double = LOCAL_START_MS + scheduler.currentTime
}

internal class CountingAuthenticator : Authenticator {
    val calls = CopyOnWriteArrayList<Pairing>()

    override suspend fun authenticate(pairing: Pairing): AuthResult {
        calls += pairing
        return AuthResult.Ok("token-${calls.size}")
    }
}

internal class RecordingStore : SettingsStore {
    val saved = CopyOnWriteArrayList<Settings>()

    @Volatile
    var pairing: Pairing? = null

    override suspend fun load(): Settings = Settings.sanitized()

    override suspend fun save(settings: Settings) {
        saved += settings
    }

    override suspend fun loadPairing(): Pairing? = pairing

    override suspend fun savePairing(pairing: Pairing?) {
        this.pairing = pairing
    }
}

internal class AnsweringTransport(private val clock: MonotonicClock) : MonitorTransport {
    val sockets = CopyOnWriteArrayList<AnsweringSocket>()

    @Volatile
    var answersPings = true

    override fun open(pairing: Pairing, token: String, listener: SocketListener): MonitorSocket {
        val socket = AnsweringSocket(listener, clock) { answersPings }
        sockets += socket
        listener.onOpen()
        return socket
    }
}

internal class AnsweringSocket(
    private val listener: SocketListener,
    private val clock: MonotonicClock,
    private val answersPings: () -> Boolean,
) : MonitorSocket {
    val sent = CopyOnWriteArrayList<JsonObject>()

    override fun send(text: String) {
        val message = Json.parseToJsonElement(text).jsonObject
        sent += message
        if (answersPings() && message.getValue("type").jsonPrimitive.content == "ping") {
            val hostUs = hostUsAtLocal(clock.nowMs())
            listener.onText("""{"type":"pong","t0":${message.getValue("t0").jsonPrimitive.double},"t1":$hostUs,"t2":$hostUs}""")
        }
    }

    override fun close() = Unit

    fun text(json: String) = listener.onText(json)

    fun sent(type: String): List<JsonObject> = sent.filter { it.getValue("type").jsonPrimitive.content == type }
}

internal object SilentPcm : PcmSource {
    override suspend fun fetch(token: String, track: String, fromFrame: Long, frames: Int): PcmResult =
        PcmResult.Ok(ByteArray(0), 0)
}

internal class SilentEngine : AudioEngine {
    override val xrunCount = 0

    override fun start(): Boolean = true

    override fun stop() = Unit

    override fun publish(plan: RenderPlan) = Unit

    override fun sampleStamp(): OutputStamp? = null

    override fun growBuffer() = Unit

    override fun setEventListener(listener: (EngineEvent) -> Unit) = Unit

    override fun write(key: ChunkKey, bytes: ByteArray, frames: Int): Boolean = true

    override fun markEmpty(key: ChunkKey) = Unit

    override fun retain(keys: Set<ChunkKey>) = Unit
}

internal class HeldSessionSource(initial: Session? = null, private val journal: MutableList<String>? = null) : SessionSource {
    private val held = MutableStateFlow(initial)
    override val session: StateFlow<Session?> = held
    val starts = CopyOnWriteArrayList<Pairing?>()

    override fun start(pairing: Pairing?) {
        journal?.add("start:${pairing?.identifier}")
        starts += pairing
    }

    fun hold(session: Session?) {
        held.value = session
    }
}

internal class ShowRig(scope: TestScope) {
    val clock = TestClock(scope.testScheduler)
    val authenticator = CountingAuthenticator()
    val store = RecordingStore()
    val transport = AnsweringTransport(clock)
    val session = Session(scope.backgroundScope, clock, authenticator, transport, SilentPcm, SilentEngine(), store)

    val socket: AnsweringSocket get() = transport.sockets.last()
}
