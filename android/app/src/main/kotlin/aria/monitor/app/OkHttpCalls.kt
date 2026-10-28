package aria.monitor.app

import aria.monitor.core.Pairing
import kotlinx.coroutines.suspendCancellableCoroutine
import okhttp3.Call
import okhttp3.Callback
import okhttp3.HttpUrl
import okhttp3.Response
import java.io.IOException
import kotlin.coroutines.resumeWithException

internal suspend fun <T> Call.await(read: (Response) -> T): T = suspendCancellableCoroutine { continuation ->
    continuation.invokeOnCancellation { cancel() }
    enqueue(
        object : Callback {
            override fun onFailure(call: Call, e: IOException) = continuation.resumeWithException(e)

            override fun onResponse(call: Call, response: Response) =
                continuation.resumeWith(runCatching { response.use(read) })
        },
    )
}

internal fun Pairing.url(vararg segments: String): HttpUrl.Builder =
    HttpUrl.Builder().scheme("http").host(host).port(port).apply { segments.forEach(::addPathSegment) }
