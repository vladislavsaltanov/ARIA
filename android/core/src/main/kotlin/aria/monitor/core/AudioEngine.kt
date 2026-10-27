package aria.monitor.core

data class OutputStamp(val framePosition: Long, val monotonicNs: Long)

enum class EngineEvent { StreamReopened }

interface AudioEngine : ChunkSink {
    fun start(): Boolean

    fun stop()

    fun publish(plan: RenderPlan)

    fun sampleStamp(): OutputStamp?

    val xrunCount: Int

    fun growBuffer()

    fun setEventListener(listener: (EngineEvent) -> Unit)
}
