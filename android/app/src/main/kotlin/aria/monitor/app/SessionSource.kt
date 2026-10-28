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

interface SessionSource {
    val session: StateFlow<Session?>

    fun start(pairing: Pairing?): Boolean
}

internal class ServiceSessionSource(context: Context) : SessionSource {
    private val app = context.applicationContext
    private val held = MutableStateFlow<Session?>(null)
    override val session: StateFlow<Session?> = held.asStateFlow()
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
