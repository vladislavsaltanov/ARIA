@file:OptIn(ExperimentalCoroutinesApi::class)

package aria.monitor.app

import aria.monitor.app.ui.AnsweringTransport
import aria.monitor.app.ui.CountingAuthenticator
import aria.monitor.app.ui.RecordingStore
import aria.monitor.app.ui.SHOW_PAIRING
import aria.monitor.app.ui.SilentPcm
import aria.monitor.app.ui.TestClock
import aria.monitor.core.AudioEngine
import aria.monitor.core.ChunkKey
import aria.monitor.core.EngineEvent
import aria.monitor.core.OutputStamp
import aria.monitor.core.Pairing
import aria.monitor.core.RenderPlan
import aria.monitor.core.Session
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.job
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.currentTime
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.withContext
import kotlinx.coroutines.test.runTest
import java.util.concurrent.CopyOnWriteArrayList
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

private val NEW_PAIRING = Pairing("192.168.1.30", 5001, "tablet", "k3y")
private const val STUCK_MS = 60_000L

private class JournalEngine(private val journal: MutableList<String>) : AudioEngine {
    override val xrunCount = 0

    override fun start(): Boolean {
        journal += "start"
        return true
    }

    override fun stop() {
        journal += "stop"
    }

    override fun publish(plan: RenderPlan) = Unit

    override fun sampleStamp(): OutputStamp? = null

    override fun growBuffer() = Unit

    override fun setEventListener(listener: (EngineEvent) -> Unit) = Unit

    override fun write(key: ChunkKey, bytes: ByteArray, frames: Int): Boolean = true

    override fun markEmpty(key: ChunkKey) = Unit

    override fun retain(keys: Set<ChunkKey>) = Unit
}

private class Rig(test: TestScope) {
    val journal = CopyOnWriteArrayList<String>()
    val authenticator = CountingAuthenticator()
    val store = RecordingStore().apply { pairing = SHOW_PAIRING }
    val scope = CoroutineScope(SupervisorJob() + StandardTestDispatcher(test.testScheduler))
    private val clock = TestClock(test.testScheduler)
    val session = Session(scope, clock, authenticator, AnsweringTransport(clock), SilentPcm, JournalEngine(journal), store)
    var scopeCancelledAtRelease: Boolean? = null
    val runtime = MonitorRuntime(session, scope) {
        scopeCancelledAtRelease = scope.coroutineContext.job.isCancelled
        journal += "release"
    }

    fun starts(): Int = journal.count { it == "start" }
}

private fun rigTest(body: suspend TestScope.(Rig) -> Unit) = runTest {
    val rig = Rig(this)
    try {
        body(rig)
    } finally {
        rig.scope.coroutineContext.job.cancel()
    }
}

class MonitorRuntimeTest {
    @Test
    fun theFirstStartWithoutAPairingUsesTheSavedOne() = rigTest { rig ->

        rig.runtime.start(null)
        runCurrent()

        assertEquals(1, rig.starts())
        assertEquals(listOf(SHOW_PAIRING), rig.authenticator.calls.toList())
    }

    @Test
    fun aRepeatedStartWithoutAPairingDoesNotRestartTheEngine() = rigTest { rig ->

        rig.runtime.start(null)
        runCurrent()
        rig.runtime.start(null)
        runCurrent()

        assertEquals(1, rig.starts())
        assertEquals(1, rig.runtime.sessionStarts)
    }

    @Test
    fun aNewPairingRestartsTheSessionWithIt() = rigTest { rig ->

        rig.runtime.start(null)
        runCurrent()
        rig.runtime.start(NEW_PAIRING)
        runCurrent()

        assertEquals(listOf(SHOW_PAIRING, NEW_PAIRING), rig.authenticator.calls.toList())
        assertEquals(2, rig.runtime.sessionStarts)
    }

    @Test
    fun theFirstStartMayCarryThePairing() = rigTest { rig ->

        rig.runtime.start(NEW_PAIRING)
        runCurrent()
        rig.runtime.start(null)
        runCurrent()

        assertEquals(listOf(NEW_PAIRING), rig.authenticator.calls.toList())
        assertEquals(1, rig.starts())
    }

    @Test
    fun shutdownStopsTheEngineThenCancelsTheScopeThenReleases() = rigTest { rig ->
        rig.runtime.start(null)
        runCurrent()

        rig.runtime.shutdown()

        assertEquals(listOf("start", "stop", "release"), rig.journal.toList())
        assertEquals(true, rig.scopeCancelledAtRelease)
    }

    @Test
    fun shutdownOfANeverStartedRuntimeStillReleases() = rigTest { rig ->

        rig.runtime.shutdown()

        assertEquals(listOf("release"), rig.journal.toList())
        assertTrue(rig.scope.coroutineContext.job.isCancelled)
    }

    @Test
    fun startsAfterShutdownAreIgnored() = rigTest { rig ->
        rig.runtime.shutdown()

        rig.runtime.start(NEW_PAIRING)
        runCurrent()

        assertEquals(0, rig.runtime.sessionStarts)
        assertEquals(listOf("release"), rig.journal.toList())
    }

    @Test
    fun aStuckLaneDoesNotHoldTheReleaseForever() = rigTest { rig ->
        rig.runtime.start(null)
        rig.scope.launch { withContext(NonCancellable) { delay(STUCK_MS) } }
        runCurrent()

        rig.runtime.shutdown()

        assertEquals("release", rig.journal.last())
        assertTrue(currentTime < STUCK_MS)
    }
}
