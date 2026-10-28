@file:OptIn(ExperimentalCoroutinesApi::class)

package aria.monitor.app

import aria.monitor.app.ui.AnsweringTransport
import aria.monitor.app.ui.CountingAuthenticator
import aria.monitor.app.ui.RecordingStore
import aria.monitor.app.ui.SHOW_PAIRING
import aria.monitor.app.ui.SilentEngine
import aria.monitor.app.ui.SilentPcm
import aria.monitor.app.ui.TestClock
import aria.monitor.core.Session
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.awaitCancellation
import kotlinx.coroutines.delay
import kotlinx.coroutines.job
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.currentTime
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.withContext
import java.util.concurrent.CopyOnWriteArrayList
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertSame
import kotlin.test.assertTrue

private const val SLOW_LANE_MS = 1500L
private const val STUCK_MS = 60_000L
private const val WAIT_MS = 3000L
private const val SETTLE_MS = 10_000L

private class FakeProcess {
    val binding = OwnedBinding()

    @Volatile
    var bound: String? = null
}

private class FakeHolds(private val name: String, private val process: FakeProcess, private val journal: MutableList<String>) : Holds {
    override fun acquire() {
        journal += "acquire $name"
        process.binding.claim(this) {
            process.bound = name
            true
        }
    }

    override fun release() {
        journal += "release holds $name"
        process.binding.release(this) { process.bound = null }
    }
}

private class Handover(private val test: TestScope) {
    val journal = CopyOnWriteArrayList<String>()
    val process = FakeProcess()
    private val scopes = CopyOnWriteArrayList<CoroutineScope>()
    val lifecycle = MonitorLifecycle(
        CoroutineScope(SupervisorJob() + StandardTestDispatcher(test.testScheduler)).also { scopes += it },
        WAIT_MS,
    )

    fun instance(name: String, slowLaneMs: Long = 0): MonitorInstance =
        MonitorInstance(lifecycle, { runtime(name, slowLaneMs) }, { FakeHolds(name, process, journal) })

    private fun runtime(name: String, slowLaneMs: Long): MonitorRuntime {
        journal += "create $name"
        val scope = CoroutineScope(SupervisorJob() + StandardTestDispatcher(test.testScheduler)).also { scopes += it }
        if (slowLaneMs > 0) scope.launch { withContext(NonCancellable) { delay(slowLaneMs) } }
        val clock = TestClock(test.testScheduler)
        val store = RecordingStore().apply { pairing = SHOW_PAIRING }
        val session = Session(scope, clock, CountingAuthenticator(), AnsweringTransport(clock), SilentPcm, SilentEngine(), store)
        return MonitorRuntime(session, scope) { journal += "release engine $name" }
    }

    fun cancelAll() = scopes.forEach { it.coroutineContext.job.cancel() }
}

private fun TestScope.settle() {
    advanceTimeBy(SETTLE_MS)
    runCurrent()
}

private fun handoverTest(body: suspend TestScope.(Handover) -> Unit) = runTest {
    val handover = Handover(this)
    try {
        body(handover)
    } finally {
        handover.cancelAll()
    }
}

class MonitorLifecycleTest {
    @Test
    fun stopThenImmediateStartCreatesTheNewEngineOnlyAfterTheOldOneIsReleased() = handoverTest { handover ->
        val old = handover.instance("old", slowLaneMs = SLOW_LANE_MS)
        old.open()
        old.start(null)
        runCurrent()

        old.close()
        val new = handover.instance("new")
        new.open()
        new.start(null)
        runCurrent()

        assertEquals(listOf("create old", "acquire old"), handover.journal.toList())
        settle()
        assertEquals(
            listOf("create old", "acquire old", "release engine old", "release holds old", "create new", "acquire new"),
            handover.journal.toList(),
        )
        assertEquals(1, new.runtime.value?.sessionStarts)
    }

    @Test
    fun stopThenImmediateStartLeavesTheNewNetworkBindingIntact() = handoverTest { handover ->
        val old = handover.instance("old", slowLaneMs = SLOW_LANE_MS)
        old.open()
        runCurrent()

        old.close()
        handover.instance("new").open()
        settle()

        assertEquals("new", handover.process.bound)
    }

    @Test
    fun aStuckTeardownDelaysTheNextInstanceOnlyBoundedly() = handoverTest { handover ->
        val old = handover.instance("old", slowLaneMs = STUCK_MS)
        old.open()
        runCurrent()
        old.close()
        val new = handover.instance("new")

        new.open()
        while (new.runtime.value == null) {
            advanceTimeBy(100)
            runCurrent()
        }

        assertTrue(currentTime < STUCK_MS)
    }

    @Test
    fun theSessionIsPublishedOnOpenAndWithdrawnOnClose() = handoverTest { handover ->
        val instance = handover.instance("one")
        assertNull(instance.session.value)

        instance.open()
        runCurrent()
        assertSame(instance.runtime.value?.session, instance.session.value)

        instance.close()
        settle()
        assertNull(instance.session.value)
        assertNull(instance.runtime.value)
    }

    @Test
    fun aStartBeforeTheEngineExistsIsAppliedOnceItDoes() = handoverTest { handover ->
        val instance = handover.instance("one")

        instance.open()
        instance.start(null)
        runCurrent()

        assertEquals(1, instance.runtime.value?.sessionStarts)
    }
}

class MonitorLifecycleOrderTest {
    @Test
    fun aHungStepDelaysTheNextOneOnlyByTheBound() = runTest {
        val scope = CoroutineScope(SupervisorJob() + StandardTestDispatcher(testScheduler))
        val lifecycle = MonitorLifecycle(scope, WAIT_MS)
        val ran = CopyOnWriteArrayList<Long>()
        try {
            lifecycle.enqueue { awaitCancellation() }
            lifecycle.enqueue { ran += currentTime }
            advanceTimeBy(WAIT_MS * 2)
            runCurrent()

            assertEquals(listOf(WAIT_MS), ran.toList())
        } finally {
            scope.coroutineContext.job.cancel()
        }
    }

    @Test
    fun stepsRunInTheOrderTheyWereEnqueued() = runTest {
        val scope = CoroutineScope(SupervisorJob() + StandardTestDispatcher(testScheduler))
        val lifecycle = MonitorLifecycle(scope, WAIT_MS)
        val order = CopyOnWriteArrayList<String>()
        try {
            lifecycle.enqueue {
                delay(500)
                order += "first"
            }
            lifecycle.enqueue { order += "second" }
            advanceTimeBy(1000)
            runCurrent()

            assertEquals(listOf("first", "second"), order.toList())
        } finally {
            scope.coroutineContext.job.cancel()
        }
    }
}

class OwnedBindingTest {
    @Test
    fun anOlderOwnerCannotReleaseANewerBinding() {
        val binding = OwnedBinding()
        val old = Any()
        val new = Any()
        var bound: String? = null

        binding.claim(old) { bound = "old"; true }
        binding.claim(new) { bound = "new"; true }
        binding.release(old) { bound = null }

        assertEquals("new", bound)
    }

    @Test
    fun theOwnerReleasesItsOwnBinding() {
        val binding = OwnedBinding()
        val owner = Any()
        var bound: String? = null

        binding.claim(owner) { bound = "mine"; true }
        binding.release(owner) { bound = null }

        assertNull(bound)
    }

    @Test
    fun aFailedClaimOwnsNothing() {
        val binding = OwnedBinding()
        val first = Any()
        val second = Any()
        var bound: String? = null

        binding.claim(first) { bound = "first"; true }
        binding.claim(second) { false }
        binding.release(second) { bound = null }

        assertEquals("first", bound)
    }
}
