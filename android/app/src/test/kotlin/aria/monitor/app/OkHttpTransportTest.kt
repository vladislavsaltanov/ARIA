package aria.monitor.app

import aria.monitor.core.MonitorSocket
import mockwebserver3.MockResponse
import mockwebserver3.MockWebServer
import okhttp3.OkHttpClient
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import okio.ByteString.Companion.encodeUtf8
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit
import kotlin.test.AfterTest
import kotlin.test.BeforeTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue

class OkHttpTransportTest {
    private val server = MockWebServer()
    private val transport = OkHttpTransport(OkHttpClient())
    private val sockets = mutableListOf<MonitorSocket>()

    @BeforeTest
    fun start() = server.start()

    @AfterTest
    fun stop() {
        sockets.forEach { it.close() }
        server.close()
    }

    private fun open(listener: RecordingListener, token: String = "tok", transport: OkHttpTransport = this.transport): MonitorSocket =
        transport.open(server.pairing(), token, listener).also { sockets += it }

    private fun upgrade(onMessage: (String) -> Unit = {}, onOpen: (WebSocket) -> Unit = {}): MockResponse =
        MockResponse.Builder().webSocketUpgrade(object : WebSocketListener() {
            override fun onOpen(webSocket: WebSocket, response: Response) = onOpen(webSocket)

            override fun onMessage(webSocket: WebSocket, text: String) = onMessage(text)

            override fun onClosing(webSocket: WebSocket, code: Int, reason: String) {
                webSocket.close(code, null)
            }
        }).build()

    @Test
    fun socketDeliversTextAndReportsUnauthorizedClose() {
        server.enqueue(upgrade(onOpen = { it.send("tick") }))
        server.enqueue(MockResponse.Builder().code(401).build())
        val first = RecordingListener()
        val second = RecordingListener()

        open(first)
        assertEquals("open", first.next())
        assertEquals("text:tick", first.next())

        open(second)
        assertEquals("closed:true", second.next())
        assertNull(second.nextWithin(300))
    }

    @Test
    fun upgradeRequestTargetsMonitorSocketWithTheToken() {
        server.enqueue(upgrade())
        val listener = RecordingListener()

        open(listener, token = "a b&c=d")
        assertEquals("open", listener.next())

        val url = server.nextRequest().url
        assertEquals("/monitor/ws", url.encodedPath)
        assertEquals("a b&c=d", url.queryParameter("token"))
    }

    @Test
    fun sentTextReachesTheHost() {
        val received = LinkedBlockingQueue<String>()
        server.enqueue(upgrade(onMessage = { received.add(it) }))
        val listener = RecordingListener()

        val socket = open(listener)
        assertEquals("open", listener.next())
        socket.send("hello")

        assertEquals("hello", received.poll(5, TimeUnit.SECONDS))
    }

    @Test
    fun failuresOtherThanUnauthorizedAreNotReportedAsUnauthorized() {
        server.enqueue(MockResponse.Builder().code(500).build())
        server.enqueue(MockResponse.Builder().code(403).build())
        val refused = server.pairing()
        val listeners = List(2) { RecordingListener().also { open(it) } }

        listeners.forEach {
            assertEquals("closed:false", it.next())
            assertNull(it.nextWithin(300))
        }

        server.close()
        val dead = RecordingListener()
        transport.open(refused, "tok", dead)
        assertEquals("closed:false", dead.next())
        assertNull(dead.nextWithin(300))
    }

    @Test
    fun hostClosingTheSocketReportsOneClosedAfterTheText() {
        server.enqueue(
            upgrade(
                onOpen = {
                    it.send("a")
                    it.send("b")
                    it.close(1000, null)
                },
            ),
        )
        val listener = RecordingListener()

        open(listener)

        assertEquals(listOf("open", "text:a", "text:b", "closed:false"), List(4) { listener.next() })
        assertNull(listener.nextWithin(300))
    }

    @Test
    fun closingLocallyReportsOneClosed() {
        server.enqueue(upgrade())
        val listener = RecordingListener()

        val socket = open(listener)
        assertEquals("open", listener.next())
        socket.close()

        assertEquals("closed:false", listener.next())
        assertNull(listener.nextWithin(300))
    }

    @Test
    fun binaryFramesAreIgnored() {
        server.enqueue(
            upgrade(
                onOpen = {
                    it.send("raw".encodeUtf8())
                    it.send("text")
                },
            ),
        )
        val listener = RecordingListener()

        open(listener)

        assertEquals(listOf("open", "text:text"), List(2) { listener.next() })
    }

    @Test
    fun invalidHostReportsClosedWithoutThrowing() {
        val listener = RecordingListener()

        transport.open(server.pairing().copy(host = "bad host"), "tok", listener)

        assertEquals("closed:false", listener.next())
        assertNull(listener.nextWithin(300))
    }

    @Test
    fun stalledUpgradeIsReportedClosedOnceWithinTheHandshakeTimeout() {
        server.enqueue(upgrade().newBuilder().headersDelay(3, TimeUnit.SECONDS).build())
        val listener = RecordingListener()
        val startedNanos = System.nanoTime()

        open(listener, transport = OkHttpTransport(OkHttpClient(), handshakeTimeoutMs = 300))

        assertEquals("closed:false", listener.next())
        assertTrue(TimeUnit.NANOSECONDS.toMillis(System.nanoTime() - startedNanos) < 2000)
        assertNull(listener.nextWithin(500))
    }

    @Test
    fun establishedIdleSocketOutlivesTheCallAndReadTimeouts() {
        val hostSockets = LinkedBlockingQueue<WebSocket>()
        server.enqueue(upgrade(onOpen = { hostSockets.add(it) }))
        val listener = RecordingListener()
        val impatient = OkHttpTransport(OkHttpClient.Builder().readTimeout(300, TimeUnit.MILLISECONDS).build(), handshakeTimeoutMs = 300)

        open(listener, transport = impatient)
        assertEquals("open", listener.next())
        val host = checkNotNull(hostSockets.poll(5, TimeUnit.SECONDS))

        assertNull(listener.nextWithin(1200))
        host.send("late")

        assertEquals("text:late", listener.next())
    }

    @Test
    fun socketClientNeverTimesOutOrPings() {
        val base = OkHttpClient.Builder().readTimeout(5, TimeUnit.SECONDS).pingInterval(1, TimeUnit.SECONDS).build()

        val derived = base.forMonitorSocket()

        assertEquals(0, derived.readTimeoutMillis)
        assertEquals(0, derived.pingIntervalMillis)
        assertEquals(8000, derived.callTimeoutMillis)
    }
}
