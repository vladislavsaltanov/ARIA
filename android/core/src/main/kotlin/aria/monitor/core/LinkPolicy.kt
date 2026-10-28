package aria.monitor.core

import kotlin.coroutines.cancellation.CancellationException

object LinkPolicy {
    const val RECONNECT_MIN_MS = 500L
    const val RECONNECT_MAX_MS = 5000L
    const val STALE_MS = 2000L

    fun nextDelay(delayMs: Long): Long = minOf(delayMs * 2, RECONNECT_MAX_MS)

    fun isStale(connected: Boolean, nowMs: Double, lastMessageMs: Double): Boolean =
        connected && nowMs - lastMessageMs > STALE_MS
}

sealed interface AuthResult {
    data class Ok(val token: String) : AuthResult
    data object Denied : AuthResult
    data object Offline : AuthResult
}

interface Authenticator {
    suspend fun authenticate(pairing: Pairing): AuthResult
}

sealed interface SignInOutcome {
    data class Ok(val token: String) : SignInOutcome
    data object NeedLogin : SignInOutcome
    data object DeniedLogin : SignInOutcome
    data object Retry : SignInOutcome
}

suspend fun signIn(pairing: Pairing?, authenticator: Authenticator): SignInOutcome {
    if (pairing == null) return SignInOutcome.NeedLogin
    val result = try {
        authenticator.authenticate(pairing)
    } catch (cancelled: CancellationException) {
        throw cancelled
    } catch (failure: Exception) {
        return SignInOutcome.Retry
    }
    return when (result) {
        is AuthResult.Ok -> SignInOutcome.Ok(result.token)
        AuthResult.Denied -> SignInOutcome.DeniedLogin
        AuthResult.Offline -> SignInOutcome.Retry
    }
}
