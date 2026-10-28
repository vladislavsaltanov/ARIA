@file:OptIn(ExperimentalCoroutinesApi::class)

package aria.monitor.core

import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.double
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.long
import kotlin.math.roundToLong
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

private fun TestScope.step(ms: Long) {
    advanceTimeBy(ms)
    runCurrent()
}

private fun TestScope.connected(rig: SessionRig = SessionRig(this)): SessionRig {
    rig.transport.autoOpen = true
    rig.session.start(SHOW_PAIRING)
    runCurrent()
    rig.socket.text(WELCOME_JSON)
    runCurrent()
    return rig
}

private fun TestScope.playing(timeline: ServerMessage.Timeline = showTimeline(1)): SessionRig {
    val rig = connected()
    rig.socket.text(timelineJson(timeline))
    step(500)
    assertTrue(rig.engine.plans.isNotEmpty())
    return rig
}

private fun FakeSocket.Frame.levels(): Levels = Json.decodeFromJsonElement(Levels.serializer(), JsonObject(json - "type"))

private fun FakeSocket.Frame.number(name: String): Double = json.getValue(name).jsonPrimitive.double

private fun SessionRig.authTimes(): List<Double> = authenticator.calls.map { it.atMs - LOCAL_START_MS }

private fun assertStrictlyIncreasing(values: List<Long>) {
    assertTrue(values.zipWithNext().all { (earlier, later) -> later > earlier }, "not strictly increasing: $values")
}

class SessionTest {
    @Test
    fun signsInOpensSocketSendsHelloWithLevels() = runTest {
        val rig = SessionRig(this)
        val levels = Levels(backingDb = -12.0, backingOn = true, clickDb = -3.0, clickOn = false)
        rig.store.settings = Settings(name = "Drums", levels = levels, trimMs = 40)

        rig.session.start(SHOW_PAIRING)
        runCurrent()

        assertEquals(listOf(SHOW_PAIRING), rig.authenticator.calls.map { it.pairing })
        assertEquals(1, rig.transport.sockets.size)
        assertEquals(SHOW_PAIRING, rig.socket.pairing)
        assertEquals("token-1", rig.socket.token)
        assertEquals(LinkState.Connecting, rig.session.state.value.link)
        assertTrue(rig.socket.frames.isEmpty())
        assertEquals(1, rig.engine.starts)

        rig.socket.open()
        runCurrent()

        val hello = rig.socket.frames.first()
        assertEquals("hello", hello.type)
        assertEquals("Drums", hello.json.getValue("name").jsonPrimitive.content)
        assertEquals(levels, Json.decodeFromJsonElement(Levels.serializer(), hello.json.getValue("levels")))
        val state = rig.session.state.value
        assertEquals(LinkState.Syncing, state.link)
        assertEquals(levels, state.levels)
        assertEquals(40, state.trimMs)
        assertEquals("Drums", state.name)
    }

    @Test
    fun pingEvery250msFor5sThenEverySecond() = runTest {
        val rig = connected()
        val openedAt = rig.socket.openedAtMs

        step(8000)

        val pings = rig.socket.sent("ping")
        assertEquals((0..20).map { it * 250.0 } + listOf(6000.0, 7000.0, 8000.0), pings.map { it.atMs - openedAt })
        pings.forEach { assertEquals(it.atMs, it.number("t0")) }
    }

    @Test
    fun pongFeedsClockAndStatsEvery2sCarryLockedOrSyncing() = runTest {
        val rig = SessionRig(this)
        rig.transport.pongRttMs = 60.0
        connected(rig)
        val openedAt = rig.socket.openedAtMs
        assertEquals(LinkState.Syncing, rig.session.state.value.link)
        assertEquals(30_000L, rig.session.state.value.uncertaintyUs)

        step(2000)

        val syncing = rig.socket.sent("stats").single()
        assertEquals(2000.0, syncing.atMs - openedAt)
        assertEquals("syncing", syncing.json.getValue("state").jsonPrimitive.content)
        assertEquals(30_000L, syncing.json.getValue("uncertaintyUs").jsonPrimitive.long)
        assertEquals(60_000L, syncing.json.getValue("rttUs").jsonPrimitive.long)

        rig.socket.pongRttMs = 0.0
        step(2000)

        val stats = rig.socket.sent("stats")
        assertEquals(listOf(2000.0, 4000.0), stats.map { it.atMs - openedAt })
        val locked = stats.last()
        assertEquals("locked", locked.json.getValue("state").jsonPrimitive.content)
        assertEquals(0L, locked.json.getValue("uncertaintyUs").jsonPrimitive.long)
        assertEquals(0L, locked.json.getValue("rttUs").jsonPrimitive.long)
        assertEquals((HOST_AHEAD_MS * 1000).roundToLong(), locked.json.getValue("offsetUs").jsonPrimitive.long)
        assertEquals(0.0, locked.number("trimMs"))
        assertEquals(LinkState.Locked, rig.session.state.value.link)
        assertEquals(0L, rig.session.state.value.uncertaintyUs)
        assertEquals(hostUsAtLocal(rig.clock.nowMs()).toDouble(), assertNotNull(rig.session.hostNowUs()), 1.0)
    }

    @Test
    fun staleTimelineSeqIgnored() = runTest {
        val rig = connected()
        val accepted = showTimeline(5)

        rig.socket.text(timelineJson(accepted))
        runCurrent()
        assertEquals(accepted, rig.session.state.value.timeline)

        rig.socket.text(timelineJson(showTimeline(4, title = "Older")))
        rig.socket.text(timelineJson(showTimeline(5, title = "Duplicate")))
        runCurrent()
        assertEquals(accepted, rig.session.state.value.timeline)

        val newer = showTimeline(6, title = "Newer")
        rig.socket.text(timelineJson(newer))
        runCurrent()
        assertEquals(newer, rig.session.state.value.timeline)
    }

    @Test
    fun afterReconnectLowerSeqAccepted() = runTest {
        val rig = connected()
        rig.socket.text(timelineJson(showTimeline(50)))
        runCurrent()
        val old = rig.socket

        old.closeFromServer()
        runCurrent()
        step(500)

        assertEquals(2, rig.transport.sockets.size)
        val fresh = rig.socket
        fresh.text(WELCOME_JSON)
        val restarted = showTimeline(3, title = "After restart")
        fresh.text(timelineJson(restarted))
        runCurrent()
        assertEquals(restarted, rig.session.state.value.timeline)

        old.text(timelineJson(showTimeline(100, title = "Late from the old socket")))
        fresh.text(timelineJson(showTimeline(2, title = "Stale")))
        runCurrent()
        assertEquals(restarted, rig.session.state.value.timeline)
    }

    @Test
    fun silentSocketOver2sIsDroppedAndReconnectsWithBackoff500To5000() = runTest {
        val rig = SessionRig(this)
        rig.transport.autoOpen = true
        rig.transport.pongRttMs = null

        rig.session.start(SHOW_PAIRING)
        runCurrent()
        step(33_000)

        val sockets = rig.transport.sockets
        assertEquals(7, sockets.size)
        sockets.take(6).forEach { assertEquals(2500.0, assertNotNull(it.closedAtMs) - it.openedAtMs) }
        assertNull(sockets.last().closedAtMs)
        val gaps = sockets.zipWithNext { previous, next -> next.openedAtMs - assertNotNull(previous.closedAtMs) }
        assertEquals(listOf(500.0, 1000.0, 2000.0, 4000.0, 5000.0, 5000.0), gaps)
        assertEquals(7, rig.authenticator.calls.size)
    }

    @Test
    fun reauthenticatesOnEveryReconnect() = runTest {
        val rig = connected()
        rig.pcm.respond = { PcmResult.Failed(503) }
        rig.socket.text(timelineJson(showTimeline(1)))
        step(100)
        assertEquals(setOf("token-1"), rig.pcm.calls.map { it.token }.toSet())

        repeat(2) {
            rig.socket.closeFromServer()
            runCurrent()
            step(500)
            rig.socket.text(WELCOME_JSON)
            step(250)
        }

        assertEquals(listOf(0.0, 600.0, 1350.0), rig.authTimes())
        assertEquals(listOf("token-1", "token-2", "token-3"), rig.transport.sockets.map { it.token })
        assertEquals("token-3", rig.pcm.calls.last().token)
    }

    @Test
    fun unauthorizedCloseTriggersReauthNotTightLoop() = runTest {
        val rig = connected()

        rig.socket.closeFromServer(unauthorized = true)
        runCurrent()
        step(499)
        assertEquals(1, rig.authenticator.calls.size)
        step(1)
        assertEquals(listOf(0.0, 500.0), rig.authTimes())
        assertEquals(2, rig.transport.sockets.size)

        rig.socket.closeFromServer(unauthorized = true)
        runCurrent()
        step(999)
        assertEquals(2, rig.authenticator.calls.size)
        step(1)
        assertEquals(listOf(0.0, 500.0, 1500.0), rig.authTimes())

        rig.authenticator.respond = { AuthResult.Denied }
        rig.socket.closeFromServer(unauthorized = true)
        runCurrent()
        step(2000)
        assertEquals(listOf(0.0, 500.0, 1500.0, 3500.0), rig.authTimes())
        assertTrue(rig.session.state.value.needsPairing)
        assertEquals(LinkState.Offline, rig.session.state.value.link)

        step(60_000)
        assertEquals(4, rig.authenticator.calls.size)
        assertEquals(3, rig.transport.sockets.size)
    }

    @Test
    fun pcmUnauthorizedReauthenticatesOnceAndResumesLoadingWithTheNewToken() = runTest {
        val rig = connected()
        rig.authenticator.respond = { call -> if (call < 3) AuthResult.Ok("token-$call") else AuthResult.Denied }
        rig.pcm.respond = { PcmResult.Failed(401) }
        rig.socket.text(timelineJson(showTimeline(1)))
        runCurrent()

        step(50)
        val rejected = rig.pcm.calls.size
        assertTrue(rejected in 1..2)
        step(499)
        assertEquals(1, rig.authenticator.calls.size)
        assertEquals(rejected, rig.pcm.calls.size)

        step(1)
        assertEquals(listOf(0.0, 550.0), rig.authTimes())
        assertTrue(rig.pcm.calls.drop(rejected).isNotEmpty())
        assertTrue(rig.pcm.calls.drop(rejected).all { it.token == "token-2" })
        assertEquals(1, rig.transport.sockets.size)
        assertNull(rig.socket.closedAtMs)

        step(999)
        assertEquals(2, rig.authenticator.calls.size)
        step(1)
        assertEquals(listOf(0.0, 550.0, 1550.0), rig.authTimes())
        assertTrue(rig.session.state.value.needsPairing)

        step(60_000)
        assertEquals(3, rig.authenticator.calls.size)
        assertEquals(1, rig.transport.sockets.size)
    }

    @Test
    fun kickedStopsReconnectUntilRetry() = runTest {
        val rig = connected()

        rig.socket.text(KICKED_JSON)
        runCurrent()
        assertTrue(rig.session.state.value.kicked)
        rig.socket.closeFromServer()
        runCurrent()
        step(60_000)

        assertEquals(1, rig.authenticator.calls.size)
        assertEquals(1, rig.transport.sockets.size)
        assertEquals(LinkState.Offline, rig.session.state.value.link)

        rig.session.retryAfterKick()
        runCurrent()

        assertFalse(rig.session.state.value.kicked)
        assertEquals(listOf(0.0, 60_000.0), rig.authTimes())
        assertEquals(2, rig.transport.sockets.size)
    }

    @Test
    fun revokedForgetsPairing() = runTest {
        val rig = SessionRig(this)
        rig.store.pairing = SHOW_PAIRING
        rig.transport.autoOpen = true
        rig.session.start(null)
        runCurrent()

        rig.socket.text(REVOKED_JSON)
        runCurrent()

        assertEquals(listOf<Pairing?>(null), rig.store.savedPairings)
        assertNull(rig.store.pairing)
        assertTrue(rig.session.state.value.needsPairing)
        assertFalse(rig.session.state.value.kicked)

        rig.socket.closeFromServer()
        runCurrent()
        step(60_000)
        assertEquals(1, rig.authenticator.calls.size)
        assertEquals(1, rig.transport.sockets.size)
    }

    @Test
    fun hostLevelsAppliedAndEchoed() = runTest {
        val rig = playing()

        rig.socket.text(levelsJson(Levels(backingDb = -55.0, backingOn = false, clickDb = 3.0, clickOn = true)))
        runCurrent()

        val applied = Levels(backingDb = -40.0, backingOn = false, clickDb = 3.0, clickOn = true)
        assertEquals(applied, rig.session.state.value.levels)
        assertEquals(applied, rig.store.saved.last().levels)
        assertEquals(applied, rig.socket.sent("levels").single().levels())
        val plan = rig.engine.plans.last()
        assertEquals(0f, plan.backingGain)
        assertEquals(dbToGain(3.0).toFloat(), plan.clickGain)
    }

    @Test
    fun uiLevelsSentToHost() = runTest {
        val rig = playing()
        val published = rig.engine.plans.size

        rig.session.setLevels(Levels(backingDb = -6.0, backingOn = true, clickDb = 10.0, clickOn = true))
        runCurrent()

        val applied = Levels(backingDb = -6.0, backingOn = true, clickDb = 6.0, clickOn = true)
        assertEquals(applied, rig.socket.sent("levels").single().levels())
        assertEquals(applied, rig.session.state.value.levels)
        assertEquals(applied, rig.store.saved.last().levels)
        assertEquals(published + 1, rig.engine.plans.size)
        assertEquals(dbToGain(-6.0).toFloat(), rig.engine.plans.last().backingGain)
        assertEquals(dbToGain(6.0).toFloat(), rig.engine.plans.last().clickGain)
    }

    @Test
    fun lossOver10sPublishesPlanWithMasterRamp1s() = runTest {
        val rig = playing()
        rig.socket.pongRttMs = null
        rig.authenticator.respond = { AuthResult.Offline }
        rig.socket.text(statusJson(true))
        rig.socket.closeFromServer()
        runCurrent()

        step(10_000)
        assertTrue(rig.engine.plans.all { it.masterGain == 1f })

        step(50)
        val silenced = rig.engine.plans.last()
        assertEquals(0f, silenced.masterGain)
        assertEquals(1.0f, silenced.masterRampSeconds)

        val firstSilent = rig.engine.plans.size - 1
        step(1000)
        assertTrue(rig.engine.plans.size > firstSilent + 1)
        assertTrue(rig.engine.plans.drop(firstSilent).all { it.masterGain == 0f && it.masterRampSeconds == 1.0f })

        rig.authenticator.respond = { AuthResult.Ok("token-$it") }
        step(2000)
        assertEquals(2, rig.transport.sockets.size)
        assertEquals(0f, rig.engine.plans.last().masterGain)

        rig.socket.text(WELCOME_JSON)
        rig.socket.text(timelineJson(showTimeline(1)))
        runCurrent()

        val restored = rig.engine.plans.last()
        assertEquals(1f, restored.masterGain)
        assertEquals(0.02f, restored.masterRampSeconds)
    }

    @Test
    fun reconnectWithin10sKeepsPlaying() = runTest {
        val rig = playing()
        rig.authenticator.respond = { call -> if (call in 2..4) AuthResult.Offline else AuthResult.Ok("token-$call") }
        rig.socket.closeFromServer()
        runCurrent()
        val published = rig.engine.plans.size

        step(9000)
        assertEquals(2, rig.transport.sockets.size)
        assertEquals(8000.0, rig.socket.openedAtMs - LOCAL_START_MS)

        step(20_000)
        assertTrue(rig.engine.plans.size > published + 20)
        assertTrue(rig.engine.plans.all { it.masterGain == 1f })
    }

    @Test
    fun streamReopenedResetsEstimatorAndRepublishes() = runTest {
        val rig = playing()
        val published = rig.engine.plans.size

        rig.engine.reopen()
        runCurrent()
        rig.socket.text(timelineJson(showTimeline(2)))
        runCurrent()
        assertEquals(published, rig.engine.plans.size)

        step(250)
        assertEquals(published, rig.engine.plans.size)

        step(250)
        assertEquals(published + 1, rig.engine.plans.size)
        val plan = rig.engine.plans.last()
        assertEquals(24_000L, plan.timeMap.baseFrame)
        assertEquals(1e6 / SAMPLE_RATE, plan.timeMap.usPerFrame, 1e-6)
        assertEquals(2, plan.voices.single().epoch)
    }

    @Test
    fun publishesNewPlanOnTimelineLevelsTrimAndTimeMap() = runTest {
        val rig = playing()
        val published = rig.engine.plans.size

        rig.socket.text(timelineJson(showTimeline(2)))
        runCurrent()
        assertEquals(published + 1, rig.engine.plans.size)
        assertEquals(2, rig.engine.plans.last().voices.single().epoch)

        rig.session.setLevels(Levels(backingDb = -10.0, backingOn = true, clickDb = -20.0, clickOn = true))
        runCurrent()
        assertEquals(published + 2, rig.engine.plans.size)
        assertEquals(dbToGain(-10.0).toFloat(), rig.engine.plans.last().backingGain)

        rig.session.setTrim(120)
        runCurrent()
        assertEquals(published + 3, rig.engine.plans.size)
        assertEquals(120_000L, rig.engine.plans.last().trimUs)
        assertEquals(120, rig.session.state.value.trimMs)
        assertEquals(120, rig.store.saved.last().trimMs)

        rig.session.setTestClick(true)
        runCurrent()
        assertEquals(published + 4, rig.engine.plans.size)

        val previousMap = rig.engine.plans.last().timeMap
        step(250)
        assertEquals(published + 5, rig.engine.plans.size)
        assertTrue(rig.engine.plans.last().timeMap.baseFrame > previousMap.baseFrame)
        assertStrictlyIncreasing(rig.engine.plans.map { it.generation })
    }

    @Test
    fun noPlanBeforeClockReady() = runTest {
        val rig = SessionRig(this)
        rig.transport.autoOpen = true
        rig.transport.pongRttMs = null
        rig.session.start(SHOW_PAIRING)
        runCurrent()
        rig.socket.text(WELCOME_JSON)
        rig.socket.text(timelineJson(showTimeline(1)))

        step(1000)
        assertTrue(rig.engine.plans.isEmpty())
        assertNull(rig.session.hostNowUs())

        val ping = rig.socket.sent("ping").last()
        rig.socket.text(pongJson(ping.number("t0"), hostUsAtLocal(ping.atMs)))
        runCurrent()
        assertNotNull(rig.session.hostNowUs())

        step(250)
        assertEquals(1, rig.engine.plans.size)
    }

    @Test
    fun testClickFlagFollowsSetTestClick() = runTest {
        val test = TestTimeline(anchorUs = hostUsAtLocal(LOCAL_START_MS), intervalUs = 500_000.0)
        val rig = playing(showTimeline(1, test = test))
        assertFalse(rig.session.state.value.testClick)
        assertNull(rig.engine.plans.last().test)
        assertTrue(rig.engine.plans.last().backingGain > 0f)

        rig.session.setTestClick(true)
        runCurrent()

        assertTrue(rig.session.state.value.testClick)
        assertEquals(test, rig.engine.plans.last().test)
        assertEquals(0f, rig.engine.plans.last().backingGain)
        assertTrue(rig.engine.plans.last().clickGain > 0f)

        rig.session.setTestClick(false)
        runCurrent()

        assertFalse(rig.session.state.value.testClick)
        assertNull(rig.engine.plans.last().test)
        assertTrue(rig.engine.plans.last().backingGain > 0f)
    }

    @Test
    fun clockAndStampsShareTheInjectedMonotonicClock() = runTest {
        val rig = playing()

        rig.socket.sent("ping").forEach { assertEquals(it.atMs, it.number("t0")) }
        val map = rig.engine.plans.last().timeMap
        assertEquals(FIRST_STREAM_FRAME + 24_000L, map.baseFrame)
        assertEquals(hostUsAtLocal(rig.at(500)).toDouble(), map.hostUsAtBaseFrame.toDouble(), 1.0)
        assertEquals(hostUsAtLocal(rig.clock.nowMs()).toDouble(), assertNotNull(rig.session.hostNowUs()), 1.0)
    }

    @Test
    fun welcomeResetsReconnectBackoff() = runTest {
        val rig = SessionRig(this)
        rig.transport.autoOpen = true
        rig.authenticator.respond = { call -> if (call in 2..3) AuthResult.Offline else AuthResult.Ok("token-$call") }
        rig.session.start(SHOW_PAIRING)
        runCurrent()
        rig.socket.closeFromServer()
        runCurrent()

        step(3500)
        assertEquals(2, rig.transport.sockets.size)
        rig.socket.text(WELCOME_JSON)
        runCurrent()
        rig.socket.closeFromServer()
        runCurrent()
        step(500)

        assertEquals(listOf(0.0, 500.0, 1500.0, 3500.0, 4000.0), rig.authTimes())
        assertEquals(3, rig.transport.sockets.size)
    }

    @Test
    fun statusSetsOutputOk() = runTest {
        val rig = connected()
        assertTrue(rig.session.state.value.outputOk)

        rig.socket.text(statusJson(false))
        runCurrent()
        assertFalse(rig.session.state.value.outputOk)

        rig.socket.text(statusJson(true))
        runCurrent()
        assertTrue(rig.session.state.value.outputOk)
    }

    @Test
    fun stopCancelsTickersSocketLoaderAndEngine() = runTest {
        val rig = SessionRig(this)
        rig.pcm.respond = { PcmResult.Failed(503) }
        connected(rig)
        rig.socket.text(timelineJson(showTimeline(1)))
        step(500)
        assertTrue(rig.engine.plans.isNotEmpty())
        assertTrue(rig.pcm.calls.isNotEmpty())
        val socket = rig.socket

        rig.session.stop()
        runCurrent()

        assertNotNull(socket.closedAtMs)
        assertEquals(1, rig.engine.stops)
        assertEquals(LinkState.Offline, rig.session.state.value.link)
        assertEquals(emptySet(), rig.engine.retains.last())
        val frames = socket.frames.size
        val plans = rig.engine.plans.size
        val fetches = rig.pcm.calls.size
        socket.text(timelineJson(showTimeline(9)))
        socket.closeFromServer()
        step(60_000)
        assertEquals(frames, socket.frames.size)
        assertEquals(plans, rig.engine.plans.size)
        assertEquals(fetches, rig.pcm.calls.size)
        assertEquals(1, rig.authenticator.calls.size)
        assertEquals(1, rig.transport.sockets.size)
        assertEquals(1L, rig.session.state.value.timeline?.seq)
    }

    @Test
    fun startTwiceReplacesTheConnection() = runTest {
        val rig = connected()
        val first = rig.socket

        rig.session.start(OTHER_PAIRING)
        runCurrent()

        assertNotNull(first.closedAtMs)
        assertEquals(2, rig.transport.sockets.size)
        assertEquals(OTHER_PAIRING, rig.socket.pairing)
        assertEquals(listOf(SHOW_PAIRING, OTHER_PAIRING), rig.authenticator.calls.map { it.pairing })
        assertEquals(2, rig.engine.starts)
        assertEquals(1, rig.engine.stops)
        val firstFrames = first.frames.size

        step(1000)
        assertEquals(firstFrames, first.frames.size)
        assertEquals(5, rig.socket.sent("ping").size)

        first.text(timelineJson(showTimeline(1)))
        runCurrent()
        assertNull(rig.session.state.value.timeline)
    }

    @Test
    fun restartWithAnotherHostForgetsTheOldHostClock() = runTest {
        val rig = connected()
        step(2000)
        assertEquals(LinkState.Locked, rig.session.state.value.link)
        val otherHostAheadMs = HOST_AHEAD_MS + 3_600_000.0
        rig.transport.autoOpen = false
        rig.transport.hostAheadMs = otherHostAheadMs

        rig.session.start(OTHER_PAIRING)
        runCurrent()
        assertNull(rig.session.hostNowUs())

        rig.socket.open()
        runCurrent()
        assertEquals(hostUsAtLocal(rig.clock.nowMs(), otherHostAheadMs).toDouble(), assertNotNull(rig.session.hostNowUs()), 1.0)
        assertEquals(LinkState.Syncing, rig.session.state.value.link)
    }

    @Test
    fun startWithoutSavedPairingNeedsPairing() = runTest {
        val rig = SessionRig(this)

        rig.session.start(null)
        runCurrent()

        assertTrue(rig.session.state.value.needsPairing)
        assertTrue(rig.authenticator.calls.isEmpty())
        assertTrue(rig.transport.sockets.isEmpty())
        assertEquals(0, rig.engine.starts)

        rig.store.pairing = SHOW_PAIRING
        rig.session.start(null)
        runCurrent()

        assertFalse(rig.session.state.value.needsPairing)
        assertEquals(listOf(SHOW_PAIRING), rig.authenticator.calls.map { it.pairing })
        assertEquals(1, rig.engine.starts)
    }

    @Test
    fun xrunGrowthGrowsBuffer() = runTest {
        val rig = playing()

        rig.engine.xrunCount = 2
        step(250)
        assertEquals(1, rig.engine.grows)
        step(250)
        assertEquals(1, rig.engine.grows)
        rig.engine.xrunCount = 3
        step(250)
        assertEquals(2, rig.engine.grows)
    }

    @Test
    fun generationOnlyIncreasesAcrossReconnectReopenAndRestart() = runTest {
        val rig = playing()

        rig.socket.closeFromServer()
        runCurrent()
        step(500)
        rig.socket.text(WELCOME_JSON)
        rig.socket.text(timelineJson(showTimeline(1)))
        step(250)
        rig.engine.reopen()
        step(500)
        val beforeRestart = rig.engine.plans.size
        rig.session.start(SHOW_PAIRING)
        runCurrent()
        rig.socket.text(WELCOME_JSON)
        rig.socket.text(timelineJson(showTimeline(1)))
        step(500)

        assertTrue(rig.engine.plans.size > beforeRestart)
        assertStrictlyIncreasing(rig.engine.plans.map { it.generation })
    }

    @Test
    fun commandsIssuedWhileSettingsLoadWinAndArePersistedOnce() = runTest {
        val rig = SessionRig(this)
        val stored = Levels(backingDb = -12.0, backingOn = true, clickDb = -3.0, clickOn = false)
        rig.store.settings = Settings(name = "Drums", levels = stored, trimMs = 40)
        val gate = CompletableDeferred<Unit>()
        rig.store.loadGate = gate
        rig.transport.autoOpen = true

        rig.session.start(SHOW_PAIRING)
        runCurrent()
        val chosen = Levels(backingDb = -6.0, backingOn = false, clickDb = 2.0, clickOn = true)
        rig.session.setLevels(chosen)
        rig.session.setTrim(80)
        rig.session.setTestClick(true)
        runCurrent()
        gate.complete(Unit)
        runCurrent()

        val state = rig.session.state.value
        assertEquals(chosen, state.levels)
        assertEquals(80, state.trimMs)
        assertEquals("Drums", state.name)
        assertTrue(state.testClick)
        assertEquals(listOf(Settings("Drums", chosen, 40), Settings("Drums", chosen, 80)), rig.store.saved)
        assertEquals("Drums", rig.socket.sent("hello").single().json.getValue("name").jsonPrimitive.content)
    }

    @Test
    fun hostThatAcceptsThenClosesWithoutAFrameStillSilencesAfter10s() = runTest {
        val rig = playing()
        rig.transport.pongRttMs = null

        rig.socket.closeFromServer()
        runCurrent()
        step(30_000)

        assertTrue(rig.transport.sockets.size > 3)
        assertEquals(0f, rig.engine.plans.last().masterGain)
        assertEquals(1.0f, rig.engine.plans.last().masterRampSeconds)
    }

    @Test
    fun laterSuccessfulSignInClearsNeedsPairing() = runTest {
        val rig = connected()
        rig.authenticator.respond = { call -> if (call == 2) AuthResult.Denied else AuthResult.Ok("token-$call") }
        rig.pcm.respond = { PcmResult.Failed(401) }
        rig.socket.text(timelineJson(showTimeline(1)))
        step(600)
        assertTrue(rig.session.state.value.needsPairing)

        rig.socket.closeFromServer()
        runCurrent()
        step(1000)

        assertEquals(3, rig.authenticator.calls.size)
        assertFalse(rig.session.state.value.needsPairing)
    }

    @Test
    fun reopenAfterOutageLongerThanTheClockWindowReportsSyncingUntilTheFirstPong() = runTest {
        val rig = connected()
        step(2000)
        assertEquals(LinkState.Locked, rig.session.state.value.link)
        rig.authenticator.respond = { call -> if (call == 2) AuthResult.Offline else AuthResult.Ok("token-$call") }
        rig.transport.autoOpen = false
        rig.transport.pongRttMs = null

        rig.socket.closeFromServer()
        runCurrent()
        step(40_000)
        val links = mutableListOf<LinkState>()
        rig.socket.open()
        runCurrent()
        links += rig.session.state.value.link
        rig.socket.pongRttMs = 0.0
        repeat(12) {
            step(250)
            links += rig.session.state.value.link
        }

        assertEquals(LinkState.Syncing, links.first())
        assertEquals(LinkState.Locked, links.last())
        assertEquals(links.indexOfLast { it == LinkState.Syncing } + 1, links.indexOfFirst { it == LinkState.Locked })
    }

    @Test
    fun setNameSendsHelloAndSavesWithoutPublishingAPlan() = runTest {
        val rig = playing()
        val plans = rig.engine.plans.size
        val levels = rig.session.state.value.levels

        rig.session.setName("  Bass  ")
        runCurrent()

        assertEquals("Bass", rig.session.state.value.name)
        val hello = rig.socket.sent("hello").last()
        assertEquals("Bass", hello.json.getValue("name").jsonPrimitive.content)
        assertEquals(levels, Json.decodeFromJsonElement(Levels.serializer(), hello.json.getValue("levels")))
        assertEquals(2, rig.socket.sent("hello").size)
        assertEquals("Bass", rig.store.saved.single().name)
        assertEquals(plans, rig.engine.plans.size)
    }

    @Test
    fun restartResetsTheRunButKeepsTheSettings() = runTest {
        val rig = playing()
        val levels = Levels(backingDb = -9.0, backingOn = true, clickDb = -2.0, clickOn = true)
        rig.session.setLevels(levels)
        rig.session.setTrim(30)
        rig.session.setTestClick(true)
        rig.socket.text(statusJson(false))
        rig.socket.text(KICKED_JSON)
        runCurrent()
        rig.transport.autoOpen = false

        rig.session.start(SHOW_PAIRING)
        runCurrent()

        val state = rig.session.state.value
        assertNull(state.timeline)
        assertFalse(state.testClick)
        assertFalse(state.kicked)
        assertTrue(state.outputOk)
        assertNull(state.uncertaintyUs)
        assertEquals(LinkState.Connecting, state.link)
        assertEquals(levels, state.levels)
        assertEquals(30, state.trimMs)
    }

    @Test
    fun wave2_failedEngineStartReportsNoAudioAndRetriesOnTheStampTickUntilItStarts() = runTest {
        val rig = SessionRig(this)
        rig.engine.startResult = false
        connected(rig)

        assertFalse(rig.session.state.value.audioOk)
        assertEquals(1, rig.engine.starts)
        step(250)
        assertEquals(2, rig.engine.starts)
        step(250)
        assertEquals(3, rig.engine.starts)
        assertFalse(rig.session.state.value.audioOk)

        rig.engine.startResult = true
        step(250)
        assertEquals(4, rig.engine.starts)
        step(250)
        assertTrue(rig.session.state.value.audioOk)
        step(5000)
        assertEquals(4, rig.engine.starts)
    }

    @Test
    fun wave2_successfulStartIsNeverRetried() = runTest {
        val rig = connected()
        rig.engine.stamping = false

        step(10_000)

        assertEquals(1, rig.engine.starts)
        assertEquals(0, rig.engine.stops)
    }

    @Test
    fun wave2_noStampForTwoSecondsAfterStartReportsNoAudioUntilTheFirstStamp() = runTest {
        val rig = SessionRig(this)
        rig.engine.stamping = false
        connected(rig)

        step(1750)
        assertTrue(rig.session.state.value.audioOk)
        step(250)
        assertFalse(rig.session.state.value.audioOk)

        rig.engine.stamping = true
        step(250)
        assertTrue(rig.session.state.value.audioOk)
    }

    @Test
    fun wave2_stampsThatStopForTwoSecondsReportNoAudio() = runTest {
        val rig = playing()
        assertTrue(rig.session.state.value.audioOk)
        rig.engine.stamping = false

        step(1750)
        assertTrue(rig.session.state.value.audioOk)
        step(250)
        assertFalse(rig.session.state.value.audioOk)
    }

    @Test
    fun wave2_streamReopenReportsNoAudioUntilTheNextStamp() = runTest {
        val rig = playing()
        step(100)
        rig.engine.reopen()
        runCurrent()

        assertFalse(rig.session.state.value.audioOk)
        step(150)
        assertTrue(rig.session.state.value.audioOk)
    }

    @Test
    fun wave2_restartAfterAFailedStartBeginsWithAudioOk() = runTest {
        val rig = SessionRig(this)
        rig.engine.startResult = false
        connected(rig)
        assertFalse(rig.session.state.value.audioOk)

        rig.engine.startResult = true
        rig.session.start(SHOW_PAIRING)
        runCurrent()

        assertTrue(rig.session.state.value.audioOk)
    }
}
