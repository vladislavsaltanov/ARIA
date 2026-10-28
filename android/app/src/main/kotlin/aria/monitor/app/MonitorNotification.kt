package aria.monitor.app

import aria.monitor.R
import aria.monitor.app.ui.LinkStatus
import aria.monitor.app.ui.toUi
import aria.monitor.core.SessionState
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.graphics.drawable.Icon
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.transform

internal const val MONITOR_CHANNEL = "monitor"
internal const val MONITOR_NOTIFICATION_ID = 1

internal fun Flow<SessionState>.notificationUpdates(minIntervalMs: Long): Flow<LinkStatus> =
    map { it.toUi().status }
        .distinctUntilChanged()
        .transform {
            emit(it)
            delay(minIntervalMs)
        }

internal fun Context.createMonitorChannel() {
    getSystemService(NotificationManager::class.java)?.createNotificationChannel(
        NotificationChannel(MONITOR_CHANNEL, getString(R.string.notification_channel), NotificationManager.IMPORTANCE_LOW),
    )
}

internal fun Context.monitorNotification(status: LinkStatus, stop: Intent): Notification {
    val text = when (status) {
        LinkStatus.Offline -> getString(R.string.status_offline)
        LinkStatus.Syncing -> getString(R.string.status_syncing)
        is LinkStatus.Locked -> getString(R.string.status_locked, status.uncertaintyMs)
    }
    val open = PendingIntent.getActivity(
        this,
        0,
        Intent(this, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP),
        PendingIntent.FLAG_IMMUTABLE,
    )
    val stopAction = Notification.Action.Builder(
        Icon.createWithResource(this, android.R.drawable.ic_media_pause),
        getString(R.string.notification_stop),
        PendingIntent.getService(this, 0, stop, PendingIntent.FLAG_IMMUTABLE),
    ).build()
    return Notification.Builder(this, MONITOR_CHANNEL)
        .setSmallIcon(android.R.drawable.ic_media_play)
        .setContentTitle(getString(R.string.app_name))
        .setContentText(text)
        .setContentIntent(open)
        .setOngoing(true)
        .setOnlyAlertOnce(true)
        .setShowWhen(false)
        .setCategory(Notification.CATEGORY_SERVICE)
        .addAction(stopAction)
        .build()
}
