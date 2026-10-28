package aria.monitor.app

import aria.monitor.core.AuthResult
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withTimeout
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import mockwebserver3.MockResponse
import mockwebserver3.MockWebServer
import okhttp3.OkHttpClient
import java.util.concurrent.TimeUnit
import kotlin.test.AfterTest
import kotlin.test.BeforeTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

class HttpAuthenticatorTest {
    private val server = MockWebServer()
    private val client = OkHttpClient()
    private val activeHost = ActiveHost()

    @BeforeTest
    fun start() = server.start()

    @AfterTest
    fun stop() = server.close()

    private fun authenticator(callTimeoutMs: Long = 5000) = HttpAuthenticator(client, activeHost, callTimeoutMs)

    @Test
    fun authOkReturnsToken() = realTest {
        server.enqueue(jsonResponse(200, """{"token":"abc123","role":"listen"}"""))

        val result = authenticator().authenticate(server.pairing())

        assertEquals(AuthResult.Ok("abc123"), result)
        val request = server.nextRequest()
        assertEquals("POST", request.method)
        assertEquals("/auth", request.url.encodedPath)
        assertTrue(request.headers["Content-Type"].orEmpty().startsWith("application/json"))
        assertEquals("""{"identifier":"ana","password":"secret"}""", request.body?.utf8())
    }

    @Test
    fun credentialsAreJsonEscaped() = realTest {
        server.enqueue(jsonResponse(200, """{"token":"t"}"""))

        authenticator().authenticate(server.pairing(identifier = "a\"b", password = "p\\q\n"))

        val body = Json.parseToJsonElement(server.nextRequest().body!!.utf8()).jsonObject
        assertEquals("a\"b", body.getValue("identifier").jsonPrimitive.content)
        assertEquals("p\\q\n", body.getValue("password").jsonPrimitive.content)
    }

    @Test
    fun authUnauthorizedIsDenied() = realTest {
        server.enqueue(jsonResponse(401, """{"error":"invalid-credentials"}"""))

        assertEquals(AuthResult.Denied, authenticator().authenticate(server.pairing()))
    }

    @Test
    fun authForbiddenIsDenied() = realTest {
        server.enqueue(MockResponse.Builder().code(403).build())

        assertEquals(AuthResult.Denied, authenticator().authenticate(server.pairing()))
    }

    @Test
    fun serverErrorIsOffline() = realTest {
        server.enqueue(MockResponse.Builder().code(500).build())
        server.enqueue(MockResponse.Builder().code(404).build())

        assertEquals(AuthResult.Offline, authenticator().authenticate(server.pairing()))
        assertEquals(AuthResult.Offline, authenticator().authenticate(server.pairing()))
    }

    @Test
    fun unusableOkBodyIsOffline() = realTest {
        val bodies = listOf("""{"role":"listen"}""", """{"token":""}""", """{"token":7}""", "[1,2]", "", "<html>captive portal</html>")
        bodies.forEach { server.enqueue(jsonResponse(200, it)) }

        bodies.forEach { assertEquals(AuthResult.Offline, authenticator().authenticate(server.pairing()), it) }
    }

    @Test
    fun authConnectionRefusedIsOffline() = realTest {
        val pairing = server.pairing()
        server.close()

        assertEquals(AuthResult.Offline, authenticator().authenticate(pairing))
    }

    @Test
    fun invalidHostIsOffline() = realTest {
        val pairing = server.pairing().copy(host = "bad host")

        assertEquals(AuthResult.Offline, authenticator().authenticate(pairing))
    }

    @Test
    fun authTimeoutIsOfflineNotCancellation() = realTest {
        server.enqueue(MockResponse.Builder().code(200).body("""{"token":"late"}""").headersDelay(3, TimeUnit.SECONDS).build())

        assertEquals(AuthResult.Offline, authenticator(callTimeoutMs = 200).authenticate(server.pairing()))
    }

    @Test
    fun authCancellationCancelsTheCall() = realTest {
        server.enqueue(MockResponse.Builder().code(200).body("""{"token":"late"}""").headersDelay(20, TimeUnit.SECONDS).build())
        var returned = false
        val auth = launch {
            authenticator(callTimeoutMs = 30_000).authenticate(server.pairing())
            returned = true
        }
        server.nextRequest()
        assertEquals(1, client.dispatcher.runningCallsCount())

        auth.cancelAndJoin()

        assertFalse(returned)
        withTimeout(3000) { while (client.dispatcher.runningCallsCount() > 0) delay(20) }
    }

    @Test
    fun authenticationDoesNotBlockItsLane() = realTest {
        server.enqueue(MockResponse.Builder().code(200).body("""{"token":"t"}""").headersDelay(3000, TimeUnit.MILLISECONDS).build())
        val lane = Dispatchers.Default.limitedParallelism(1)
        val sibling = CompletableDeferred<Unit>()

        val auth = async(lane) { authenticator().authenticate(server.pairing()) }
        launch(lane) { sibling.complete(Unit) }

        withTimeout(1000) { sibling.await() }
        assertFalse(auth.isCompleted)
        assertEquals(AuthResult.Ok("t"), auth.await())
    }

    @Test
    fun successfulAuthPublishesTheHost() = realTest {
        server.enqueue(jsonResponse(200, """{"token":"t"}"""))
        val pairing = server.pairing()

        authenticator().authenticate(pairing)

        assertEquals(pairing, activeHost.pairing)
    }

    @Test
    fun failedAuthLeavesTheHostUntouched() = realTest {
        server.enqueue(jsonResponse(401, "{}"))
        server.enqueue(MockResponse.Builder().code(500).build())

        authenticator().authenticate(server.pairing())
        authenticator().authenticate(server.pairing())

        assertNull(activeHost.pairing)
    }
}
