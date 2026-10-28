@file:OptIn(ExperimentalCoroutinesApi::class)

package aria.monitor.app.ui

import aria.monitor.core.Levels
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlin.test.AfterTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue

private fun TestScope.viewModelOver(source: HeldSessionSource): MonitorViewModel {
    Dispatchers.setMain(UnconfinedTestDispatcher(testScheduler))
    val viewModel = MonitorViewModel(source)
    backgroundScope.launch(UnconfinedTestDispatcher(testScheduler)) { viewModel.ui.collect {} }
    return viewModel
}

private fun levelsOf(frame: JsonObject): Levels = Json.decodeFromJsonElement(Levels.serializer(), JsonObject(frame - "type"))

private fun TestScope.started(rig: ShowRig) {
    rig.session.start(SHOW_PAIRING)
    runCurrent()
    rig.socket.text(WELCOME_JSON)
    runCurrent()
}

class MonitorViewModelTest {
    @AfterTest
    fun resetMain() {
        Dispatchers.resetMain()
    }

    @Test
    fun withoutSessionTheUiIsTheEmptyOfflineState() = runTest {
        val viewModel = viewModelOver(HeldSessionSource())

        runCurrent()

        assertEquals(MonitorUi.NoSession, viewModel.ui.value)
    }

    @Test
    fun uiFollowsTheSessionWhenItAppearsAndWhenItGoesAway() = runTest {
        val rig = ShowRig(this)
        val source = HeldSessionSource()
        val viewModel = viewModelOver(source)
        started(rig)

        source.hold(rig.session)
        runCurrent()

        assertTrue(viewModel.ui.value.hasSession)
        assertEquals(LinkStatus.Syncing, viewModel.ui.value.status)

        source.hold(null)
        runCurrent()

        assertEquals(MonitorUi.NoSession, viewModel.ui.value)
    }

    @Test
    fun sessionStartedWithoutPairingShowsTheNewQrOverlayState() = runTest {
        val rig = ShowRig(this)
        val viewModel = viewModelOver(HeldSessionSource(rig.session))
        rig.session.start(null)
        runCurrent()

        assertTrue(viewModel.ui.value.needsPairing)
        assertEquals(LinkStatus.Offline, viewModel.ui.value.status)
    }

    @Test
    fun hostKickShowsTheKickedStateAndRetryReconnects() = runTest {
        val rig = ShowRig(this)
        val viewModel = viewModelOver(HeldSessionSource(rig.session))
        started(rig)

        rig.socket.text(KICKED_JSON)
        runCurrent()
        assertTrue(viewModel.ui.value.kicked)
        assertEquals(1, rig.authenticator.calls.size)

        viewModel.retryAfterKick()
        runCurrent()

        assertFalse(viewModel.ui.value.kicked)
        assertEquals(2, rig.authenticator.calls.size)
    }

    @Test
    fun hostOutputStatusReachesTheBadgeState() = runTest {
        val rig = ShowRig(this)
        val viewModel = viewModelOver(HeldSessionSource(rig.session))
        started(rig)

        rig.socket.text("""{"type":"status","output":false}""")
        runCurrent()

        assertTrue(viewModel.ui.value.outputMissing)
    }

    @Test
    fun levelActionsGoToTheSessionAndComeBackInTheUi() = runTest {
        val rig = ShowRig(this)
        val viewModel = viewModelOver(HeldSessionSource(rig.session))
        started(rig)

        viewModel.setBackingDb(-12.0)
        runCurrent()
        viewModel.toggleBacking()
        runCurrent()
        viewModel.setClickDb(3.0)
        runCurrent()
        viewModel.toggleClick()
        runCurrent()

        val expected = Levels(backingDb = -12.0, backingOn = false, clickDb = 3.0, clickOn = false)
        assertEquals(expected, viewModel.ui.value.levels)
        assertEquals(expected, rig.store.saved.last().levels)
        assertEquals(4, rig.store.saved.size)
        assertEquals(expected, levelsOf(rig.socket.sent("levels").last()))
    }

    @Test
    fun unchangedLevelIsNotResent() = runTest {
        val rig = ShowRig(this)
        val viewModel = viewModelOver(HeldSessionSource(rig.session))
        started(rig)
        viewModel.setBackingDb(-6.0)
        runCurrent()
        val saves = rig.store.saved.size
        val sends = rig.socket.sent("levels").size

        viewModel.setBackingDb(-6.0)
        runCurrent()

        assertEquals(saves, rig.store.saved.size)
        assertEquals(sends, rig.socket.sent("levels").size)
    }

    @Test
    fun trimStepsAccumulateAndStayWithinTheClamp() = runTest {
        val rig = ShowRig(this)
        val viewModel = viewModelOver(HeldSessionSource(rig.session))
        started(rig)

        viewModel.adjustTrim(10)
        runCurrent()
        viewModel.adjustTrim(-1)
        runCurrent()
        assertEquals(9, viewModel.ui.value.trimMs)
        assertEquals(9, rig.store.saved.last().trimMs)

        viewModel.adjustTrim(10_000)
        runCurrent()
        assertEquals(500, viewModel.ui.value.trimMs)

        viewModel.adjustTrim(-10_000)
        runCurrent()
        assertEquals(-100, viewModel.ui.value.trimMs)
    }

    @Test
    fun testClickToggles() = runTest {
        val rig = ShowRig(this)
        val viewModel = viewModelOver(HeldSessionSource(rig.session))
        started(rig)

        viewModel.toggleTestClick()
        runCurrent()
        assertTrue(viewModel.ui.value.testClick)

        viewModel.toggleTestClick()
        runCurrent()
        assertFalse(viewModel.ui.value.testClick)
    }

    @Test
    fun nameIsSentTrimmedAndOnlyWhenItChanged() = runTest {
        val rig = ShowRig(this)
        val viewModel = viewModelOver(HeldSessionSource(rig.session))
        started(rig)
        val hellos = rig.socket.sent("hello").size

        viewModel.setName("  Drums  ")
        runCurrent()
        assertEquals("Drums", viewModel.ui.value.name)
        assertEquals("Drums", rig.store.saved.last().name)
        assertEquals(hellos + 1, rig.socket.sent("hello").size)

        viewModel.setName("Drums ")
        runCurrent()
        assertEquals(hellos + 1, rig.socket.sent("hello").size)
    }

    @Test
    fun actionsWithoutSessionDoNothing() = runTest {
        val viewModel = viewModelOver(HeldSessionSource())

        viewModel.setBackingDb(-3.0)
        viewModel.toggleBacking()
        viewModel.setClickDb(-3.0)
        viewModel.toggleClick()
        viewModel.adjustTrim(10)
        viewModel.toggleTestClick()
        viewModel.setName("Drums")
        viewModel.retryAfterKick()
        runCurrent()

        assertEquals(MonitorUi.NoSession, viewModel.ui.value)
    }

    @Test
    fun keepScreenOnStartsOnAndFollowsTheSetter() = runTest {
        val viewModel = viewModelOver(HeldSessionSource())

        assertTrue(viewModel.keepScreenOn.value)
        viewModel.setKeepScreenOn(false)
        assertFalse(viewModel.keepScreenOn.value)
        viewModel.setKeepScreenOn(true)
        assertTrue(viewModel.keepScreenOn.value)
    }

    @Test
    fun frameWithoutSessionIsEmpty() = runTest {
        val viewModel = viewModelOver(HeldSessionSource())

        val frame = viewModel.frame()

        assertEquals("—", frame.beat.barBeat)
        assertEquals("--:--", frame.now.position)
        assertNull(frame.now.title)
    }

    @Test
    fun frameBeforeTheClockIsReadyKeepsTitleButNoPosition() = runTest {
        val rig = ShowRig(this)
        val viewModel = viewModelOver(HeldSessionSource(rig.session))
        rig.transport.answersPings = false
        rig.session.start(SHOW_PAIRING)
        runCurrent()
        rig.socket.text(timelineJson(gridTimeline()))
        runCurrent()
        assertNull(rig.session.hostNowUs())

        val frame = viewModel.frame()

        assertEquals("Song", frame.now.title)
        assertEquals("--:--", frame.now.position)
        assertEquals("—", frame.beat.barBeat)
        assertEquals("120 BPM", frame.beat.bpm)
    }

    @Test
    fun frameReadsTheSessionClockOnEveryCall() = runTest {
        val rig = ShowRig(this)
        val viewModel = viewModelOver(HeldSessionSource(rig.session))
        started(rig)
        rig.socket.text(timelineJson(gridTimeline()))
        runCurrent()

        val atStart = viewModel.frame()
        advanceTimeBy(10_100)
        runCurrent()
        val later = viewModel.frame()

        assertEquals("1:1", atStart.beat.barBeat)
        assertEquals("0:00", atStart.now.position)
        assertEquals("6:1", later.beat.barBeat)
        assertEquals(0, later.beat.litBeat)
        assertEquals("0:10", later.now.position)
        assertNotEquals(atStart, later)
    }
}
