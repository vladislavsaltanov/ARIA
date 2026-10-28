package aria.monitor.app

import aria.monitor.app.ui.OnboardingStore
import aria.monitor.core.Pairing
import aria.monitor.core.Settings
import aria.monitor.core.SettingsStore
import androidx.datastore.core.DataStore
import androidx.datastore.core.handlers.ReplaceFileCorruptionHandler
import androidx.datastore.preferences.core.MutablePreferences
import androidx.datastore.preferences.core.PreferenceDataStoreFactory
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.booleanPreferencesKey
import androidx.datastore.preferences.core.doublePreferencesKey
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.emptyPreferences
import androidx.datastore.preferences.core.intPreferencesKey
import androidx.datastore.preferences.core.stringPreferencesKey
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.first
import java.io.File
import java.io.IOException

private val NAME = stringPreferencesKey("name")
private val BACKING_DB = doublePreferencesKey("backing_db")
private val BACKING_ON = booleanPreferencesKey("backing_on")
private val CLICK_DB = doublePreferencesKey("click_db")
private val CLICK_ON = booleanPreferencesKey("click_on")
private val TRIM_MS = intPreferencesKey("trim_ms")
private val PAIRING_HOST = stringPreferencesKey("pairing_host")
private val PAIRING_PORT = intPreferencesKey("pairing_port")
private val PAIRING_ID = stringPreferencesKey("pairing_id")
private val PAIRING_KEY = stringPreferencesKey("pairing_key")
private val ONBOARDING_DONE = booleanPreferencesKey("onboarding_done")
private val PORTS = 1..65535

internal fun monitorDataStore(scope: CoroutineScope, produceFile: () -> File): DataStore<Preferences> =
    PreferenceDataStoreFactory.create(
        corruptionHandler = ReplaceFileCorruptionHandler { emptyPreferences() },
        scope = scope,
        produceFile = produceFile,
    )

internal class DataStoreSettings(private val dataStore: DataStore<Preferences>) : SettingsStore, OnboardingStore {
    override suspend fun load(): Settings {
        val stored = read()
        return Settings.sanitized(
            name = stored[NAME] as? String,
            backingDb = (stored[BACKING_DB] as? Number)?.toDouble(),
            backingOn = stored[BACKING_ON] as? Boolean,
            clickDb = (stored[CLICK_DB] as? Number)?.toDouble(),
            clickOn = stored[CLICK_ON] as? Boolean,
            trimMs = (stored[TRIM_MS] as? Number)?.toDouble(),
        )
    }

    override suspend fun save(settings: Settings) = write {
        it[NAME] = settings.name
        it[BACKING_DB] = settings.levels.backingDb
        it[BACKING_ON] = settings.levels.backingOn
        it[CLICK_DB] = settings.levels.clickDb
        it[CLICK_ON] = settings.levels.clickOn
        it[TRIM_MS] = settings.trimMs
    }

    override suspend fun loadPairing(): Pairing? {
        val stored = read()
        return Pairing(
            host = (stored[PAIRING_HOST] as? String)?.takeIf { it.isNotEmpty() } ?: return null,
            port = (stored[PAIRING_PORT] as? Int)?.takeIf { it in PORTS } ?: return null,
            identifier = (stored[PAIRING_ID] as? String)?.takeIf { it.isNotEmpty() } ?: return null,
            password = (stored[PAIRING_KEY] as? String)?.takeIf { it.isNotEmpty() } ?: return null,
        )
    }

    override suspend fun savePairing(pairing: Pairing?) = write {
        if (pairing == null) {
            it.remove(PAIRING_HOST)
            it.remove(PAIRING_PORT)
            it.remove(PAIRING_ID)
            it.remove(PAIRING_KEY)
        } else {
            it[PAIRING_HOST] = pairing.host
            it[PAIRING_PORT] = pairing.port
            it[PAIRING_ID] = pairing.identifier
            it[PAIRING_KEY] = pairing.password
        }
    }

    override suspend fun isDone(): Boolean = read()[ONBOARDING_DONE] as? Boolean == true

    override suspend fun markDone() = write { it[ONBOARDING_DONE] = true }

    private suspend fun read(): Map<Preferences.Key<*>, Any> =
        try {
            dataStore.data.first().asMap()
        } catch (unreadable: IOException) {
            emptyMap()
        }

    private suspend fun write(change: suspend (MutablePreferences) -> Unit) {
        try {
            dataStore.edit(change)
        } catch (unwritable: IOException) {
            return
        }
    }
}
