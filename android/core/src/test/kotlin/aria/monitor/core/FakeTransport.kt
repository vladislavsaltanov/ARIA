package aria.monitor.core

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.double
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.util.concurrent.CopyOnWriteArrayList

class FakeTransport(private val clock: MonotonicClock) : MonitorTransport {
    val sockets = CopyOnWriteArrayList<FakeSocket>()

    @Volatile
    var autoOpen = false

    @Volatile
    var pongRttMs: Double? = 0.0

    @Volatile
    var hostAheadMs = HOST_AHEAD_MS

    override fun open(pairing: Pairing, token: String, listener: SocketListener): MonitorSocket {
        val socket = FakeSocket(pairing, token, listener, clock, clock.nowMs(), pongRttMs, hostAheadMs)
        sockets += socket
        if (autoOpen) listener.onOpen()
        return socket
    }
}

class FakeSocket(
    val pairing: Pairing,
    val token: String,
    private val listener: SocketListener,
    private val clock: MonotonicClock,
    val openedAtMs: Double,
    @Volatile var pongRttMs: Double?,
    private val hostAheadMs: Double,
) : MonitorSocket {
    class Frame(val atMs: Double, val json: JsonObject) {
        val type: String get() = json.getValue("type").jsonPrimitive.content
    }

    val frames = CopyOnWriteArrayList<Frame>()

    @Volatile
    var closedAtMs: Double? = null

    override fun send(text: String) {
        val frame = Frame(clock.nowMs(), Json.parseToJsonElement(text).jsonObject)
        frames += frame
        val rtt = pongRttMs
        if (rtt != null && frame.type == "ping") {
            listener.onText(pongJson(frame.json.getValue("t0").jsonPrimitive.double - rtt, hostUsAtLocal(frame.atMs, hostAheadMs)))
        }
    }

    override fun close() {
        if (closedAtMs == null) closedAtMs = clock.nowMs()
    }

    fun open() = listener.onOpen()

    fun text(json: String) = listener.onText(json)

    fun closeFromServer(unauthorized: Boolean = false) = listener.onClosed(unauthorized)

    fun sent(type: String): List<Frame> = frames.filter { it.type == type }
}
