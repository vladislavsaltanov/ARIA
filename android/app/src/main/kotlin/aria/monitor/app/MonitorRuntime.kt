package aria.monitor.app

import aria.monitor.core.Pairing
import aria.monitor.core.Session
import aria.monitor.core.SettingsStore
import aria.monitor.engine.OboeAudioEngine
import android.content.Context
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.job
import kotlinx.coroutines.withTimeoutOrNull
import okhttp3.OkHttpClient

private const val QUIESCE_TIMEOUT_MS = 2000L

internal class MonitorRuntime(
    val session: Session,
    private val scope: CoroutineScope,
    private val release: () -> Unit,
) {
    private val lock = Any()
    private var closed = false

    @Volatile
    var sessionStarts = 0
        private set

    fun start(pairing: Pairing?) {
        synchronized(lock) {
            if (closed || (sessionStarts > 0 && pairing == null)) return
            sessionStarts++
            session.start(pairing)
        }
    }

    suspend fun shutdown() {
        synchronized(lock) { closed = true }
        withTimeoutOrNull(QUIESCE_TIMEOUT_MS) { session.stop().join() }
        val job = scope.coroutineContext.job
        job.cancel()
        withTimeoutOrNull(QUIESCE_TIMEOUT_MS) { job.join() }
        release()
    }

    companion object {
        fun create(context: Context): MonitorRuntime {
            val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
            val client = OkHttpClient()
            val host = ActiveHost()
            val engine = OboeAudioEngine()
            val settings = AppStores.settings(context)
            val store = object : SettingsStore by settings {
                override suspend fun savePairing(pairing: Pairing?) {
                    if (pairing == null) host.pairing = null
                    settings.savePairing(pairing)
                }
            }
            val session = Session(
                scope,
                SystemMonotonicClock(),
                HttpAuthenticator(client, host),
                OkHttpTransport(client),
                HttpPcmSource(client, host),
                engine,
                store,
            )
            return MonitorRuntime(session, scope) {
                engine.release()
                host.pairing = null
            }
        }
    }
}
