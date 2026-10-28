package aria.monitor.app

import aria.monitor.core.AuthResult
import aria.monitor.core.Authenticator
import aria.monitor.core.Pairing
import kotlinx.coroutines.CancellationException
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody
import okhttp3.RequestBody.Companion.toRequestBody
import okhttp3.Response
import java.util.concurrent.TimeUnit

private const val CONNECT_TIMEOUT_MS = 3000L
private const val DEFAULT_CALL_TIMEOUT_MS = 6000L
private const val MAX_BODY_BYTES = 4096L
private const val STATUS_UNAUTHORIZED = 401
private const val STATUS_FORBIDDEN = 403

internal class HttpAuthenticator(
    client: OkHttpClient,
    private val activeHost: ActiveHost,
    callTimeoutMs: Long = DEFAULT_CALL_TIMEOUT_MS,
) : Authenticator {
    private val client = client.newBuilder()
        .connectTimeout(CONNECT_TIMEOUT_MS, TimeUnit.MILLISECONDS)
        .callTimeout(callTimeoutMs, TimeUnit.MILLISECONDS)
        .build()

    override suspend fun authenticate(pairing: Pairing): AuthResult {
        val result = try {
            val request = Request.Builder().url(pairing.url("auth").build()).post(credentials(pairing)).build()
            client.newCall(request).await(::outcome)
        } catch (cancelled: CancellationException) {
            throw cancelled
        } catch (failure: Exception) {
            AuthResult.Offline
        }
        if (result is AuthResult.Ok) activeHost.pairing = pairing
        return result
    }

    private fun credentials(pairing: Pairing): RequestBody =
        buildJsonObject {
            put("identifier", pairing.identifier)
            put("password", pairing.password)
        }.toString().toRequestBody("application/json".toMediaType())

    private fun outcome(response: Response): AuthResult = when {
        response.code == STATUS_UNAUTHORIZED || response.code == STATUS_FORBIDDEN -> AuthResult.Denied
        !response.isSuccessful -> AuthResult.Offline
        else -> token(response.peekBody(MAX_BODY_BYTES).string())?.let(AuthResult::Ok) ?: AuthResult.Offline
    }

    private fun token(body: String): String? {
        val token = Json.parseToJsonElement(body).jsonObject["token"]?.jsonPrimitive ?: return null
        return if (token.isString) token.content.takeIf { it.isNotEmpty() } else null
    }
}
