package aria.monitor.app

import aria.monitor.core.Pairing
import aria.monitor.core.Session
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.ServiceConnection
import android.os.IBinder
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

data class ServiceStop(val serial: Int, val deliberate: Boolean)

internal class StopReports {
    private val latest = MutableStateFlow<ServiceStop?>(null)
    val stops: StateFlow<ServiceStop?> = latest.asStateFlow()

    @Synchronized
    fun report(deliberate: Boolean) {
        latest.value = ServiceStop((latest.value?.serial ?: 0) + 1, deliberate)
    }
}

interface SessionSource {
    val session: StateFlow<Session?>
    val stops: StateFlow<ServiceStop?>

    fun start(pairing: Pairing?): Boolean
}

internal class ServiceSessionSource(context: Context) : SessionSource {
    private val app = context.applicationContext
    private val held = MutableStateFlow<Session?>(null)
    override val session: StateFlow<Session?> = held.asStateFlow()
    override val stops: StateFlow<ServiceStop?> = AppStores.stopReports.stops
    private var bound = false
    private var follower: Job? = null

    private val connection = object : ServiceConnection {
        override fun onServiceConnected(name: ComponentName, service: IBinder) {
            follower?.cancel()
            val access = service as? MonitorService.Access ?: return
            follower = AppStores.scope.launch { access.session.collect { held.value = it } }
        }

        override fun onServiceDisconnected(name: ComponentName) {
            forget()
        }

        override fun onBindingDied(name: ComponentName) {
            forget()
            if (bound) {
                unbind()
                bind()
            }
        }
    }

    override fun start(pairing: Pairing?): Boolean = MonitorService.start(app, pairing)

    fun bind() {
        if (!bound) bound = app.bindService(Intent(app, MonitorService::class.java), connection, 0)
    }

    fun unbind() {
        if (bound) app.unbindService(connection)
        bound = false
        forget()
    }

    private fun forget() {
        follower?.cancel()
        follower = null
        held.value = null
    }
}
