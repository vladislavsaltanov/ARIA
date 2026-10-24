package aria.monitor.core

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.Job
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.launch
import kotlin.math.min

sealed interface PcmResult {
    class Ok(val bytes: ByteArray, val frames: Int) : PcmResult

    class Failed(val status: Int) : PcmResult
}

interface PcmSource {
    suspend fun fetch(token: String, track: String, fromFrame: Long, frames: Int): PcmResult
}

interface ChunkSink {
    fun write(key: ChunkKey, bytes: ByteArray, frames: Int): Boolean

    fun markEmpty(key: ChunkKey)

    fun retain(keys: Set<ChunkKey>)
}

private const val MAX_IN_FLIGHT = 2
private const val RETRY_MS = 200L
private const val FAILURE_COOLDOWN_MS = 1000L
private const val STATUS_UNAUTHORIZED = 401
private const val STATUS_UNDECODABLE = 422
private const val STATUS_UNAVAILABLE = 503
private const val STATUS_TRANSPORT_ERROR = -1
private const val BYTES_PER_FRAME = 4

class ChunkLoader(
    private val scope: CoroutineScope,
    private val source: PcmSource,
    private val sink: ChunkSink,
    private val nowMs: () -> Long,
    private val onUnauthorized: () -> Unit = {},
    private val capacity: Int = POOL_SLOTS,
) {
    private class Attempt(val id: Long, val job: Job)

    private val lock = Any()
    private var token: String? = null
    private var rejectedToken: String? = null
    private var wanted: List<ChunkKey> = emptyList()
    private val delivered = HashSet<ChunkKey>()
    private val inFlight = HashMap<ChunkKey, Attempt>()
    private val cooldownUntilMs = HashMap<ChunkKey, Long>()
    private var wake: Job? = null
    private var attempts = 0L
    private val cancelling = ArrayList<Job>()

    fun want(windows: List<Window>) {
        val ordered = windows.flatMap { chunksFor(it) }.distinct().take(capacity)
        val keys = ordered.toSet()
        transition {
            sink.retain(keys)
            wanted = ordered
            delivered.retainAll(keys)
            inFlight.keys.filter { it !in keys }.forEach { evicted -> inFlight.remove(evicted)?.let { cancelling += it.job } }
        }
    }

    fun setToken(token: String?) {
        transition {
            if (token == this.token) return@transition
            this.token = token
            cancelAll()
            cooldownUntilMs.clear()
        }
    }

    private inline fun transition(change: () -> Unit) {
        var toStart: List<Job> = emptyList()
        var toCancel: List<Job> = emptyList()
        try {
            synchronized(lock) {
                try {
                    change()
                } finally {
                    toStart = pump()
                    toCancel = cancelling.toList()
                    cancelling.clear()
                }
            }
        } finally {
            toCancel.forEach { it.cancel() }
            toStart.forEach { it.start() }
        }
    }

    private fun cancelAll() {
        inFlight.values.mapTo(cancelling) { it.job }
        inFlight.clear()
    }

    private fun pump(): List<Job> {
        wake?.let { cancelling += it }
        wake = null
        val activeToken = token
        if (activeToken == null || activeToken == rejectedToken) return emptyList()
        val now = nowMs()
        cooldownUntilMs.values.removeAll { it <= now }
        val started = ArrayList<Job>()
        var earliestCooldown = Long.MAX_VALUE
        for (key in wanted) {
            if (key in delivered || key in inFlight) continue
            val until = cooldownUntilMs[key]
            if (until != null) {
                earliestCooldown = min(earliestCooldown, until)
                continue
            }
            if (inFlight.size >= MAX_IN_FLIGHT) break
            started += launchFetch(key, activeToken)
        }
        if (inFlight.size < MAX_IN_FLIGHT && earliestCooldown != Long.MAX_VALUE) {
            val timer = scope.launch(start = CoroutineStart.LAZY) {
                delay(earliestCooldown - nowMs())
                transition {}
            }
            wake = timer
            started += timer
        }
        return started
    }

    private fun launchFetch(key: ChunkKey, token: String): Job {
        val id = ++attempts
        val job = scope.launch(start = CoroutineStart.LAZY) { fetch(key, token, id) }
        inFlight[key] = Attempt(id, job)
        return job
    }

    private suspend fun fetch(key: ChunkKey, token: String, id: Long) {
        val result = try {
            source.fetch(token, key.track, key.chunk.toLong() * CHUNK_FRAMES, CHUNK_FRAMES)
        } catch (_: CancellationException) {
            currentCoroutineContext().ensureActive()
            PcmResult.Failed(STATUS_TRANSPORT_ERROR)
        } catch (_: Exception) {
            PcmResult.Failed(STATUS_TRANSPORT_ERROR)
        }
        complete(key, id, result)
    }

    private fun complete(key: ChunkKey, id: Long, result: PcmResult) {
        var rejected = false
        transition {
            if (inFlight[key]?.id != id) return@transition
            inFlight.remove(key)
            try {
                when (result) {
                    is PcmResult.Ok -> deliver(key, result)
                    is PcmResult.Failed -> when (result.status) {
                        STATUS_UNAUTHORIZED -> {
                            rejectedToken = token
                            cancelAll()
                            rejected = true
                        }
                        STATUS_UNDECODABLE -> {
                            sink.markEmpty(key)
                            delivered += key
                        }
                        STATUS_UNAVAILABLE -> coolDown(key, RETRY_MS)
                        else -> coolDown(key, FAILURE_COOLDOWN_MS)
                    }
                }
            } catch (failure: Exception) {
                coolDown(key, FAILURE_COOLDOWN_MS)
                throw failure
            }
        }
        if (rejected) onUnauthorized()
    }

    private fun deliver(key: ChunkKey, chunk: PcmResult.Ok) {
        when {
            !chunk.isConsistent() -> coolDown(key, FAILURE_COOLDOWN_MS)
            chunk.frames == 0 -> {
                sink.markEmpty(key)
                delivered += key
            }
            sink.write(key, chunk.bytes, chunk.frames) -> delivered += key
            else -> coolDown(key, RETRY_MS)
        }
    }

    private fun PcmResult.Ok.isConsistent(): Boolean = frames in 0..CHUNK_FRAMES && bytes.size == frames * BYTES_PER_FRAME

    private fun coolDown(key: ChunkKey, ms: Long) {
        cooldownUntilMs[key] = nowMs() + ms
    }
}
