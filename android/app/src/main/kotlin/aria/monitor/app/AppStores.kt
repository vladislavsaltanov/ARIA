package aria.monitor.app

import android.annotation.SuppressLint
import android.content.Context
import androidx.datastore.preferences.preferencesDataStoreFile
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob

internal object AppStores {
    private var settings: DataStoreSettings? = null

    @SuppressLint("StaticFieldLeak")
    private var sessions: ServiceSessionSource? = null
    val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)

    @Synchronized
    fun settings(context: Context): DataStoreSettings =
        settings ?: DataStoreSettings(
            monitorDataStore(CoroutineScope(SupervisorJob() + Dispatchers.IO)) {
                context.applicationContext.preferencesDataStoreFile("monitor")
            },
        ).also { settings = it }

    @Synchronized
    fun sessions(context: Context): ServiceSessionSource =
        sessions ?: ServiceSessionSource(context).also { sessions = it }
}
