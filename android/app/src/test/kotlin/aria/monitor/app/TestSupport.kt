package aria.monitor.app

import aria.monitor.core.Pairing
import aria.monitor.core.SocketListener
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.withContext
import mockwebserver3.MockResponse
import mockwebserver3.MockWebServer
import mockwebserver3.RecordedRequest
import okio.Buffer
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit
import kotlin.time.Duration.Companion.seconds

internal fun realTest(body: suspend CoroutineScope.() -> Unit) =
    runTest(timeout = 30.seconds) { withContext(Dispatchers.Default, body) }

internal fun MockWebServer.pairing(identifier: String = "ana", password: String = "secret") =
    Pairing(hostName, port, identifier, password)

internal fun MockWebServer.nextRequest(): RecordedRequest =
    takeRequest(5, TimeUnit.SECONDS) ?: throw AssertionError("no request reached the server")

internal fun jsonResponse(code: Int, body: String): MockResponse =
    MockResponse.Builder().code(code).addHeader("Content-Type", "application/json").body(body).build()

internal fun pcmResponse(frames: String?, bytes: ByteArray, code: Int = 200): MockResponse {
    val builder = MockResponse.Builder().code(code).body(Buffer().write(bytes))
    if (frames != null) builder.addHeader("X-Aria-Frames", frames)
    return builder.build()
}

internal fun pcmBytes(frames: Int): ByteArray = ByteArray(frames * 4) { it.toByte() }

internal class RecordingListener : SocketListener {
    private val events = LinkedBlockingQueue<String>()

    override fun onOpen() {
        events.add("open")
    }

    override fun onText(text: String) {
        events.add("text:$text")
    }

    override fun onClosed(unauthorized: Boolean) {
        events.add("closed:$unauthorized")
    }

    fun next(): String = events.poll(5, TimeUnit.SECONDS) ?: "no event"

    fun nextWithin(millis: Long): String? = events.poll(millis, TimeUnit.MILLISECONDS)
}
