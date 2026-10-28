package aria.monitor.app

import aria.monitor.core.PcmResult
import aria.monitor.core.PcmSource
import kotlinx.coroutines.CancellationException
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import java.util.concurrent.TimeUnit

internal const val TRANSPORT_ERROR = -1
private const val DEFAULT_CALL_TIMEOUT_MS = 10_000L
private const val BYTES_PER_FRAME = 4
private const val FRAMES_HEADER = "X-Aria-Frames"

internal class HttpPcmSource(
    client: OkHttpClient,
    private val activeHost: ActiveHost,
    callTimeoutMs: Long = DEFAULT_CALL_TIMEOUT_MS,
) : PcmSource {
    private val client = client.newBuilder().callTimeout(callTimeoutMs, TimeUnit.MILLISECONDS).build()

    override suspend fun fetch(token: String, track: String, fromFrame: Long, frames: Int): PcmResult {
        val host = activeHost.pairing ?: return PcmResult.Failed(TRANSPORT_ERROR)
        return try {
            val url = host.url("monitor", "pcm")
                .addQueryParameter("token", token)
                .addQueryParameter("track", track)
                .addQueryParameter("from", fromFrame.toString())
                .addQueryParameter("frames", frames.toString())
                .build()
            client.newCall(Request.Builder().url(url).build()).await { chunk(it, frames) }
        } catch (cancelled: CancellationException) {
            throw cancelled
        } catch (failure: Exception) {
            PcmResult.Failed(TRANSPORT_ERROR)
        }
    }

    private fun chunk(response: Response, requestedFrames: Int): PcmResult {
        if (!response.isSuccessful) return PcmResult.Failed(response.code)
        val frames = response.header(FRAMES_HEADER)?.toIntOrNull()?.takeIf { it in 0..requestedFrames }
            ?: return PcmResult.Failed(TRANSPORT_ERROR)
        val bytes = response.peekBody(requestedFrames.toLong() * BYTES_PER_FRAME + 1).bytes()
        return if (bytes.size == frames * BYTES_PER_FRAME) PcmResult.Ok(bytes, frames) else PcmResult.Failed(TRANSPORT_ERROR)
    }
}
