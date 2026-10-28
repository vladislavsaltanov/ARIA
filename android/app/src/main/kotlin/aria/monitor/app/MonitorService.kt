package aria.monitor.app

import aria.monitor.app.ui.toUi
import aria.monitor.core.Pairing
import aria.monitor.core.Session
import android.app.NotificationManager
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Binder
import android.os.Build
import android.os.IBinder
import android.util.Log
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.MainScope
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch

private const val TAG = "AriaMonitor"
private const val TYPED_FOREGROUND_SDK = 29
private const val NOTIFICATION_MIN_INTERVAL_MS = 1000L
private const val EXTRA_HOST = "aria.monitor.extra.HOST"
private const val EXTRA_PORT = "aria.monitor.extra.PORT"
private const val EXTRA_IDENTIFIER = "aria.monitor.extra.IDENTIFIER"
private const val EXTRA_PASSWORD = "aria.monitor.extra.PASSWORD"
private const val NO_PORT = -1

private val teardown = CoroutineScope(SupervisorJob() + Dispatchers.Default)

class MonitorService : Service() {
    private lateinit var runtime: MonitorRuntime
    private lateinit var holds: SystemHolds
    private val ui = MainScope()
    private val access = Access()
    private var foreground = false

    inner class Access : Binder() {
        val session: Session get() = runtime.session

        internal val sessionStarts: Int get() = runtime.sessionStarts
    }

    override fun onCreate() {
        super.onCreate()
        runtime = MonitorRuntime.create(this)
        holds = SystemHolds(this).also { it.acquire() }
        createMonitorChannel()
        ui.launch {
            runtime.session.state.notificationUpdates(NOTIFICATION_MIN_INTERVAL_MS).collect { status ->
                if (foreground && notificationsGranted()) {
                    getSystemService(NotificationManager::class.java)
                        ?.notify(MONITOR_NOTIFICATION_ID, monitorNotification(status, stopIntent(this@MonitorService)))
                }
            }
        }
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_STOP || !enterForeground()) {
            stopNow()
            return START_NOT_STICKY
        }
        runtime.start(intent?.pairing())
        return START_STICKY
    }

    override fun onBind(intent: Intent): IBinder = access

    override fun onDestroy() {
        ui.cancel()
        stopNow()
        val runtime = runtime
        val holds = holds
        teardown.launch {
            runtime.shutdown()
            holds.release()
        }
        super.onDestroy()
    }

    private fun enterForeground(): Boolean {
        if (foreground) return true
        val notification = monitorNotification(runtime.session.state.value.toUi().status, stopIntent(this))
        try {
            if (Build.VERSION.SDK_INT >= TYPED_FOREGROUND_SDK) {
                startForeground(MONITOR_NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PLAYBACK)
            } else {
                startForeground(MONITOR_NOTIFICATION_ID, notification)
            }
        } catch (refused: IllegalStateException) {
            Log.w(TAG, "monitor foreground refused", refused)
            return false
        } catch (denied: SecurityException) {
            Log.w(TAG, "monitor foreground denied", denied)
            return false
        }
        foreground = true
        return true
    }

    private fun stopNow() {
        foreground = false
        stopForeground(STOP_FOREGROUND_REMOVE)
        stopSelf()
    }

    companion object {
        internal const val ACTION_STOP = "aria.monitor.action.STOP"

        fun start(context: Context, pairing: Pairing?): Boolean =
            try {
                context.startForegroundService(Intent(context, MonitorService::class.java).withPairing(pairing))
                true
            } catch (refused: IllegalStateException) {
                Log.w(TAG, "monitor service start refused", refused)
                false
            } catch (denied: SecurityException) {
                Log.w(TAG, "monitor service start denied", denied)
                false
            }

        internal fun stopIntent(context: Context): Intent =
            Intent(context, MonitorService::class.java).setAction(ACTION_STOP)

        internal fun Intent.withPairing(pairing: Pairing?): Intent =
            if (pairing == null) {
                this
            } else {
                putExtra(EXTRA_HOST, pairing.host)
                    .putExtra(EXTRA_PORT, pairing.port)
                    .putExtra(EXTRA_IDENTIFIER, pairing.identifier)
                    .putExtra(EXTRA_PASSWORD, pairing.password)
            }

        internal fun Intent.pairing(): Pairing? {
            val host = getStringExtra(EXTRA_HOST) ?: return null
            val port = getIntExtra(EXTRA_PORT, NO_PORT).takeIf { it != NO_PORT } ?: return null
            val identifier = getStringExtra(EXTRA_IDENTIFIER) ?: return null
            val password = getStringExtra(EXTRA_PASSWORD) ?: return null
            return Pairing(host, port, identifier, password)
        }
    }
}
