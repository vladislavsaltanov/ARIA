package aria.monitor.core

import kotlin.math.ceil
import kotlin.math.floor
import kotlin.math.max
import kotlin.math.min
import kotlin.math.pow
import kotlin.math.sqrt

const val SAMPLE_RATE = 48000

data class Beat(val hostUs: Double, val index: Long, val accent: Boolean)

fun frameAt(voice: VoiceTimeline, hostUs: Double, rate: Double): Double =
    if (voice.playing) {
        voice.anchorFrame.toDouble() + ((hostUs - voice.anchorUs.toDouble()) * rate) / 1e6
    } else {
        voice.anchorFrame.toDouble()
    }

fun hostUsOf(voice: VoiceTimeline, frame: Double, rate: Double): Double =
    voice.anchorUs.toDouble() + ((frame - voice.anchorFrame.toDouble()) * 1e6) / rate

fun curve(name: String, t: Double): Double =
    when (name) {
        "Logarithmic" -> sqrt(t)
        "Exponential" -> t * t
        "SCurve" -> t * t * (3.0 - 2.0 * t)
        else -> t
    }

fun fadeLevel(fade: FadeTimeline?, framesSinceAnchor: Double): Double {
    if (fade == null) return 1.0
    val t = min(1.0, max(0.0, fade.progress + framesSinceAnchor / max(1, fade.frames).toDouble()))
    return fade.from + (fade.to - fade.from) * curve(fade.curve, t)
}

fun beatsBetween(grid: GridTimeline?, voice: VoiceTimeline?, fromUs: Double, toUs: Double, rate: Double): List<Beat> {
    if (grid == null || voice == null || !voice.playing || !(grid.bpm > 0.0) || grid.beatsPerBar <= 0) return emptyList()
    val framesPerBeat = (60.0 / grid.bpm) * SAMPLE_RATE
    val from = frameAt(voice, fromUs, rate)
    val unclamped = frameAt(voice, toUs, rate)
    val to = voice.endFrame?.let { min(unclamped, it.toDouble()) } ?: unclamped
    if (to <= from) return emptyList()
    val firstBeatFrame = grid.firstBeatFrame.toDouble()
    val first = ceil((from - firstBeatFrame) / framesPerBeat)
    val last = floor((to - firstBeatFrame - 1e-9) / framesPerBeat)
    val bar = grid.beatsPerBar.toLong()
    return buildList {
        var index = first
        while (index <= last) {
            val frame = firstBeatFrame + index * framesPerBeat
            if (!(frame < 0.0)) {
                val beatIndex = index.toLong()
                add(Beat(hostUsOf(voice, frame, rate), beatIndex, beatIndex.mod(bar) == 0L))
            }
            index += 1.0
        }
    }
}

fun dbToGain(db: Double): Double = 10.0.pow(db / 20.0)
