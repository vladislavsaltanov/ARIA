@file:OptIn(ExperimentalCoroutinesApi::class)

package aria.monitor.app

import aria.monitor.app.ui.LinkStatus
import aria.monitor.core.Levels
import aria.monitor.core.LinkState
import aria.monitor.core.SessionState
import android.net.wifi.WifiManager
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

private const val MIN_INTERVAL_MS = 1000L
private const val TICK_MS = 250L

private fun state(link: LinkState, uncertaintyUs: Long? = null, audioOk: Boolean = true) = SessionState(
    link = link,
    uncertaintyUs = uncertaintyUs,
    timeline = null,
    levels = Levels(0.0, true, 0.0, true),
    trimMs = 0,
    outputOk = true,
    testClick = false,
    kicked = false,
    needsPairing = false,
    name = "",
    audioOk = audioOk,
)

class ServicePolicyTest {
    @Test
    fun androidTenAndLaterUseTheLowLatencyWifiLock() {
        assertEquals(WifiManager.WIFI_MODE_FULL_LOW_LATENCY, wifiLockMode(29))
        assertEquals(WifiManager.WIFI_MODE_FULL_LOW_LATENCY, wifiLockMode(36))
    }

    @Test
    @Suppress("DEPRECATION")
    fun olderAndroidUsesTheHighPerformanceWifiLock() {
        assertEquals(WifiManager.WIFI_MODE_FULL_HIGH_PERF, wifiLockMode(28))
        assertEquals(WifiManager.WIFI_MODE_FULL_HIGH_PERF, wifiLockMode(26))
    }

    @Test
    fun theNotificationFollowsTheDisplayedStatusOnly() = runTest {
        val states = MutableStateFlow(state(LinkState.Offline))
        val shown = mutableListOf<LinkStatus>()
        val job = launch { states.notificationUpdates(MIN_INTERVAL_MS).collect { shown += it } }
        runCurrent()

        states.value = state(LinkState.Connecting)
        advanceTimeBy(MIN_INTERVAL_MS + 1)
        states.value = state(LinkState.Locked, 2400)
        advanceTimeBy(MIN_INTERVAL_MS + 1)
        states.value = state(LinkState.Locked, 2410)
        advanceTimeBy(MIN_INTERVAL_MS + 1)
        job.cancel()

        assertEquals(listOf(LinkStatus.Offline, LinkStatus.Locked("2,4")), shown)
    }

    @Test
    fun aFlickeringStatusIsPostedAtMostOncePerInterval() = runTest {
        val states = MutableStateFlow(state(LinkState.Locked, 1000))
        val shown = mutableListOf<LinkStatus>()
        val job = launch { states.notificationUpdates(MIN_INTERVAL_MS).collect { shown += it } }
        runCurrent()

        repeat(16) { tick ->
            states.value = state(LinkState.Locked, if (tick % 2 == 0) 2000L else 3000L)
            advanceTimeBy(TICK_MS)
        }
        runCurrent()
        job.cancel()

        assertEquals(LinkStatus.Locked("1,0"), shown.first())
        assertTrue(shown.size in 2..5)
    }

    @Test
    fun theNotificationShowsNoAudioWhileLinkedWithoutSound() = runTest {
        val states = MutableStateFlow(state(LinkState.Locked, 1000))
        val shown = mutableListOf<LinkStatus>()
        val job = launch { states.notificationUpdates(MIN_INTERVAL_MS).collect { shown += it } }
        runCurrent()

        states.value = state(LinkState.Locked, 1000, audioOk = false)
        advanceTimeBy(MIN_INTERVAL_MS + 1)
        states.value = state(LinkState.Offline, audioOk = false)
        advanceTimeBy(MIN_INTERVAL_MS + 1)
        job.cancel()

        assertEquals(listOf(LinkStatus.Locked("1,0"), LinkStatus.NoAudio, LinkStatus.Offline), shown)
    }
}
