@file:OptIn(ExperimentalCoroutinesApi::class)

package aria.monitor.app.ui

import aria.monitor.core.Pairing
import aria.monitor.core.Settings
import aria.monitor.core.SettingsStore
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.cancel
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
import kotlin.test.assertFalse
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

private class ForgetfulStore : SettingsStore {
    @Volatile
    var keeps = false
    private var pairing: Pairing? = null

    override suspend fun load(): Settings = Settings.sanitized()

    override suspend fun save(settings: Settings) = Unit

    override suspend fun loadPairing(): Pairing? = pairing

    override suspend fun savePairing(pairing: Pairing?) {
        if (keeps) this.pairing = pairing
    }
}

private fun TestScope.flowOver(
    store: SettingsStore,
    onboarding: OnboardingStore,
    source: HeldSessionSource = HeldSessionSource(),
): AppFlowViewModel {
    Dispatchers.setMain(UnconfinedTestDispatcher(testScheduler))
    return AppFlowViewModel(store, onboarding, source, backgroundScope).also { runCurrent() }
}

class AppFlowViewModelTest {
    @AfterTest
    fun resetMain() {
        Dispatchers.resetMain()
    }

    @Test
    fun firstRunStartsWithOnboarding() = runTest {
        val source = HeldSessionSource()
        val flow = flowOver(RecordingStore(), FakeOnboarding(done = false), source)

        assertEquals(Destination.Onboarding, flow.destination.value)
        assertTrue(source.starts.isEmpty())
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
        val source = HeldSessionSource()
        val flow = flowOver(store, FakeOnboarding(), source)

        flow.finishOnboarding()
        runCurrent()

        assertEquals(Destination.Monitor, flow.destination.value)
        assertEquals(listOf<Pairing?>(null), source.starts.toList())
    }

    @Test
    fun onboardingIsNotShownAgainAfterTheFirstRun() = runTest {
        val source = HeldSessionSource()
        val flow = flowOver(RecordingStore(), FakeOnboarding(done = true), source)

        assertEquals(Destination.Pairing(canCancel = false), flow.destination.value)
        assertTrue(source.starts.isEmpty())
    }

    @Test
    fun returningUserWithAPairingOpensTheMonitor() = runTest {
        val store = RecordingStore().apply { pairing = SHOW_PAIRING }
        val source = HeldSessionSource()

        assertEquals(Destination.Monitor, flowOver(store, FakeOnboarding(done = true), source).destination.value)
        assertEquals(listOf<Pairing?>(null), source.starts.toList())
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
        val source = HeldSessionSource(journal = journal)
        val flow = flowOver(store, FakeOnboarding(done = true), source)

        flow.paired(NEW_PAIRING)
        runCurrent()

        assertEquals(NEW_PAIRING, store.pairing)
        assertEquals(Destination.Monitor, flow.destination.value)
        assertEquals(listOf("save:tablet", "start:tablet"), journal)
        assertFalse(flow.pairingNotSaved.value)
    }

    @Test
    fun theSessionIsNotStartedBeforeThePairingIsSafelyPersisted() = runTest {
        val store = OrderedStore(mutableListOf(), saveTakesMs = 100)
        val source = HeldSessionSource()
        val flow = flowOver(store, FakeOnboarding(done = true), source)

        flow.paired(NEW_PAIRING)
        runCurrent()
        assertTrue(source.starts.isEmpty())
        advanceTimeBy(100)
        runCurrent()

        assertEquals(listOf<Pairing?>(NEW_PAIRING), source.starts.toList())
    }

    @Test
    fun closingTheScreenWhileThePairingIsBeingSavedLosesNothing() = runTest {
        val store = OrderedStore(mutableListOf(), saveTakesMs = 100)
        val source = HeldSessionSource()
        val flow = flowOver(store, FakeOnboarding(done = true), source)

        flow.paired(NEW_PAIRING)
        runCurrent()
        flow.viewModelScope.cancel()
        advanceTimeBy(100)
        runCurrent()

        assertEquals(NEW_PAIRING, store.pairing)
        assertEquals(listOf<Pairing?>(NEW_PAIRING), source.starts.toList())
    }

    @Test
    fun aPairingTheStoreFailedToKeepIsReportedAndStillStarted() = runTest {
        val source = HeldSessionSource()
        val flow = flowOver(ForgetfulStore(), FakeOnboarding(done = true), source)

        flow.paired(NEW_PAIRING)
        runCurrent()

        assertTrue(flow.pairingNotSaved.value)
        assertEquals(Destination.Monitor, flow.destination.value)
        assertEquals(listOf<Pairing?>(NEW_PAIRING), source.starts.toList())
    }

    @Test
    fun aLaterSuccessfulSaveClearsTheReport() = runTest {
        val store = ForgetfulStore()
        val flow = flowOver(store, FakeOnboarding(done = true))

        flow.paired(NEW_PAIRING)
        runCurrent()
        store.keeps = true
        flow.openPairing()
        flow.paired(SHOW_PAIRING)
        runCurrent()

        assertFalse(flow.pairingNotSaved.value)
    }

    @Test
    fun pairedWithoutARunningSessionStillPersistsForTheServiceToPickUp() = runTest {
        val store = RecordingStore()
        val source = HeldSessionSource()
        val flow = flowOver(store, FakeOnboarding(done = true), source)

        flow.paired(NEW_PAIRING)
        runCurrent()

        assertEquals(NEW_PAIRING, store.pairing)
        assertEquals(Destination.Monitor, flow.destination.value)
        assertEquals(listOf<Pairing?>(NEW_PAIRING), source.starts.toList())
    }

    @Test
    fun pairedAgainFromTheMonitorReplacesTheOldPairing() = runTest {
        val store = RecordingStore().apply { pairing = SHOW_PAIRING }
        val source = HeldSessionSource()
        val flow = flowOver(store, FakeOnboarding(done = true), source)

        flow.openPairing()
        flow.paired(NEW_PAIRING)
        runCurrent()

        assertEquals(NEW_PAIRING, store.pairing)
        assertEquals(listOf(null, NEW_PAIRING), source.starts.toList())
    }

    @Test
    fun aSecondPairedCallFromAStaleScannerFrameIsIgnored() = runTest {
        val store = RecordingStore()
        val source = HeldSessionSource()
        val flow = flowOver(store, FakeOnboarding(done = true), source)

        flow.paired(NEW_PAIRING)
        flow.paired(SHOW_PAIRING)
        runCurrent()

        assertEquals(NEW_PAIRING, store.pairing)
        assertEquals(listOf<Pairing?>(NEW_PAIRING), source.starts.toList())
    }

    @Test
    fun aRefusedStartIsSurfaced() = runTest {
        val store = RecordingStore().apply { pairing = SHOW_PAIRING }
        val source = HeldSessionSource().apply { accepts = false }

        val flow = flowOver(store, FakeOnboarding(done = true), source)

        assertTrue(flow.monitorStopped.value)
    }

    @Test
    fun aRefusedStartAfterPairingIsSurfaced() = runTest {
        val source = HeldSessionSource().apply { accepts = false }
        val flow = flowOver(RecordingStore(), FakeOnboarding(done = true), source)

        flow.paired(NEW_PAIRING)
        runCurrent()

        assertTrue(flow.monitorStopped.value)
    }

    @Test
    fun aSessionArrivingAfterARefusalClearsTheStop() = runTest {
        val store = RecordingStore().apply { pairing = SHOW_PAIRING }
        val source = HeldSessionSource().apply { accepts = false }
        val flow = flowOver(store, FakeOnboarding(done = true), source)

        source.hold(ShowRig(this).session)
        runCurrent()

        assertFalse(flow.monitorStopped.value)
    }

    @Test
    fun aDeliberateStopSurvivesARotation() = runTest {
        val store = RecordingStore().apply { pairing = SHOW_PAIRING }
        val source = HeldSessionSource(ShowRig(this).session)
        val flow = flowOver(store, FakeOnboarding(done = true), source)
        flow.resume()

        source.reportStop(deliberate = true)
        source.hold(null)
        runCurrent()
        flow.onActivityStopped(changingConfigurations = true)
        flow.resume()

        assertTrue(flow.monitorStopped.value)
        assertEquals(listOf<Pairing?>(null, null), source.starts.toList())
    }

    @Test
    fun aDeliberateStopIsLiftedByComingBackFromTheBackground() = runTest {
        val store = RecordingStore().apply { pairing = SHOW_PAIRING }
        val source = HeldSessionSource(ShowRig(this).session)
        val flow = flowOver(store, FakeOnboarding(done = true), source)
        source.reportStop(deliberate = true)
        source.hold(null)
        runCurrent()

        flow.onActivityStopped(changingConfigurations = false)
        flow.resume()

        assertFalse(flow.monitorStopped.value)
        assertEquals(listOf<Pairing?>(null, null), source.starts.toList())
    }

    @Test
    fun aRotationAfterBackgroundingStillCountsAsComingBack() = runTest {
        val store = RecordingStore().apply { pairing = SHOW_PAIRING }
        val source = HeldSessionSource(ShowRig(this).session)
        val flow = flowOver(store, FakeOnboarding(done = true), source)
        source.reportStop(deliberate = true)
        runCurrent()

        flow.onActivityStopped(changingConfigurations = true)
        flow.resume()
        flow.onActivityStopped(changingConfigurations = false)
        flow.resume()

        assertEquals(listOf<Pairing?>(null, null), source.starts.toList())
    }

    @Test
    fun reconnectLiftsADeliberateStop() = runTest {
        val store = RecordingStore().apply { pairing = SHOW_PAIRING }
        val source = HeldSessionSource(ShowRig(this).session)
        val flow = flowOver(store, FakeOnboarding(done = true), source)
        source.reportStop(deliberate = true)
        source.hold(null)
        runCurrent()

        flow.reconnect()

        assertFalse(flow.monitorStopped.value)
        assertEquals(listOf<Pairing?>(null, null), source.starts.toList())
        flow.onActivityStopped(changingConfigurations = true)
        flow.resume()
        assertEquals(listOf<Pairing?>(null, null, null), source.starts.toList())
    }

    @Test
    fun aSessionLostAfterARotationIsStillReported() = runTest {
        val store = RecordingStore().apply { pairing = SHOW_PAIRING }
        val rig = ShowRig(this)
        val source = HeldSessionSource(rig.session)
        val flow = flowOver(store, FakeOnboarding(done = true), source)
        flow.onActivityStopped(changingConfigurations = true)
        source.hold(null)
        flow.resume()
        source.hold(rig.session)
        runCurrent()

        source.hold(null)
        runCurrent()

        assertTrue(flow.monitorStopped.value)
    }

    @Test
    fun aSessionLostWithoutAStopIsRearmedOnTheNextStart() = runTest {
        val store = RecordingStore().apply { pairing = SHOW_PAIRING }
        val source = HeldSessionSource(ShowRig(this).session)
        val flow = flowOver(store, FakeOnboarding(done = true), source)

        source.hold(null)
        runCurrent()
        flow.onActivityStopped(changingConfigurations = true)
        flow.resume()

        assertFalse(flow.monitorStopped.value)
        assertEquals(listOf<Pairing?>(null, null), source.starts.toList())
    }

    @Test
    fun aRefusalInsideTheServiceRaisesTheStopFlag() = runTest {
        val store = RecordingStore().apply { pairing = SHOW_PAIRING }
        val source = HeldSessionSource()
        val flow = flowOver(store, FakeOnboarding(done = true), source)

        source.reportStop(deliberate = false)
        runCurrent()

        assertTrue(flow.monitorStopped.value)
        flow.onActivityStopped(changingConfigurations = true)
        flow.resume()
        assertEquals(listOf<Pairing?>(null, null), source.starts.toList())
    }

    @Test
    fun aStopReportedBeforeTheScreenExistedIsIgnored() = runTest {
        val store = RecordingStore().apply { pairing = SHOW_PAIRING }
        val source = HeldSessionSource().apply { reportStop(deliberate = true) }
        val flow = flowOver(store, FakeOnboarding(done = true), source)

        flow.resume()

        assertFalse(flow.monitorStopped.value)
        assertEquals(listOf<Pairing?>(null, null), source.starts.toList())
    }

    @Test
    fun leavingTheScreenIsNotReportedAsAStop() = runTest {
        val store = RecordingStore().apply { pairing = SHOW_PAIRING }
        val source = HeldSessionSource(ShowRig(this).session)
        val flow = flowOver(store, FakeOnboarding(done = true), source)

        flow.onActivityStopped(changingConfigurations = false)
        source.hold(null)
        runCurrent()

        assertFalse(flow.monitorStopped.value)
    }

    @Test
    fun aNewPairingLiftsADeliberateStop() = runTest {
        val store = RecordingStore().apply { pairing = SHOW_PAIRING }
        val source = HeldSessionSource(ShowRig(this).session)
        val flow = flowOver(store, FakeOnboarding(done = true), source)
        source.reportStop(deliberate = true)
        runCurrent()

        flow.openPairing()
        flow.paired(NEW_PAIRING)
        runCurrent()
        flow.onActivityStopped(changingConfigurations = true)
        flow.resume()

        assertEquals(listOf(null, NEW_PAIRING, null), source.starts.toList())
    }

    @Test
    fun aStopWhileVisibleStaysStoppedWithoutRestarting() = runTest {
        val store = RecordingStore().apply { pairing = SHOW_PAIRING }
        val rig = ShowRig(this)
        val source = HeldSessionSource(rig.session)
        val flow = flowOver(store, FakeOnboarding(done = true), source)
        assertFalse(flow.monitorStopped.value)

        source.hold(null)
        runCurrent()

        assertTrue(flow.monitorStopped.value)
        assertEquals(listOf<Pairing?>(null), source.starts.toList())
    }

    @Test
    fun aSessionThatNeverArrivedIsNotReportedAsStopped() = runTest {
        val store = RecordingStore().apply { pairing = SHOW_PAIRING }
        val flow = flowOver(store, FakeOnboarding(done = true), HeldSessionSource())

        assertFalse(flow.monitorStopped.value)
    }

    @Test
    fun reconnectRearmsTheServiceAndClearsTheStop() = runTest {
        val store = RecordingStore().apply { pairing = SHOW_PAIRING }
        val rig = ShowRig(this)
        val source = HeldSessionSource(rig.session)
        val flow = flowOver(store, FakeOnboarding(done = true), source)
        source.hold(null)
        runCurrent()

        flow.reconnect()

        assertFalse(flow.monitorStopped.value)
        assertEquals(listOf<Pairing?>(null, null), source.starts.toList())
    }

    @Test
    fun aRestartedScreenRearmsTheService() = runTest {
        val store = RecordingStore().apply { pairing = SHOW_PAIRING }
        val source = HeldSessionSource()
        val flow = flowOver(store, FakeOnboarding(done = true), source)

        flow.resume()

        assertEquals(listOf<Pairing?>(null, null), source.starts.toList())
    }

    @Test
    fun resumeOutsideTheMonitorStartsNothing() = runTest {
        val source = HeldSessionSource()
        val flow = flowOver(RecordingStore(), FakeOnboarding(done = false), source)

        flow.resume()

        assertTrue(source.starts.isEmpty())
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
