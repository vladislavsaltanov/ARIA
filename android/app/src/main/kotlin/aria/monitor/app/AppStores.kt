package aria.monitor.app

import android.content.Context
import androidx.datastore.preferences.preferencesDataStoreFile
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob

internal object AppStores {
    private var settings: DataStoreSettings? = null

    @Synchronized
    fun settings(context: Context): DataStoreSettings =
        settings ?: DataStoreSettings(
            monitorDataStore(CoroutineScope(SupervisorJob() + Dispatchers.IO)) {
                context.applicationContext.preferencesDataStoreFile("monitor")
            },
        ).also { settings = it }
}
