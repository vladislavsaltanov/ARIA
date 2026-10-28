package aria.monitor.core

import kotlin.math.floor
import kotlin.math.max

const val CHUNK_FRAMES = 96000
const val POOL_SLOTS = 30

private const val AHEAD_FRAMES = 20L * SAMPLE_RATE
private const val BEHIND_FRAMES = 0.5 * SAMPLE_RATE
private const val STANDBY_FRAMES = 6L * SAMPLE_RATE

data class ChunkKey(val track: String, val chunk: Int)

data class Window(val track: String, val from: Long, val to: Long)

fun windowsFor(timeline: ServerMessage.Timeline, nowUs: Double): List<Window> {
    val voices = timeline.voices.map { voice ->
        if (voice.playing) {
            val frame = frameAt(voice, nowUs, timeline.rate)
            Window(voice.track, max(0L, floor(frame - BEHIND_FRAMES).toLong()), floor(frame + AHEAD_FRAMES).toLong())
        } else {
            Window(voice.track, max(0L, voice.anchorFrame), voice.anchorFrame + STANDBY_FRAMES)
        }
    }
    val next = timeline.deck.next?.let { Window(it.track, max(0L, it.startFrame), it.startFrame + STANDBY_FRAMES) }
    return voices + listOfNotNull(next)
}

fun chunksFor(window: Window, chunkFrames: Int = CHUNK_FRAMES): List<ChunkKey> {
    if (window.to <= window.from) return emptyList()
    val size = chunkFrames.toLong()
    return (window.from.floorDiv(size)..(window.to - 1).floorDiv(size)).map { ChunkKey(window.track, it.toInt()) }
}
