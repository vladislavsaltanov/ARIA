package aria.monitor.engine

import aria.monitor.core.AudioEngine
import aria.monitor.core.ChunkKey
import aria.monitor.core.EngineEvent
import aria.monitor.core.OutputStamp
import aria.monitor.core.PlanWire
import aria.monitor.core.RenderPlan
import aria.monitor.core.guidHiLo

private const val NO_HANDLE = 0L
private const val STAMP_LONGS = 2
private const val KEY_LONGS = 3

class OboeAudioEngine : AudioEngine {
    private val lock = Any()
    private var handle = nativeCreate()
    private val stampBuffer = LongArray(STAMP_LONGS)

    @Volatile
    private var listener: (EngineEvent) -> Unit = {}

    override fun start(): Boolean = synchronized(lock) { handle != NO_HANDLE && nativeStart(handle) }

    override fun stop() {
        synchronized(lock) { if (handle != NO_HANDLE) nativeStop(handle) }
    }

    fun release() {
        synchronized(lock) {
            if (handle == NO_HANDLE) return
            nativeDestroy(handle)
            handle = NO_HANDLE
        }
    }

    override fun publish(plan: RenderPlan) {
        val bytes = PlanWire.encode(plan)
        synchronized(lock) { if (handle != NO_HANDLE) nativePublish(handle, bytes) }
    }

    override fun write(key: ChunkKey, bytes: ByteArray, frames: Int): Boolean {
        val parts = partsOf(key) ?: return false
        return synchronized(lock) {
            handle != NO_HANDLE && nativeWrite(handle, parts.first, parts.second, key.chunk, bytes, frames)
        }
    }

    override fun markEmpty(key: ChunkKey) {
        val parts = partsOf(key) ?: return
        synchronized(lock) { if (handle != NO_HANDLE) nativeMarkEmpty(handle, parts.first, parts.second, key.chunk) }
    }

    override fun retain(keys: Set<ChunkKey>) {
        val flat = LongArray(keys.size * KEY_LONGS)
        var count = 0
        for (key in keys) {
            val parts = partsOf(key) ?: continue
            flat[count * KEY_LONGS] = parts.first
            flat[count * KEY_LONGS + 1] = parts.second
            flat[count * KEY_LONGS + 2] = key.chunk.toLong()
            count++
        }
        val packed = flat.copyOf(count * KEY_LONGS)
        synchronized(lock) { if (handle != NO_HANDLE) nativeRetain(handle, packed) }
    }

    override fun sampleStamp(): OutputStamp? {
        var stamp: OutputStamp? = null
        var reopens = 0
        synchronized(lock) {
            if (handle == NO_HANDLE) return null
            reopens = nativeConsumeReopenCount(handle)
            if (nativeSampleStamp(handle, stampBuffer)) stamp = OutputStamp(stampBuffer[0], stampBuffer[1])
        }
        repeat(reopens) { listener(EngineEvent.StreamReopened) }
        return stamp
    }

    override val xrunCount: Int
        get() = synchronized(lock) { if (handle == NO_HANDLE) 0 else nativeXrunCount(handle) }

    override fun growBuffer() {
        synchronized(lock) { if (handle != NO_HANDLE) nativeGrowBuffer(handle) }
    }

    internal fun simulateDisconnectForTests(failedReopenAttempts: Int = 0) {
        synchronized(lock) { if (handle != NO_HANDLE) nativeSimulateDisconnect(handle, failedReopenAttempts) }
    }

    override fun setEventListener(listener: (EngineEvent) -> Unit) {
        this.listener = listener
    }

    private fun partsOf(key: ChunkKey): Pair<Long, Long>? =
        try {
            guidHiLo(key.track)
        } catch (_: IllegalArgumentException) {
            null
        }

    private external fun nativeCreate(): Long

    private external fun nativeDestroy(handle: Long)

    private external fun nativeStart(handle: Long): Boolean

    private external fun nativeStop(handle: Long)

    private external fun nativePublish(handle: Long, plan: ByteArray): Boolean

    private external fun nativeWrite(handle: Long, hi: Long, lo: Long, chunk: Int, pcm: ByteArray, frames: Int): Boolean

    private external fun nativeMarkEmpty(handle: Long, hi: Long, lo: Long, chunk: Int)

    private external fun nativeRetain(handle: Long, keys: LongArray)

    private external fun nativeSampleStamp(handle: Long, out: LongArray): Boolean

    private external fun nativeXrunCount(handle: Long): Int

    private external fun nativeGrowBuffer(handle: Long)

    private external fun nativeSimulateDisconnect(handle: Long, failedReopenAttempts: Int)

    private external fun nativeConsumeReopenCount(handle: Long): Int

    private companion object {
        init {
            System.loadLibrary("aria_engine")
        }
    }
}
