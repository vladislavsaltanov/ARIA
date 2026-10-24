@file:OptIn(ExperimentalCoroutinesApi::class)

package aria.monitor.core

import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineExceptionHandler
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.TestCoroutineScheduler
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.advanceUntilIdle
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.withContext
import java.util.concurrent.CopyOnWriteArrayList
import java.util.concurrent.atomic.AtomicInteger
import kotlin.concurrent.thread
import kotlin.coroutines.cancellation.CancellationException
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

private const val TOKEN = "tok"
private const val TRACK_A = "track-a"
private const val TRACK_B = "track-b"
private const val TRACK_C = "track-c"
private const val TRACK_NEXT = "track-next"

private val FULL_CHUNK = PcmResult.Ok(ByteArray(CHUNK_FRAMES * 4), CHUNK_FRAMES)

private fun key(track: String, chunk: Int) = ChunkKey(track, chunk)

private fun window(track: String, firstChunk: Int, lastChunk: Int) =
    Window(track, firstChunk.toLong() * CHUNK_FRAMES, (lastChunk + 1L) * CHUNK_FRAMES)

private class FakeSource(private val clockMs: () -> Long) : PcmSource {
    data class Call(val token: String, val track: String, val fromFrame: Long, val frames: Int, val atMs: Long) {
        val key: ChunkKey get() = ChunkKey(track, (fromFrame / CHUNK_FRAMES).toInt())
    }

    val calls = CopyOnWriteArrayList<Call>()
    val cancelled = AtomicInteger()
    val maxInFlight = AtomicInteger()
    private val inFlight = AtomicInteger()

    @Volatile
    var respond: suspend (Call) -> PcmResult = { FULL_CHUNK }

    override suspend fun fetch(token: String, track: String, fromFrame: Long, frames: Int): PcmResult {
        val call = Call(token, track, fromFrame, frames, clockMs())
        calls += call
        maxInFlight.accumulateAndGet(inFlight.incrementAndGet(), ::maxOf)
        try {
            return respond(call)
        } catch (cancellation: CancellationException) {
            cancelled.incrementAndGet()
            throw cancellation
        } finally {
            inFlight.decrementAndGet()
        }
    }
}

private class FakeSink(private val clockMs: () -> Long) : ChunkSink {
    data class Write(val key: ChunkKey, val frames: Int, val size: Int)

    val writes = CopyOnWriteArrayList<Write>()
    val attempts = CopyOnWriteArrayList<Pair<ChunkKey, Long>>()
    val empties = CopyOnWriteArrayList<ChunkKey>()
    val retains = CopyOnWriteArrayList<Set<ChunkKey>>()
    val violations = CopyOnWriteArrayList<String>()

    @Volatile
    var accepts: (ChunkKey) -> Boolean = { true }

    @Volatile
    var beforeWrite: (ChunkKey) -> Unit = {}

    @Volatile
    private var lastRetained: Set<ChunkKey>? = null

    override fun write(key: ChunkKey, bytes: ByteArray, frames: Int): Boolean {
        attempts += key to clockMs()
        if (lastRetained?.contains(key) == false) violations += "write for $key outside the retained set"
        beforeWrite(key)
        if (!accepts(key)) return false
        writes += Write(key, frames, bytes.size)
        return true
    }

    override fun markEmpty(key: ChunkKey) {
        if (lastRetained?.contains(key) == false) violations += "markEmpty for $key outside the retained set"
        empties += key
    }

    override fun retain(keys: Set<ChunkKey>) {
        lastRetained = keys
        retains += keys
    }
}

private class Harness(
    scope: CoroutineScope,
    scheduler: TestCoroutineScheduler,
    token: String? = TOKEN,
    capacity: Int = POOL_SLOTS,
) {
    val source = FakeSource { scheduler.currentTime }
    val sink = FakeSink { scheduler.currentTime }
    val unauthorized = AtomicInteger()
    val loader = ChunkLoader(
        scope,
        source,
        sink,
        { scheduler.currentTime },
        onUnauthorized = { unauthorized.incrementAndGet() },
        capacity = capacity,
    )

    init {
        loader.setToken(token)
    }
}

private fun TestScope.advance(ms: Long) {
    advanceTimeBy(ms)
    runCurrent()
}

class ChunkLoaderTest {
    @Test
    fun retries503After200ms() = runTest {
        val h = Harness(backgroundScope, testScheduler)
        h.source.respond = { if (h.source.calls.size == 1) PcmResult.Failed(503) else FULL_CHUNK }
        h.loader.want(listOf(window(TRACK_A, 0, 0)))
        runCurrent()
        assertEquals(1, h.source.calls.size)
        advance(199)
        assertEquals(1, h.source.calls.size)
        advance(1)
        assertEquals(2, h.source.calls.size)
        assertEquals(200L, h.source.calls[1].atMs)
        assertEquals(listOf(key(TRACK_A, 0)), h.sink.writes.map { it.key })
        advance(10_000)
        assertEquals(2, h.source.calls.size)
    }

    @Test
    fun otherFailureWaitsOneSecondPerChunk() = runTest {
        val h = Harness(backgroundScope, testScheduler)
        h.source.respond = { call ->
            if (call.key.chunk == 0 && h.source.calls.count { it.key.chunk == 0 } == 1) PcmResult.Failed(500) else FULL_CHUNK
        }
        h.loader.want(listOf(window(TRACK_A, 0, 1)))
        runCurrent()
        assertEquals(listOf(key(TRACK_A, 0), key(TRACK_A, 1)), h.source.calls.map { it.key })
        assertEquals(listOf(key(TRACK_A, 1)), h.sink.writes.map { it.key })
        advance(999)
        assertEquals(2, h.source.calls.size)
        advance(1)
        assertEquals(3, h.source.calls.size)
        assertEquals(key(TRACK_A, 0), h.source.calls[2].key)
        assertEquals(1000L, h.source.calls[2].atMs)
        assertEquals(setOf(key(TRACK_A, 0), key(TRACK_A, 1)), h.sink.writes.map { it.key }.toSet())
    }

    @Test
    fun everyNonRetryableFailureCoolsDownForOneSecond() = runTest {
        for (status in listOf(400, 403, 404, 500, 502)) {
            val h = Harness(backgroundScope, testScheduler)
            h.source.respond = { if (h.source.calls.size == 1) PcmResult.Failed(status) else FULL_CHUNK }
            val start = testScheduler.currentTime
            h.loader.want(listOf(window(TRACK_A, 0, 0)))
            runCurrent()
            advance(999)
            assertEquals(1, h.source.calls.size, "status $status")
            advance(1)
            assertEquals(2, h.source.calls.size, "status $status")
            assertEquals(start + 1000, h.source.calls[1].atMs, "status $status")
            assertTrue(h.sink.empties.isEmpty(), "status $status")
            h.loader.want(emptyList())
        }
    }

    @Test
    fun thrownExceptionWaitsOneSecond() = runTest {
        val h = Harness(backgroundScope, testScheduler)
        h.source.respond = { if (h.source.calls.size == 1) throw IllegalStateException("socket reset") else FULL_CHUNK }
        h.loader.want(listOf(window(TRACK_A, 0, 0)))
        runCurrent()
        advance(999)
        assertEquals(1, h.source.calls.size)
        advance(1)
        assertEquals(2, h.source.calls.size)
        assertEquals(1000L, h.source.calls[1].atMs)
        assertEquals(1, h.sink.writes.size)
    }

    @Test
    fun cancellationInsideTheSourceIsAFailureAndFreesTheSlot() = runTest {
        val h = Harness(backgroundScope, testScheduler)
        h.source.respond = { call ->
            if (call.key.chunk < 2 && h.source.calls.count { it.key == call.key } == 1) throw CancellationException("inner timeout") else FULL_CHUNK
        }
        h.loader.want(listOf(window(TRACK_A, 0, 2)))
        runCurrent()
        assertEquals(listOf(key(TRACK_A, 0), key(TRACK_A, 1), key(TRACK_A, 2)), h.source.calls.map { it.key })
        assertEquals(listOf(key(TRACK_A, 2)), h.sink.writes.map { it.key })
        advance(999)
        assertEquals(3, h.source.calls.size)
        advance(1)
        assertEquals(5, h.source.calls.size)
        assertEquals(listOf(1000L, 1000L), h.source.calls.drop(3).map { it.atMs })
        assertEquals(setOf(key(TRACK_A, 0), key(TRACK_A, 1), key(TRACK_A, 2)), h.sink.writes.map { it.key }.toSet())
    }

    @Test
    fun undecodable422MarksEmptyAndNeverRetries() = runTest {
        val h = Harness(backgroundScope, testScheduler)
        h.source.respond = { PcmResult.Failed(422) }
        h.loader.want(listOf(window(TRACK_A, 0, 0)))
        runCurrent()
        assertEquals(listOf(key(TRACK_A, 0)), h.sink.empties)
        assertTrue(h.sink.writes.isEmpty())
        advance(60_000)
        h.loader.want(listOf(window(TRACK_A, 0, 0)))
        advance(60_000)
        assertEquals(1, h.source.calls.size)
        assertEquals(1, h.sink.empties.size)
    }

    @Test
    fun neverMoreThanTwoInFlight() = runTest {
        val h = Harness(backgroundScope, testScheduler)
        h.source.respond = {
            delay(100)
            FULL_CHUNK
        }
        h.loader.want(listOf(window(TRACK_A, 0, 5)))
        runCurrent()
        assertEquals(2, h.source.calls.size)
        advance(1000)
        assertEquals(2, h.source.maxInFlight.get())
        assertEquals(6, h.sink.writes.size)
        assertEquals(listOf(0L, 0L, 100L, 100L, 200L, 200L), h.source.calls.map { it.atMs })
    }

    @Test
    fun retainReceivesOnlyWantedKeys() = runTest {
        val h = Harness(backgroundScope, testScheduler)
        h.loader.want(listOf(window(TRACK_A, 0, 2), window(TRACK_B, 0, 0), window(TRACK_A, 1, 3)))
        runCurrent()
        assertEquals(
            setOf(key(TRACK_A, 0), key(TRACK_A, 1), key(TRACK_A, 2), key(TRACK_A, 3), key(TRACK_B, 0)),
            h.sink.retains.last(),
        )
        h.loader.want(listOf(window(TRACK_A, 2, 3)))
        assertEquals(setOf(key(TRACK_A, 2), key(TRACK_A, 3)), h.sink.retains.last())
        h.loader.want(emptyList())
        assertEquals(emptySet(), h.sink.retains.last())
        assertTrue(h.sink.violations.isEmpty())
    }

    @Test
    fun retainIsCalledEvenWithoutAToken() = runTest {
        val h = Harness(backgroundScope, testScheduler, token = null)
        h.loader.want(listOf(window(TRACK_A, 0, 1)))
        advance(5000)
        assertEquals(setOf(key(TRACK_A, 0), key(TRACK_A, 1)), h.sink.retains.last())
        assertTrue(h.source.calls.isEmpty())
    }

    @Test
    fun zeroFramesResponseMarksEmpty() = runTest {
        val h = Harness(backgroundScope, testScheduler)
        h.source.respond = { PcmResult.Ok(ByteArray(0), 0) }
        h.loader.want(listOf(window(TRACK_A, 3, 3)))
        runCurrent()
        assertEquals(listOf(key(TRACK_A, 3)), h.sink.empties)
        assertTrue(h.sink.writes.isEmpty())
        advance(60_000)
        assertEquals(1, h.source.calls.size)
        assertEquals(1, h.sink.empties.size)
        assertEquals(0, h.unauthorized.get())
    }

    @Test
    fun shortFinalChunkWritesActualFrames() = runTest {
        val h = Harness(backgroundScope, testScheduler)
        h.source.respond = { PcmResult.Ok(ByteArray(1000 * 4), 1000) }
        h.loader.want(listOf(window(TRACK_A, 7, 7)))
        runCurrent()
        assertEquals(CHUNK_FRAMES, h.source.calls.single().frames)
        assertEquals(listOf(FakeSink.Write(key(TRACK_A, 7), 1000, 4000)), h.sink.writes.toList())
        assertTrue(h.sink.empties.isEmpty())
        advance(60_000)
        assertEquals(1, h.source.calls.size)
        assertEquals(1, h.sink.writes.size)
    }

    @Test
    fun deliveredKeysAreNotRefetchedWhileWanted() = runTest {
        val h = Harness(backgroundScope, testScheduler)
        h.loader.want(listOf(window(TRACK_A, 0, 1)))
        runCurrent()
        h.loader.want(listOf(window(TRACK_A, 0, 1)))
        h.loader.want(listOf(window(TRACK_A, 1, 2)))
        advance(1000)
        assertEquals(listOf(key(TRACK_A, 0), key(TRACK_A, 1), key(TRACK_A, 2)), h.source.calls.map { it.key })
    }

    @Test
    fun deliveredKeyIsForgottenWhenNoLongerWantedAndFetchedAgain() = runTest {
        val h = Harness(backgroundScope, testScheduler)
        h.loader.want(listOf(window(TRACK_A, 0, 0)))
        runCurrent()
        h.loader.want(listOf(window(TRACK_A, 1, 1)))
        runCurrent()
        h.loader.want(listOf(window(TRACK_A, 0, 0)))
        runCurrent()
        assertEquals(listOf(key(TRACK_A, 0), key(TRACK_A, 1), key(TRACK_A, 0)), h.source.calls.map { it.key })
    }

    @Test
    fun wantedOrderIsFetchOrder() = runTest {
        val h = Harness(backgroundScope, testScheduler)
        h.source.respond = {
            delay(10)
            FULL_CHUNK
        }
        val wanted = listOf(
            window(TRACK_B, 5, 5),
            window(TRACK_A, 2, 2),
            window(TRACK_A, 0, 1),
            window(TRACK_NEXT, 0, 0),
            window(TRACK_B, 5, 6),
        )
        h.loader.want(wanted)
        advance(1000)
        assertEquals(
            listOf(
                key(TRACK_B, 5),
                key(TRACK_A, 2),
                key(TRACK_A, 0),
                key(TRACK_A, 1),
                key(TRACK_NEXT, 0),
                key(TRACK_B, 6),
            ),
            h.source.calls.map { it.key },
        )
    }

    @Test
    fun rewantingReordersThePendingQueue() = runTest {
        val h = Harness(backgroundScope, testScheduler)
        val gate = CompletableDeferred<Unit>()
        h.source.respond = {
            gate.await()
            FULL_CHUNK
        }
        h.loader.want(listOf(window(TRACK_A, 0, 3)))
        runCurrent()
        h.loader.want(listOf(window(TRACK_B, 0, 0), window(TRACK_A, 0, 3)))
        gate.complete(Unit)
        runCurrent()
        assertEquals(
            listOf(key(TRACK_A, 0), key(TRACK_A, 1), key(TRACK_B, 0), key(TRACK_A, 2), key(TRACK_A, 3)),
            h.source.calls.map { it.key },
        )
    }

    @Test
    fun positionsBeyondInt32RequestTheRightFromFrame() = runTest {
        val h = Harness(backgroundScope, testScheduler)
        h.loader.want(listOf(window(TRACK_A, 40_000, 40_000)))
        runCurrent()
        val call = h.source.calls.single()
        assertEquals(3_840_000_000L, call.fromFrame)
        assertEquals(CHUNK_FRAMES, call.frames)
        assertEquals(TRACK_A, call.track)
        assertEquals(TOKEN, call.token)
    }

    @Test
    fun sinkRejectionRetriesAfter200msAndNeverMarksEmpty() = runTest {
        val h = Harness(backgroundScope, testScheduler)
        h.sink.accepts = { it != key(TRACK_A, 0) }
        h.loader.want(listOf(window(TRACK_A, 0, 1)))
        runCurrent()
        assertEquals(listOf(key(TRACK_A, 1)), h.sink.writes.map { it.key })
        assertEquals(2, h.source.calls.size)
        advance(199)
        assertEquals(2, h.source.calls.size)
        advance(1)
        assertEquals(3, h.source.calls.size)
        assertEquals(key(TRACK_A, 0), h.source.calls[2].key)
        assertEquals(200L, h.source.calls[2].atMs)
        advance(200)
        assertEquals(4, h.source.calls.size)
        assertEquals(400L, h.source.calls[3].atMs)
        h.sink.accepts = { true }
        advance(200)
        assertEquals(setOf(key(TRACK_A, 0), key(TRACK_A, 1)), h.sink.writes.map { it.key }.toSet())
        assertEquals(5, h.source.calls.size)
        advance(60_000)
        assertEquals(5, h.source.calls.size)
        assertTrue(h.sink.empties.isEmpty())
    }

    @Test
    fun unauthorizedStopsFetchingAndNotifiesOnce() = runTest {
        val h = Harness(backgroundScope, testScheduler)
        h.source.respond = { PcmResult.Failed(401) }
        h.loader.want(listOf(window(TRACK_A, 0, 5)))
        runCurrent()
        assertEquals(1, h.unauthorized.get())
        val callsAtStop = h.source.calls.size
        assertTrue(callsAtStop in 1..2)
        advance(60_000)
        h.loader.want(listOf(window(TRACK_A, 0, 9)))
        advance(60_000)
        assertEquals(callsAtStop, h.source.calls.size)
        assertEquals(1, h.unauthorized.get())
        h.loader.setToken(TOKEN)
        advance(60_000)
        h.loader.setToken(null)
        h.loader.setToken(TOKEN)
        advance(60_000)
        assertEquals(callsAtStop, h.source.calls.size)
        assertEquals(1, h.unauthorized.get())
        assertTrue(h.sink.empties.isEmpty())
        assertTrue(h.sink.writes.isEmpty())
    }

    @Test
    fun fetchResumesAfterNewToken() = runTest {
        val h = Harness(backgroundScope, testScheduler)
        h.source.respond = { call -> if (call.token == "tok2") FULL_CHUNK else PcmResult.Failed(401) }
        h.loader.want(listOf(window(TRACK_A, 0, 3)))
        runCurrent()
        assertEquals(1, h.unauthorized.get())
        h.loader.setToken("tok2")
        runCurrent()
        assertEquals(setOf(key(TRACK_A, 0), key(TRACK_A, 1), key(TRACK_A, 2), key(TRACK_A, 3)), h.sink.writes.map { it.key }.toSet())
        assertEquals(4, h.source.calls.count { it.token == "tok2" })
        assertEquals(1, h.unauthorized.get())
        h.source.respond = { PcmResult.Failed(401) }
        h.loader.setToken("tok3")
        h.loader.want(listOf(window(TRACK_A, 0, 4)))
        runCurrent()
        assertEquals(2, h.unauthorized.get())
        val callsAtSecondStop = h.source.calls.size
        advance(60_000)
        assertEquals(callsAtSecondStop, h.source.calls.size)
    }

    @Test
    fun tokenChangeRestartsPendingFetches() = runTest {
        val h = Harness(backgroundScope, testScheduler, token = "t1")
        val gate = CompletableDeferred<Unit>()
        h.source.respond = {
            gate.await()
            FULL_CHUNK
        }
        h.loader.want(listOf(window(TRACK_A, 0, 3)))
        runCurrent()
        assertEquals(listOf("t1", "t1"), h.source.calls.map { it.token })
        h.loader.setToken("t2")
        runCurrent()
        assertEquals(2, h.source.cancelled.get())
        assertEquals(listOf("t1", "t1", "t2", "t2"), h.source.calls.map { it.token })
        assertEquals(listOf(key(TRACK_A, 0), key(TRACK_A, 1)), h.source.calls.drop(2).map { it.key })
        gate.complete(Unit)
        runCurrent()
        assertEquals(setOf(key(TRACK_A, 0), key(TRACK_A, 1), key(TRACK_A, 2), key(TRACK_A, 3)), h.sink.writes.map { it.key }.toSet())
        assertEquals(4, h.sink.writes.size)
        assertEquals(6, h.source.calls.size)
        assertTrue(h.source.calls.drop(2).all { it.token == "t2" })
    }

    @Test
    fun tokenChangeClearsCooldowns() = runTest {
        val h = Harness(backgroundScope, testScheduler, token = "t1")
        h.source.respond = { call -> if (call.token == "t1") PcmResult.Failed(500) else FULL_CHUNK }
        h.loader.want(listOf(window(TRACK_A, 0, 0)))
        runCurrent()
        advance(500)
        assertEquals(1, h.source.calls.size)
        h.loader.setToken("t2")
        runCurrent()
        assertEquals(2, h.source.calls.size)
        assertEquals(500L, h.source.calls[1].atMs)
        assertEquals(1, h.sink.writes.size)
    }

    @Test
    fun nullTokenPausesFetching() = runTest {
        val h = Harness(backgroundScope, testScheduler, token = null)
        h.loader.want(listOf(window(TRACK_A, 0, 1)))
        advance(5000)
        assertTrue(h.source.calls.isEmpty())
        val gate = CompletableDeferred<Unit>()
        h.source.respond = {
            gate.await()
            FULL_CHUNK
        }
        h.loader.setToken("t1")
        runCurrent()
        assertEquals(2, h.source.calls.size)
        h.loader.setToken(null)
        runCurrent()
        assertEquals(2, h.source.cancelled.get())
        gate.complete(Unit)
        advance(5000)
        assertEquals(2, h.source.calls.size)
        assertTrue(h.sink.writes.isEmpty())
        h.loader.setToken("t1")
        runCurrent()
        assertEquals(4, h.source.calls.size)
        assertEquals(2, h.sink.writes.size)
    }

    @Test
    fun settingTheSameTokenAgainChangesNothing() = runTest {
        val h = Harness(backgroundScope, testScheduler)
        val gate = CompletableDeferred<Unit>()
        h.source.respond = {
            gate.await()
            FULL_CHUNK
        }
        h.loader.want(listOf(window(TRACK_A, 0, 1)))
        runCurrent()
        h.loader.setToken(TOKEN)
        runCurrent()
        assertEquals(0, h.source.cancelled.get())
        assertEquals(2, h.source.calls.size)
    }

    @Test
    fun inFlightFetchIsCancelledWhenItsWantDisappears() = runTest {
        val h = Harness(backgroundScope, testScheduler)
        val gate = CompletableDeferred<Unit>()
        h.source.respond = {
            gate.await()
            FULL_CHUNK
        }
        h.loader.want(listOf(window(TRACK_A, 0, 0)))
        runCurrent()
        assertEquals(1, h.source.calls.size)
        h.loader.want(emptyList())
        runCurrent()
        assertEquals(1, h.source.cancelled.get())
        gate.complete(Unit)
        runCurrent()
        assertTrue(h.sink.writes.isEmpty())
        h.loader.want(listOf(window(TRACK_A, 0, 0)))
        runCurrent()
        assertEquals(2, h.source.calls.size)
        assertEquals(0L, h.source.calls[1].atMs)
        assertEquals(1, h.sink.writes.size)
    }

    @Test
    fun cancelledFetchFreesItsParallelSlot() = runTest {
        val h = Harness(backgroundScope, testScheduler)
        val gate = CompletableDeferred<Unit>()
        h.source.respond = {
            gate.await()
            FULL_CHUNK
        }
        h.loader.want(listOf(window(TRACK_A, 0, 3)))
        runCurrent()
        assertEquals(2, h.source.calls.size)
        h.loader.want(listOf(window(TRACK_A, 1, 3)))
        runCurrent()
        assertEquals(listOf(key(TRACK_A, 0), key(TRACK_A, 1), key(TRACK_A, 2)), h.source.calls.map { it.key })
    }

    @Test
    fun resultOfNonCancellableFetchIsDiscardedAfterItsWantDisappears() = runTest {
        val h = Harness(backgroundScope, testScheduler)
        val gate = CompletableDeferred<Unit>()
        h.source.respond = {
            withContext(NonCancellable) { gate.await() }
            FULL_CHUNK
        }
        h.loader.want(listOf(window(TRACK_A, 0, 0)))
        runCurrent()
        assertEquals(1, h.source.calls.size)
        h.loader.want(emptyList())
        runCurrent()
        gate.complete(Unit)
        runCurrent()
        assertTrue(h.sink.writes.isEmpty())
        assertTrue(h.sink.attempts.isEmpty())
        assertTrue(h.sink.violations.isEmpty())
    }

    @Test
    fun cooldownWakeComesFromDelayNotPolling() = runTest {
        val scope = CoroutineScope(StandardTestDispatcher(testScheduler) + SupervisorJob())
        val h = Harness(scope, testScheduler)
        h.source.respond = { if (h.source.calls.size == 1) PcmResult.Failed(500) else FULL_CHUNK }
        h.loader.want(listOf(window(TRACK_A, 0, 0)))
        advanceUntilIdle()
        scope.cancel()
        assertEquals(2, h.source.calls.size)
        assertEquals(1000L, testScheduler.currentTime)
        assertEquals(1, h.sink.writes.size)
    }

    @Test
    fun overCapacityFetchesOnlyTopNInPriorityOrder() = runTest {
        val h = Harness(backgroundScope, testScheduler)
        h.source.respond = {
            delay(10)
            FULL_CHUNK
        }
        val windows = listOf(window(TRACK_A, 0, 10), window(TRACK_B, 0, 10), window(TRACK_C, 0, 10), window(TRACK_NEXT, 0, 2))
        assertEquals(36, windows.flatMap { chunksFor(it) }.size)
        val topN = windows.flatMap { chunksFor(it) }.take(POOL_SLOTS)
        h.loader.want(windows)
        assertEquals(topN.toSet(), h.sink.retains.last())
        advance(10_000)
        assertEquals(POOL_SLOTS, topN.size)
        assertEquals(topN, h.source.calls.map { it.key })
        assertEquals(topN.toSet(), h.sink.writes.map { it.key }.toSet())
        assertEquals(POOL_SLOTS, h.sink.writes.size)
        advance(60_000)
        assertEquals(POOL_SLOTS, h.source.calls.size)
        assertTrue(h.sink.empties.isEmpty())
    }

    @Test
    fun keysBeyondCapacityAreNeverRequested() = runTest {
        val h = Harness(backgroundScope, testScheduler)
        h.source.respond = {
            delay(10)
            FULL_CHUNK
        }
        for (step in 0..20) {
            val windows = listOf(
                window(TRACK_A, step, step + 10),
                window(TRACK_B, 0, 10),
                window(TRACK_C, step / 2, step / 2 + 10),
                window(TRACK_NEXT, 0, 2),
            )
            val allowed = windows.flatMap { chunksFor(it) }.distinct().take(POOL_SLOTS).toSet()
            val before = h.source.calls.size
            h.loader.want(windows)
            advance(25)
            val requested = h.source.calls.drop(before).map { it.key }
            assertTrue(allowed.containsAll(requested), "step $step requested outside the top $POOL_SLOTS: ${requested - allowed}")
            assertEquals(allowed, h.sink.retains.last(), "step $step")
        }
        assertTrue(h.sink.violations.isEmpty(), h.sink.violations.toString())
    }

    @Test
    fun higherPriorityKeyEvictsLowerPriorityInFlight() = runTest {
        val h = Harness(backgroundScope, testScheduler, capacity = 2)
        val gate = CompletableDeferred<Unit>()
        h.source.respond = {
            gate.await()
            FULL_CHUNK
        }
        h.loader.want(listOf(window(TRACK_B, 0, 1)))
        runCurrent()
        assertEquals(listOf(key(TRACK_B, 0), key(TRACK_B, 1)), h.source.calls.map { it.key })
        h.loader.want(listOf(window(TRACK_A, 0, 0), window(TRACK_B, 0, 1)))
        runCurrent()
        assertEquals(setOf(key(TRACK_A, 0), key(TRACK_B, 0)), h.sink.retains.last())
        assertEquals(1, h.source.cancelled.get())
        assertEquals(listOf(key(TRACK_B, 0), key(TRACK_B, 1), key(TRACK_A, 0)), h.source.calls.map { it.key })
        gate.complete(Unit)
        runCurrent()
        assertEquals(setOf(key(TRACK_A, 0), key(TRACK_B, 0)), h.sink.writes.map { it.key }.toSet())
        assertEquals(2, h.sink.writes.size)
        assertTrue(h.sink.attempts.none { it.first == key(TRACK_B, 1) })
        advance(60_000)
        assertEquals(3, h.source.calls.size)
    }

    @Test
    fun capacityParameterIsHonoured() = runTest {
        val h = Harness(backgroundScope, testScheduler, capacity = 3)
        h.loader.want(listOf(window(TRACK_A, 0, 9)))
        runCurrent()
        assertEquals(setOf(key(TRACK_A, 0), key(TRACK_A, 1), key(TRACK_A, 2)), h.sink.retains.last())
        advance(5000)
        assertEquals(listOf(key(TRACK_A, 0), key(TRACK_A, 1), key(TRACK_A, 2)), h.source.calls.map { it.key })
        assertEquals(3, h.sink.writes.size)
        h.loader.want(listOf(window(TRACK_A, 5, 9)))
        advance(5000)
        assertEquals(setOf(key(TRACK_A, 5), key(TRACK_A, 6), key(TRACK_A, 7)), h.sink.retains.last())
        assertEquals(
            listOf(key(TRACK_A, 0), key(TRACK_A, 1), key(TRACK_A, 2), key(TRACK_A, 5), key(TRACK_A, 6), key(TRACK_A, 7)),
            h.source.calls.map { it.key },
        )
    }

    @Test
    fun cancelWithUnconfinedDispatcherAndSwallowingSourceDoesNotThrow() = runTest {
        val scope = CoroutineScope(SupervisorJob() + Dispatchers.Unconfined)
        try {
            val h = Harness(scope, testScheduler)
            val gate = CompletableDeferred<Unit>()
            h.source.respond = {
                try {
                    gate.await()
                } catch (_: CancellationException) {
                }
                FULL_CHUNK
            }
            h.loader.want(listOf(window(TRACK_A, 0, 3)))
            assertEquals(2, h.source.calls.size)
            h.loader.setToken("t2")
            assertEquals(listOf(TOKEN, TOKEN, "t2", "t2"), h.source.calls.map { it.token })
            assertTrue(h.sink.writes.isEmpty())
            h.loader.want(listOf(window(TRACK_B, 0, 1)))
            assertEquals(listOf(TOKEN, TOKEN, "t2", "t2", "t2", "t2"), h.source.calls.map { it.token })
            assertTrue(h.sink.writes.isEmpty())
            gate.complete(Unit)
            assertEquals(setOf(key(TRACK_B, 0), key(TRACK_B, 1)), h.sink.writes.map { it.key }.toSet())
            h.loader.want(listOf(window(TRACK_A, 0, 3)))
            assertEquals(6, h.sink.writes.size)
            assertEquals(setOf(key(TRACK_A, 0), key(TRACK_A, 1), key(TRACK_A, 2), key(TRACK_A, 3)), h.sink.retains.last())
            assertTrue(h.sink.violations.isEmpty(), h.sink.violations.toString())
        } finally {
            scope.cancel()
        }
    }

    @Test
    fun unauthorizedHaltWithUnconfinedDispatcherAndSwallowingSourceDoesNotThrow() = runTest {
        val scope = CoroutineScope(SupervisorJob() + Dispatchers.Unconfined)
        try {
            val h = Harness(scope, testScheduler)
            val gate = CompletableDeferred<Unit>()
            h.source.respond = { call ->
                if (call.token == TOKEN && call.key.chunk == 0) {
                    gate.await()
                    PcmResult.Failed(401)
                } else {
                    try {
                        gate.await()
                    } catch (_: CancellationException) {
                    }
                    FULL_CHUNK
                }
            }
            h.loader.want(listOf(window(TRACK_A, 0, 3)))
            gate.complete(Unit)
            assertEquals(1, h.unauthorized.get())
            assertTrue(h.sink.writes.isEmpty())
            h.loader.setToken("t2")
            assertEquals(setOf(key(TRACK_A, 0), key(TRACK_A, 1), key(TRACK_A, 2), key(TRACK_A, 3)), h.sink.writes.map { it.key }.toSet())
            assertEquals(4, h.sink.writes.size)
            assertEquals(1, h.unauthorized.get())
        } finally {
            scope.cancel()
        }
    }

    @Test
    fun throwingSinkDoesNotStallOtherKeys() = runTest {
        val failures = CopyOnWriteArrayList<Throwable>()
        val scope = CoroutineScope(
            StandardTestDispatcher(testScheduler) + SupervisorJob() + CoroutineExceptionHandler { _, error -> failures += error },
        )
        try {
            val h = Harness(scope, testScheduler)
            val gate = CompletableDeferred<Unit>()
            h.source.respond = { call ->
                if (call.key.chunk == 1) gate.await()
                FULL_CHUNK
            }
            h.sink.beforeWrite = { if (it == key(TRACK_A, 0)) throw IllegalStateException("engine bug") }
            h.loader.want(listOf(window(TRACK_A, 0, 2)))
            runCurrent()
            assertEquals(1, failures.size)
            assertEquals(listOf(key(TRACK_A, 2)), h.sink.writes.map { it.key })
            advance(999)
            assertEquals(1, h.source.calls.count { it.key == key(TRACK_A, 0) })
            advance(1)
            assertEquals(2, h.source.calls.count { it.key == key(TRACK_A, 0) })
            assertEquals(2, failures.size)
            gate.complete(Unit)
            runCurrent()
            assertEquals(setOf(key(TRACK_A, 1), key(TRACK_A, 2)), h.sink.writes.map { it.key }.toSet())
        } finally {
            scope.cancel()
        }
    }

    @Test
    fun inconsistentBytesAndFramesIsAFailure() = runTest {
        val h = Harness(backgroundScope, testScheduler)
        val bad = listOf(PcmResult.Ok(ByteArray(100), 1000), PcmResult.Ok(ByteArray(0), -5), PcmResult.Ok(ByteArray(4), 0))
        h.source.respond = { bad.getOrElse(h.source.calls.size - 1) { FULL_CHUNK } }
        h.loader.want(listOf(window(TRACK_A, 3, 3)))
        runCurrent()
        for (attempt in 1..3) {
            assertEquals(attempt, h.source.calls.size)
            assertTrue(h.sink.writes.isEmpty())
            assertTrue(h.sink.empties.isEmpty())
            advance(999)
            assertEquals(attempt, h.source.calls.size)
            advance(1)
        }
        assertEquals(listOf(0L, 1000L, 2000L, 3000L), h.source.calls.map { it.atMs })
        assertEquals(listOf(FakeSink.Write(key(TRACK_A, 3), CHUNK_FRAMES, CHUNK_FRAMES * 4)), h.sink.writes.toList())
        assertTrue(h.sink.empties.isEmpty())
    }

    @Test
    fun framesAboveChunkSizeIsAFailure() = runTest {
        val h = Harness(backgroundScope, testScheduler)
        h.source.respond = {
            if (h.source.calls.size == 1) PcmResult.Ok(ByteArray((CHUNK_FRAMES + 1) * 4), CHUNK_FRAMES + 1) else FULL_CHUNK
        }
        h.loader.want(listOf(window(TRACK_A, 0, 0)))
        runCurrent()
        assertTrue(h.sink.writes.isEmpty())
        assertTrue(h.sink.empties.isEmpty())
        advance(999)
        assertEquals(1, h.source.calls.size)
        advance(1)
        assertEquals(2, h.source.calls.size)
        assertEquals(1000L, h.source.calls[1].atMs)
        assertEquals(listOf(FakeSink.Write(key(TRACK_A, 0), CHUNK_FRAMES, CHUNK_FRAMES * 4)), h.sink.writes.toList())
    }

    @Test
    fun concurrentWantsWhileFetchesCompleteStayConsistent() {
        val clockMs = { System.nanoTime() / 1_000_000 }
        val source = FakeSource(clockMs)
        val sink = FakeSink(clockMs)
        source.respond = {
            delay(1)
            FULL_CHUNK
        }
        val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
        try {
            val loader = ChunkLoader(scope, source, sink, clockMs)
            loader.setToken(TOKEN)
            val windows = listOf(window(TRACK_A, 0, 7), window(TRACK_B, 0, 7))
            val expected = windows.flatMap { chunksFor(it) }.toSet()
            assertEquals(16, expected.size)
            val workers = List(4) { thread { repeat(3000) { loader.want(windows) } } }
            workers.forEach { it.join() }
            awaitUntil { sink.writes.size >= expected.size }
            Thread.sleep(100)
            assertEquals(expected, sink.writes.map { it.key }.toSet())
            assertEquals(expected.size, sink.writes.size)
            assertEquals(expected.size, source.calls.size)
            assertTrue(source.maxInFlight.get() <= 2, "in flight peaked at ${source.maxInFlight.get()}")
            assertTrue(sink.retains.all { it == expected })
            assertTrue(sink.violations.isEmpty(), sink.violations.toString())
        } finally {
            scope.cancel()
        }
    }

    @Test
    fun concurrentChangingWantsAndTokensNeverWriteOutsideTheWantedSet() {
        val clockMs = { System.nanoTime() / 1_000_000 }
        val source = FakeSource(clockMs)
        val sink = FakeSink(clockMs)
        source.respond = {
            delay(1)
            FULL_CHUNK
        }
        val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
        try {
            val loader = ChunkLoader(scope, source, sink, clockMs)
            loader.setToken("t1")
            val first = listOf(window(TRACK_A, 0, 7), window(TRACK_NEXT, 0, 2))
            val second = listOf(window(TRACK_A, 4, 11), window(TRACK_B, 0, 2))
            assertEquals(11, first.flatMap { chunksFor(it) }.toSet().size)
            val allowed = setOf(first, second).map { set -> set.flatMap { chunksFor(it) }.toSet() }.toSet()
            val wanters = List(3) { index ->
                thread { repeat(2000) { step -> loader.want(if ((step + index) % 2 == 0) first else second) } }
            }
            val tokens = thread { repeat(400) { step -> loader.setToken(if (step % 2 == 0) "t2" else "t1") } }
            (wanters + tokens).forEach { it.join() }
            loader.setToken("t1")
            loader.want(first)
            val expected = first.flatMap { chunksFor(it) }.toSet()
            awaitUntil { sink.writes.map { it.key }.toSet().containsAll(expected) }
            assertTrue(sink.retains.all { it in allowed })
            assertTrue(sink.violations.isEmpty(), sink.violations.toString())
        } finally {
            scope.cancel()
        }
    }

    private fun awaitUntil(timeoutMs: Long = 10_000, condition: () -> Boolean) {
        val deadline = System.nanoTime() + timeoutMs * 1_000_000
        while (!condition()) {
            assertTrue(System.nanoTime() < deadline, "condition not reached within $timeoutMs ms")
            Thread.sleep(5)
        }
    }
}
