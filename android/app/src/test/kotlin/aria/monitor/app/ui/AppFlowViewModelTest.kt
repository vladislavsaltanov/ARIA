@file:OptIn(ExperimentalCoroutinesApi::class)

package aria.monitor.app.ui

import aria.monitor.core.Pairing
import aria.monitor.core.Settings
import aria.monitor.core.SettingsStore
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import kotlin.test.AfterTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

private val NEW_PAIRING = Pairing("192.168.1.30", 5001, "tablet", "k3y")

private class FakeOnboarding(var done: Boolean = false) : OnboardingStore {
    override suspend fun isDone(): Boolean = done

    override suspend fun markDone() {
        done = true
    }
}

private class OrderedStore(private val journal: MutableList<String>, private val saveTakesMs: Long = 0) : SettingsStore {
    @Volatile
    var pairing: Pairing? = null

    override suspend fun load(): Settings = Settings.sanitized()

    override suspend fun save(settings: Settings) = Unit

    override suspend fun loadPairing(): Pairing? = pairing

    override suspend fun savePairing(pairing: Pairing?) {
        delay(saveTakesMs)
        journal += "save:${pairing?.identifier}"
        this.pairing = pairing
    }
}

private fun TestScope.flowOver(
    store: SettingsStore,
    onboarding: OnboardingStore,
    source: HeldSessionSource = HeldSessionSource(),
): AppFlowViewModel {
    Dispatchers.setMain(UnconfinedTestDispatcher(testScheduler))
    return AppFlowViewModel(store, onboarding, source).also { runCurrent() }
}

class AppFlowViewModelTest {
    @AfterTest
    fun resetMain() {
        Dispatchers.resetMain()
    }

    @Test
    fun firstRunStartsWithOnboarding() = runTest {
        val flow = flowOver(RecordingStore(), FakeOnboarding(done = false))

        assertEquals(Destination.Onboarding, flow.destination.value)
    }

    @Test
    fun finishingOnboardingWithoutPairingGoesToPairingThatCannotBeCancelled() = runTest {
        val onboarding = FakeOnboarding()
        val flow = flowOver(RecordingStore(), onboarding)

        flow.finishOnboarding()
        runCurrent()

        assertEquals(Destination.Pairing(canCancel = false), flow.destination.value)
        assertTrue(onboarding.done)
    }

    @Test
    fun finishingOnboardingWithASavedPairingGoesStraightToTheMonitor() = runTest {
        val store = RecordingStore().apply { pairing = SHOW_PAIRING }
        val flow = flowOver(store, FakeOnboarding())

        flow.finishOnboarding()
        runCurrent()

        assertEquals(Destination.Monitor, flow.destination.value)
    }

    @Test
    fun onboardingIsNotShownAgainAfterTheFirstRun() = runTest {
        val flow = flowOver(RecordingStore(), FakeOnboarding(done = true))

        assertEquals(Destination.Pairing(canCancel = false), flow.destination.value)
    }

    @Test
    fun returningUserWithAPairingOpensTheMonitor() = runTest {
        val store = RecordingStore().apply { pairing = SHOW_PAIRING }

        assertEquals(Destination.Monitor, flowOver(store, FakeOnboarding(done = true)).destination.value)
    }

    @Test
    fun finishOnboardingOutsideOnboardingChangesNothing() = runTest {
        val store = RecordingStore().apply { pairing = SHOW_PAIRING }
        val flow = flowOver(store, FakeOnboarding(done = true))

        flow.openPairing()
        flow.finishOnboarding()
        runCurrent()

        assertEquals(Destination.Pairing(canCancel = true), flow.destination.value)
    }

    @Test
    fun openingPairingFromTheMonitorCanBeCancelled() = runTest {
        val store = RecordingStore().apply { pairing = SHOW_PAIRING }
        val flow = flowOver(store, FakeOnboarding(done = true))

        flow.openPairing()
        assertEquals(Destination.Pairing(canCancel = true), flow.destination.value)
        flow.cancelPairing()

        assertEquals(Destination.Monitor, flow.destination.value)
    }

    @Test
    fun firstPairingCannotBeCancelled() = runTest {
        val flow = flowOver(RecordingStore(), FakeOnboarding(done = true))

        flow.cancelPairing()

        assertEquals(Destination.Pairing(canCancel = false), flow.destination.value)
    }

    @Test
    fun openPairingOutsideTheMonitorChangesNothing() = runTest {
        val flow = flowOver(RecordingStore(), FakeOnboarding(done = false))

        flow.openPairing()

        assertEquals(Destination.Onboarding, flow.destination.value)
    }

    @Test
    fun pairedPersistsThePairingThenStartsTheSessionWithIt() = runTest {
        val journal = mutableListOf<String>()
        val store = OrderedStore(journal)
        val rig = ShowRig(this)
        val source = HeldSessionSource(rig.session)
        val flow = flowOver(store, FakeOnboarding(done = true), source)

        flow.paired(NEW_PAIRING)
        runCurrent()

        assertEquals(NEW_PAIRING, store.pairing)
        assertEquals(Destination.Monitor, flow.destination.value)
        assertEquals(listOf(NEW_PAIRING), rig.authenticator.calls.toList())
        assertEquals(listOf("save:tablet"), journal)
    }

    @Test
    fun theSessionIsNotStartedBeforeThePairingIsSafelyPersisted() = runTest {
        val store = OrderedStore(mutableListOf(), saveTakesMs = 100)
        val rig = ShowRig(this)
        val flow = flowOver(store, FakeOnboarding(done = true), HeldSessionSource(rig.session))

        flow.paired(NEW_PAIRING)
        runCurrent()
        assertTrue(rig.authenticator.calls.isEmpty())
        advanceTimeBy(100)
        runCurrent()

        assertEquals(listOf(NEW_PAIRING), rig.authenticator.calls.toList())
    }

    @Test
    fun pairedWithoutARunningSessionStillPersistsForTheServiceToPickUp() = runTest {
        val store = RecordingStore()
        val flow = flowOver(store, FakeOnboarding(done = true))

        flow.paired(NEW_PAIRING)
        runCurrent()

        assertEquals(NEW_PAIRING, store.pairing)
        assertEquals(Destination.Monitor, flow.destination.value)
    }

    @Test
    fun pairedAgainFromTheMonitorReplacesTheOldPairing() = runTest {
        val store = RecordingStore().apply { pairing = SHOW_PAIRING }
        val rig = ShowRig(this)
        val flow = flowOver(store, FakeOnboarding(done = true), HeldSessionSource(rig.session))

        flow.openPairing()
        flow.paired(NEW_PAIRING)
        runCurrent()

        assertEquals(NEW_PAIRING, store.pairing)
        assertEquals(listOf(NEW_PAIRING), rig.authenticator.calls.toList())
    }

    @Test
    fun aSecondPairedCallFromAStaleScannerFrameIsIgnored() = runTest {
        val store = RecordingStore()
        val rig = ShowRig(this)
        val flow = flowOver(store, FakeOnboarding(done = true), HeldSessionSource(rig.session))

        flow.paired(NEW_PAIRING)
        flow.paired(SHOW_PAIRING)
        runCurrent()

        assertEquals(NEW_PAIRING, store.pairing)
        assertEquals(listOf(NEW_PAIRING), rig.authenticator.calls.toList())
    }

    @Test
    fun pairedOutsideThePairingScreenIsIgnored() = runTest {
        val store = RecordingStore().apply { pairing = SHOW_PAIRING }
        val flow = flowOver(store, FakeOnboarding(done = true))

        flow.paired(NEW_PAIRING)
        runCurrent()

        assertEquals(SHOW_PAIRING, store.pairing)
    }
}
