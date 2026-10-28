@file:OptIn(ExperimentalCoroutinesApi::class)

package aria.monitor.core

import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.TestCoroutineScheduler
import kotlinx.coroutines.test.TestScope
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonObject
import java.util.concurrent.CopyOnWriteArrayList
import kotlin.math.roundToLong

const val LOCAL_START_MS = 1_000_000.0
const val HOST_AHEAD_MS = 12_000_000_000.0
const val FIRST_STREAM_FRAME = 7_000_000L
const val SHOW_TRACK = "1db4df96-26fe-4f49-a954-fcacd871e9c0"
const val WELCOME_JSON = """{"type":"welcome","listener":"L1","role":"listen","sampleRate":48000}"""
const val KICKED_JSON = """{"type":"kicked"}"""
const val REVOKED_JSON = """{"type":"revoked"}"""

val SHOW_PAIRING = Pairing("192.168.1.20", 5000, "phone", "secret")
val OTHER_PAIRING = Pairing("192.168.1.30", 5000, "phone", "other")

fun hostUsAtLocal(localMs: Double, aheadMs: Double = HOST_AHEAD_MS): Long = ((localMs + aheadMs) * 1000).roundToLong()

fun pongJson(t0: Double, hostUs: Long): String = """{"type":"pong","t0":$t0,"t1":$hostUs,"t2":$hostUs}"""

fun statusJson(output: Boolean): String = """{"type":"status","output":$output}"""

fun levelsJson(levels: Levels): String = typed("levels", Json.encodeToJsonElement(Levels.serializer(), levels))

fun timelineJson(timeline: ServerMessage.Timeline): String =
    typed("timeline", Json.encodeToJsonElement(ServerMessage.Timeline.serializer(), timeline))

private fun typed(type: String, body: JsonElement): String =
    JsonObject(mapOf("type" to JsonPrimitive(type)) + body.jsonObject).toString()

fun showTimeline(seq: Long, test: TestTimeline? = null, title: String = "Song"): ServerMessage.Timeline =
    ServerMessage.Timeline(
        seq = seq,
        rate = SAMPLE_RATE.toDouble(),
        voices = listOf(
            VoiceTimeline(
                id = 1,
                epoch = seq.toInt(),
                track = SHOW_TRACK,
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
        grid = null,
        deck = DeckTimeline(status = "Playing", title = title, next = null),
        test = test,
    )

class TestClock(private val scheduler: TestCoroutineScheduler) : MonotonicClock {
    override fun nowMs(): Double = LOCAL_START_MS + scheduler.currentTime
}

class FakeAuthenticator(private val clock: MonotonicClock) : Authenticator {
    class Call(val pairing: Pairing, val atMs: Double)

    val calls = CopyOnWriteArrayList<Call>()

    @Volatile
    var respond: (Int) -> AuthResult = { AuthResult.Ok("token-$it") }

    override suspend fun authenticate(pairing: Pairing): AuthResult {
        calls += Call(pairing, clock.nowMs())
        return respond(calls.size)
    }
}

class FakePcm : PcmSource {
    class Call(val token: String, val track: String, val fromFrame: Long)

    val calls = CopyOnWriteArrayList<Call>()

    @Volatile
    var respond: (String) -> PcmResult = { PcmResult.Ok(ByteArray(0), 0) }

    override suspend fun fetch(token: String, track: String, fromFrame: Long, frames: Int): PcmResult {
        calls += Call(token, track, fromFrame)
        return respond(token)
    }
}

class FakeStore : SettingsStore {
    @Volatile
    var settings: Settings = Settings.sanitized()

    @Volatile
    var pairing: Pairing? = null

    @Volatile
    var loadGate: CompletableDeferred<Unit>? = null

    val saved = CopyOnWriteArrayList<Settings>()
    val savedPairings = CopyOnWriteArrayList<Pairing?>()

    override suspend fun load(): Settings {
        loadGate?.await()
        return settings
    }

    override suspend fun save(settings: Settings) {
        saved += settings
        this.settings = settings
    }

    override suspend fun loadPairing(): Pairing? = pairing

    override suspend fun savePairing(pairing: Pairing?) {
        savedPairings += pairing
        this.pairing = pairing
    }
}

class FakeEngine(private val clock: MonotonicClock) : AudioEngine {
    val plans = CopyOnWriteArrayList<RenderPlan>()
    val retains = CopyOnWriteArrayList<Set<ChunkKey>>()

    @Volatile
    var starts = 0

    @Volatile
    var stops = 0

    @Volatile
    var grows = 0

    @Volatile
    override var xrunCount = 0

    @Volatile
    private var running = false

    @Volatile
    private var streamStartMs = 0.0

    @Volatile
    private var streamFirstFrame = FIRST_STREAM_FRAME

    @Volatile
    private var listener: (EngineEvent) -> Unit = {}

    override fun start(): Boolean {
        starts++
        running = true
        streamStartMs = clock.nowMs()
        streamFirstFrame = FIRST_STREAM_FRAME
        return true
    }

    override fun stop() {
        stops++
        running = false
    }

    override fun publish(plan: RenderPlan) {
        plans += plan
    }

    override fun sampleStamp(): OutputStamp? {
        if (!running) return null
        val now = clock.nowMs()
        return OutputStamp(streamFirstFrame + ((now - streamStartMs) * SAMPLE_RATE / 1000).roundToLong(), (now * 1e6).roundToLong())
    }

    override fun growBuffer() {
        grows++
    }

    override fun setEventListener(listener: (EngineEvent) -> Unit) {
        this.listener = listener
    }

    fun reopen() {
        streamStartMs = clock.nowMs()
        streamFirstFrame = 0L
        listener(EngineEvent.StreamReopened)
    }

    override fun write(key: ChunkKey, bytes: ByteArray, frames: Int): Boolean = true

    override fun markEmpty(key: ChunkKey) = Unit

    override fun retain(keys: Set<ChunkKey>) {
        retains += keys
    }
}

class SessionRig(scope: TestScope) {
    val clock = TestClock(scope.testScheduler)
    val transport = FakeTransport(clock)
    val authenticator = FakeAuthenticator(clock)
    val pcm = FakePcm()
    val engine = FakeEngine(clock)
    val store = FakeStore()
    val session = Session(scope.backgroundScope, clock, authenticator, transport, pcm, engine, store)

    val socket: FakeSocket get() = transport.sockets.last()

    fun at(virtualMs: Long): Double = LOCAL_START_MS + virtualMs
}
