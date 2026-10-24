package aria.monitor.core

import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.advanceUntilIdle
import kotlinx.coroutines.test.runTest
import kotlin.coroutines.cancellation.CancellationException
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertTrue

private val PAIRING = Pairing("192.168.1.10", 5000, "abc", "xyz")

private fun authenticator(block: suspend (Pairing) -> AuthResult) = object : Authenticator {
    override suspend fun authenticate(pairing: Pairing): AuthResult = block(pairing)
}

class LinkPolicyTest {
    @Test
    fun constantsMatchTheWebClient() {
        assertEquals(500L, LinkPolicy.RECONNECT_MIN_MS)
        assertEquals(5000L, LinkPolicy.RECONNECT_MAX_MS)
        assertEquals(2000L, LinkPolicy.STALE_MS)
    }

    @Test
    fun nextDelayDoublesUpTo5000() {
        val delays = generateSequence(LinkPolicy.RECONNECT_MIN_MS) { LinkPolicy.nextDelay(it) }.take(6).toList()
        assertEquals(listOf(500L, 1000L, 2000L, 4000L, 5000L, 5000L), delays)
    }

    @Test
    fun staleOnlyWhenConnectedAndSilentOver2000ms() {
        assertFalse(LinkPolicy.isStale(connected = true, nowMs = 3000.0, lastMessageMs = 1000.0))
        assertTrue(LinkPolicy.isStale(connected = true, nowMs = 3001.0, lastMessageMs = 1000.0))
        assertFalse(LinkPolicy.isStale(connected = false, nowMs = 60_000.0, lastMessageMs = 0.0))
        assertFalse(LinkPolicy.isStale(connected = true, nowMs = 1000.0, lastMessageMs = 1000.0))
    }

    @Test
    fun signInNoPairingNeedsLogin() = runTest {
        val outcome = signIn(null, authenticator { error("must not be called") })
        assertEquals(SignInOutcome.NeedLogin, outcome)
    }

    @Test
    fun signInOfflineRetries() = runTest {
        assertEquals(SignInOutcome.Retry, signIn(PAIRING, authenticator { AuthResult.Offline }))
    }

    @Test
    fun signInDeniedNeedsLoginWithError() = runTest {
        assertEquals(SignInOutcome.DeniedLogin, signIn(PAIRING, authenticator { AuthResult.Denied }))
    }

    @Test
    fun signInOkCarriesToken() = runTest {
        var seen: Pairing? = null
        val outcome = signIn(PAIRING, authenticator { seen = it; AuthResult.Ok("tok") })
        assertEquals(SignInOutcome.Ok("tok"), outcome)
        assertEquals(PAIRING, seen)
    }

    @Test
    fun signInAuthenticatorThrowingRetries() = runTest {
        assertEquals(SignInOutcome.Retry, signIn(PAIRING, authenticator { throw java.io.IOException("boom") }))
        assertEquals(SignInOutcome.Retry, signIn(PAIRING, authenticator { throw IllegalStateException("boom") }))
    }

    @Test
    fun signInPropagatesCancellation() = runTest {
        assertFailsWith<CancellationException> {
            signIn(PAIRING, authenticator { throw CancellationException("cancelled") })
        }
    }

    @OptIn(ExperimentalCoroutinesApi::class)
    @Test
    fun signInCancelledWhileAuthenticatingDoesNotRetry() = runTest {
        val started = CompletableDeferred<Unit>()
        var outcome: SignInOutcome? = null
        val job = launch {
            outcome = signIn(PAIRING, authenticator { started.complete(Unit); CompletableDeferred<AuthResult>().await() })
        }
        advanceUntilIdle()
        assertTrue(started.isCompleted)
        job.cancel()
        advanceUntilIdle()
        assertTrue(job.isCancelled)
        assertEquals(null, outcome)
    }
}
