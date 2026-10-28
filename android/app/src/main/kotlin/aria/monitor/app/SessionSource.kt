package aria.monitor.app

import aria.monitor.core.Pairing
import aria.monitor.core.Session
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.ServiceConnection
import android.os.IBinder
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

interface SessionSource {
    val session: StateFlow<Session?>

    fun start(pairing: Pairing?)
}

internal class ServiceSessionSource(context: Context) : SessionSource {
    private val app = context.applicationContext
    private val held = MutableStateFlow<Session?>(null)
    override val session: StateFlow<Session?> = held.asStateFlow()
    private var bound = false

    private val connection = object : ServiceConnection {
        override fun onServiceConnected(name: ComponentName, service: IBinder) {
            held.value = (service as? MonitorService.Access)?.session
        }

        override fun onServiceDisconnected(name: ComponentName) {
            held.value = null
        }

        override fun onBindingDied(name: ComponentName) {
            held.value = null
            if (bound) {
                unbind()
                bind()
            }
        }
    }

    override fun start(pairing: Pairing?) {
        MonitorService.start(app, pairing)
    }

    fun bind() {
        if (!bound) bound = app.bindService(Intent(app, MonitorService::class.java), connection, 0)
    }

    fun unbind() {
        if (bound) app.unbindService(connection)
        bound = false
        held.value = null
    }
}
