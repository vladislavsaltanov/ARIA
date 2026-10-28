package aria.monitor.app

import aria.monitor.core.Levels
import aria.monitor.core.Pairing
import aria.monitor.core.Settings
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.booleanPreferencesKey
import androidx.datastore.preferences.core.doublePreferencesKey
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.intPreferencesKey
import androidx.datastore.preferences.core.stringPreferencesKey
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.flow
import java.io.File
import java.io.IOException
import kotlin.io.path.createTempDirectory
import kotlin.test.AfterTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue
import kotlin.test.assertNull

class DataStoreSettingsTest {
    private val directory = createTempDirectory("monitor-settings").toFile()
    private val scope = CoroutineScope(Dispatchers.IO + SupervisorJob())
    private val dataStore: DataStore<Preferences> = monitorDataStore(scope) { File(directory, "monitor.preferences_pb") }
    private val store = DataStoreSettings(dataStore)

    @AfterTest
    fun cleanUp() {
        scope.cancel()
        directory.deleteRecursively()
    }

    @Test
    fun emptyStoreLoadsDefaults() = realTest {
        assertEquals(Settings.sanitized(), store.load())
        assertNull(store.loadPairing())
    }

    @Test
    fun corruptedFileIsReplacedAndStoreKeepsWorking() = realTest {
        scope.cancel()
        val corruptedScope = CoroutineScope(Dispatchers.IO + SupervisorJob())
        val file = File(directory, "corrupted.preferences_pb").apply { writeBytes(ByteArray(64) { 0x7f }) }
        val corrupted = DataStoreSettings(monitorDataStore(corruptedScope) { file })
        val settings = Settings("Ana", Levels(-6.5, false, 3.0, true), 42)

        assertEquals(Settings.sanitized(), corrupted.load())
        corrupted.save(settings)

        assertEquals(settings, corrupted.load())
        corruptedScope.cancel()
    }

    @Test
    fun unreadableAndUnwritableStoreDegradesWithoutThrowing() = realTest {
        val broken = DataStoreSettings(
            object : DataStore<Preferences> {
                override val data: Flow<Preferences> = flow { throw IOException("disk") }

                override suspend fun updateData(transform: suspend (Preferences) -> Preferences): Preferences = throw IOException("disk")
            },
        )

        assertEquals(Settings.sanitized(), broken.load())
        assertNull(broken.loadPairing())
        broken.save(Settings("Ana", Levels(-1.0, true, -2.0, true), 5))
        broken.savePairing(Pairing("host", 80, "id", "key"))
        broken.savePairing(null)
    }

    @Test
    fun settingsRoundTrip() = realTest {
        val settings = Settings("Ana", Levels(backingDb = -6.5, backingOn = false, clickDb = 3.0, clickOn = true), trimMs = 42)

        store.save(settings)

        assertEquals(settings, store.load())
    }

    @Test
    fun storedNumbersAreClampedBySanitizing() = realTest {
        dataStore.edit {
            it[doublePreferencesKey("backing_db")] = 99.0
            it[doublePreferencesKey("click_db")] = -99.0
            it[intPreferencesKey("trim_ms")] = 9000
        }

        val loaded = store.load()

        assertEquals(6.0, loaded.levels.backingDb)
        assertEquals(-40.0, loaded.levels.clickDb)
        assertEquals(500, loaded.trimMs)
    }

    @Test
    fun quotedNumberIsNotCoerced() = realTest {
        dataStore.edit {
            it[stringPreferencesKey("backing_db")] = "5"
            it[stringPreferencesKey("click_db")] = "-3"
            it[stringPreferencesKey("trim_ms")] = "100"
        }

        val loaded = store.load()

        assertEquals(0.0, loaded.levels.backingDb)
        assertEquals(0.0, loaded.levels.clickDb)
        assertEquals(0, loaded.trimMs)
    }

    @Test
    fun booleanInNumericSlotAndNumberInBooleanSlotAreIgnored() = realTest {
        dataStore.edit {
            it[booleanPreferencesKey("backing_db")] = true
            it[booleanPreferencesKey("click_db")] = true
            it[doublePreferencesKey("backing_on")] = 0.0
            it[stringPreferencesKey("click_on")] = "false"
            it[intPreferencesKey("name")] = 7
        }

        val loaded = store.load()

        assertEquals(Settings.sanitized(), loaded)
    }

    @Test
    fun pairingRoundTrip() = realTest {
        val pairing = Pairing("192.168.1.20", 8080, "ana", "s3cret")

        store.savePairing(pairing)

        assertEquals(pairing, store.loadPairing())
    }

    @Test
    fun forgetPairing() = realTest {
        store.savePairing(Pairing("192.168.1.20", 8080, "ana", "s3cret"))
        store.save(Settings("Ana", Levels(-1.0, true, -2.0, true), 5))

        store.savePairing(null)

        assertNull(store.loadPairing())
        assertEquals("Ana", store.load().name)
        assertEquals(emptyList(), dataStore.data.first().asMap().keys.map { it.name }.filter { it.startsWith("pairing_") })
    }

    @Test
    fun incompleteOrMistypedPairingLoadsAsNull() = realTest {
        val complete = Pairing("host", 80, "id", "key")
        val corruptions = mapOf<String, suspend () -> Unit>(
            "missing key" to { dataStore.edit { it.remove(stringPreferencesKey("pairing_key")) } },
            "missing host" to { dataStore.edit { it.remove(stringPreferencesKey("pairing_host")) } },
            "string port" to { dataStore.edit { it[stringPreferencesKey("pairing_port")] = "80" } },
            "zero port" to { dataStore.edit { it[intPreferencesKey("pairing_port")] = 0 } },
            "huge port" to { dataStore.edit { it[intPreferencesKey("pairing_port")] = 70000 } },
            "empty id" to { dataStore.edit { it[stringPreferencesKey("pairing_id")] = "" } },
            "numeric host" to { dataStore.edit { it[intPreferencesKey("pairing_host")] = 7 } },
        )

        corruptions.forEach { (label, corrupt) ->
            store.savePairing(complete)
            corrupt()
            assertNull(store.loadPairing(), label)
        }
    }

    @Test
    fun onboardingIsNotDoneOnAFreshStore() = realTest {
        assertFalse(store.isDone())
    }

    @Test
    fun markingOnboardingDoneIsRemembered() = realTest {
        store.markDone()

        assertTrue(store.isDone())
    }

    @Test
    fun aWronglyTypedOnboardingFlagCountsAsNotDone() = realTest {
        dataStore.edit { it[stringPreferencesKey("onboarding_done")] = "yes" }

        assertFalse(store.isDone())
    }

    @Test
    fun onboardingFlagDoesNotTouchSettingsOrPairing() = realTest {
        store.savePairing(Pairing("h", 1, "i", "k"))
        store.markDone()

        assertEquals(Pairing("h", 1, "i", "k"), store.loadPairing())
        assertEquals(Settings.sanitized(), store.load())
    }
}
