package aria.monitor.app

import aria.monitor.core.PcmResult
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withTimeout
import mockwebserver3.MockResponse
import mockwebserver3.MockWebServer
import okhttp3.OkHttpClient
import java.util.concurrent.TimeUnit
import kotlin.test.AfterTest
import kotlin.test.BeforeTest
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertIs

class HttpPcmSourceTest {
    private val server = MockWebServer()
    private val client = OkHttpClient()
    private val activeHost = ActiveHost()
    private val track = "5f2c1c0e-8f0e-4c53-9d6c-2d1b6f6e7a11"

    @BeforeTest
    fun start() {
        server.start()
        activeHost.pairing = server.pairing()
    }

    @AfterTest
    fun stop() = server.close()

    private fun source(callTimeoutMs: Long = 5000) = HttpPcmSource(client, activeHost, callTimeoutMs)

    private suspend fun fetch(source: HttpPcmSource = source()) = source.fetch("tok&en=1", track, 96000, 96000)

    @Test
    fun pcmMapsFramesHeaderAndBytes() = realTest {
        val bytes = pcmBytes(3)
        server.enqueue(pcmResponse("3", bytes))

        val result = assertIs<PcmResult.Ok>(fetch())

        assertEquals(3, result.frames)
        assertContentEquals(bytes, result.bytes)
        val url = server.nextRequest().url
        assertEquals("/monitor/pcm", url.encodedPath)
        assertEquals("tok&en=1", url.queryParameter("token"))
        assertEquals(track, url.queryParameter("track"))
        assertEquals("96000", url.queryParameter("from"))
        assertEquals("96000", url.queryParameter("frames"))
    }

    @Test
    fun shortFinalChunkKeepsItsOwnFrameCount() = realTest {
        server.enqueue(pcmResponse("2", pcmBytes(2)))
        server.enqueue(pcmResponse("0", ByteArray(0)))

        assertEquals(2, assertIs<PcmResult.Ok>(fetch()).frames)
        val empty = assertIs<PcmResult.Ok>(fetch())
        assertEquals(0, empty.frames)
        assertEquals(0, empty.bytes.size)
    }

    @Test
    fun pcm503And422MapToFailedStatus() = realTest {
        listOf(503, 422).forEach { server.enqueue(MockResponse.Builder().code(it).build()) }

        assertEquals(503, assertIs<PcmResult.Failed>(fetch()).status)
        assertEquals(422, assertIs<PcmResult.Failed>(fetch()).status)
    }

    @Test
    fun unauthorizedAndForbiddenStayDistinguishable() = realTest {
        listOf(401, 403, 404, 400).forEach { server.enqueue(MockResponse.Builder().code(it).build()) }

        assertEquals(401, assertIs<PcmResult.Failed>(fetch()).status)
        assertEquals(403, assertIs<PcmResult.Failed>(fetch()).status)
        assertEquals(404, assertIs<PcmResult.Failed>(fetch()).status)
        assertEquals(400, assertIs<PcmResult.Failed>(fetch()).status)
    }

    @Test
    fun malformedFramesHeaderIsTransportFailure() = realTest {
        val headers = listOf(null, "", "abc", "-1", "1.5", "99999999999")
        headers.forEach { server.enqueue(pcmResponse(it, pcmBytes(1))) }

        headers.forEach { assertEquals(TRANSPORT_ERROR, assertIs<PcmResult.Failed>(fetch(), it).status, it) }
    }

    @Test
    fun missingFramesHeaderWithEmptyBodyIsTransportFailure() = realTest {
        server.enqueue(pcmResponse(null, ByteArray(0)))

        assertEquals(TRANSPORT_ERROR, assertIs<PcmResult.Failed>(fetch()).status)
    }

    @Test
    fun overflowingFramesHeaderIsTransportFailure() = realTest {
        val headers = listOf("1073741824", "-1073741824", "2147483647")
        headers.forEach { server.enqueue(pcmResponse(it, ByteArray(0))) }

        headers.forEach { assertEquals(TRANSPORT_ERROR, assertIs<PcmResult.Failed>(fetch(), it).status, it) }
    }

    @Test
    fun bodySizeMismatchIsTransportFailure() = realTest {
        server.enqueue(pcmResponse("10", ByteArray(3)))
        server.enqueue(pcmResponse("1", pcmBytes(2)))
        server.enqueue(pcmResponse("0", pcmBytes(1)))

        repeat(3) { assertEquals(TRANSPORT_ERROR, assertIs<PcmResult.Failed>(fetch()).status) }
    }

    @Test
    fun moreFramesThanRequestedIsTransportFailure() = realTest {
        server.enqueue(pcmResponse("96001", pcmBytes(96001)))

        assertEquals(TRANSPORT_ERROR, assertIs<PcmResult.Failed>(fetch()).status)
    }

    @Test
    fun connectionRefusedIsTransportFailure() = realTest {
        server.close()

        assertEquals(TRANSPORT_ERROR, assertIs<PcmResult.Failed>(fetch()).status)
    }

    @Test
    fun timeoutIsTransportFailureNotCancellation() = realTest {
        server.enqueue(pcmResponse("1", pcmBytes(1)).newBuilder().headersDelay(3, TimeUnit.SECONDS).build())

        assertEquals(TRANSPORT_ERROR, assertIs<PcmResult.Failed>(fetch(source(callTimeoutMs = 200))).status)
    }

    @Test
    fun invalidHostIsTransportFailure() = realTest {
        activeHost.pairing = server.pairing().copy(host = "bad host")

        assertEquals(TRANSPORT_ERROR, assertIs<PcmResult.Failed>(fetch()).status)
    }

    @Test
    fun withoutAnAuthenticatedHostNothingIsSent() = realTest {
        activeHost.pairing = null

        assertEquals(TRANSPORT_ERROR, assertIs<PcmResult.Failed>(fetch()).status)
        assertEquals(0, server.requestCount)
    }

    @Test
    fun requestsGoToTheAuthenticatedHost() = realTest {
        val authenticated = MockWebServer()
        authenticated.start()
        try {
            activeHost.pairing = null
            authenticated.enqueue(jsonResponse(200, """{"token":"tok"}"""))
            authenticated.enqueue(pcmResponse("1", pcmBytes(1)))

            HttpAuthenticator(client, activeHost).authenticate(authenticated.pairing())
            assertIs<PcmResult.Ok>(fetch())

            assertEquals("/auth", authenticated.nextRequest().url.encodedPath)
            assertEquals("/monitor/pcm", authenticated.nextRequest().url.encodedPath)
            assertEquals(0, server.requestCount)
        } finally {
            authenticated.close()
        }
    }

    @Test
    fun cancellationCancelsTheCall() = realTest {
        server.enqueue(pcmResponse("1", pcmBytes(1)).newBuilder().headersDelay(20, TimeUnit.SECONDS).build())
        var returned = false
        val job = launch {
            fetch(source(callTimeoutMs = 30_000))
            returned = true
        }
        server.nextRequest()
        assertEquals(1, client.dispatcher.runningCallsCount())

        job.cancelAndJoin()

        assertFalse(returned)
        withTimeout(3000) { while (client.dispatcher.runningCallsCount() > 0) delay(20) }
    }

    @Test
    fun fetchDoesNotBlockItsLane() = realTest {
        server.enqueue(pcmResponse("1", pcmBytes(1)).newBuilder().headersDelay(1500, TimeUnit.MILLISECONDS).build())
        val lane = Dispatchers.Default.limitedParallelism(1)
        val sibling = CompletableDeferred<Unit>()

        val fetching = async(lane) { fetch() }
        launch(lane) { sibling.complete(Unit) }

        withTimeout(1000) { sibling.await() }
        assertFalse(fetching.isCompleted)
        assertIs<PcmResult.Ok>(fetching.await())
    }
}
