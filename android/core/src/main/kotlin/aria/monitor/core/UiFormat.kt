package aria.monitor.core

import java.math.BigDecimal
import java.math.RoundingMode
import kotlin.math.floor
import kotlin.math.min

private const val NO_TIME = "--:--"
private const val NO_VALUE = "—"
private const val SECONDS_PER_MINUTE = 60L
private const val TEN = 10L
private const val MICROS_PER_MILLI = 1000.0
private const val BPM_FRACTION_DIGITS = 2
private const val DB_FRACTION_DIGITS = 1
private const val UNCERTAINTY_FRACTION_DIGITS = 1
private const val METER_DENOMINATOR = 4

data class BeatDisplay(
    val barBeat: String,
    val bpm: String,
    val meter: String,
    val beatsPerBar: Int,
    val litBeat: Int?,
)

data class NowDisplay(val title: String?, val nextTitle: String?, val position: String)

data class MonitorFrame(val beat: BeatDisplay, val now: NowDisplay)

private val NO_GRID = BeatDisplay(barBeat = NO_VALUE, bpm = "$NO_VALUE BPM", meter = "$NO_VALUE/$NO_VALUE", beatsPerBar = 0, litBeat = null)

fun formatTime(seconds: Double): String {
    if (!seconds.isFinite() || seconds < 0.0) return NO_TIME
    val whole = floor(seconds).toLong()
    val remainder = whole % SECONDS_PER_MINUTE
    return "${whole / SECONDS_PER_MINUTE}:${if (remainder < TEN) "0" else ""}$remainder"
}

fun formatUncertaintyMs(us: Long): String =
    rounded(us / MICROS_PER_MILLI, UNCERTAINTY_FRACTION_DIGITS).toPlainString().withCommaDecimal()

fun formatDb(db: Double): String =
    if (db.isFinite()) rounded(db, DB_FRACTION_DIGITS).stripTrailingZeros().toPlainString().withCommaDecimal() else NO_VALUE

fun beatDisplay(grid: GridTimeline?, voice: VoiceTimeline?, hostNowUs: Double, rate: Double): BeatDisplay {
    if (grid == null || voice == null || !grid.bpm.isFinite() || !(grid.bpm > 0.0) || grid.beatsPerBar <= 0) return NO_GRID
    val grounded = NO_GRID.copy(
        bpm = "${rounded(grid.bpm, BPM_FRACTION_DIGITS).stripTrailingZeros().toPlainString().withCommaDecimal()} BPM",
        meter = "${grid.beatsPerBar}/$METER_DENOMINATOR",
        beatsPerBar = grid.beatsPerBar,
    )
    val unclamped = frameAt(voice, hostNowUs, rate)
    val frame = voice.endFrame?.let { min(unclamped, it.toDouble()) } ?: unclamped
    val framesPerBeat = (60.0 / grid.bpm) * SAMPLE_RATE
    val position = floor((frame - grid.firstBeatFrame.toDouble()) / framesPerBeat)
    if (!frame.isFinite() || !position.isFinite()) return grounded
    val index = position.toLong()
    val bar = grid.beatsPerBar.toLong()
    val beat = index.mod(bar).toInt()
    return grounded.copy(
        barBeat = "${index.floorDiv(bar) + 1}:${beat + 1}",
        litBeat = if (voice.playing) beat else null,
    )
}

fun nowDisplay(timeline: ServerMessage.Timeline?, hostNowUs: Double): NowDisplay {
    if (timeline == null) return NowDisplay(title = null, nextTitle = null, position = NO_TIME)
    val main = gridVoice(timeline) ?: timeline.voices.firstOrNull { it.playing } ?: timeline.voices.firstOrNull()
    return NowDisplay(
        title = timeline.deck.title?.takeIf { it.isNotEmpty() },
        nextTitle = timeline.deck.next?.title,
        position = main?.let { formatTime(frameAt(it, hostNowUs, timeline.rate) / SAMPLE_RATE) } ?: NO_TIME,
    )
}

fun monitorFrame(timeline: ServerMessage.Timeline?, hostNowUs: Double?): MonitorFrame {
    val now = hostNowUs ?: Double.NaN
    return MonitorFrame(
        beat = beatDisplay(timeline?.grid, timeline?.let(::gridVoice), now, timeline?.rate ?: 0.0),
        now = nowDisplay(timeline, now),
    )
}

private fun gridVoice(timeline: ServerMessage.Timeline): VoiceTimeline? =
    timeline.grid?.let { grid -> timeline.voices.firstOrNull { it.id == grid.voice } }

private fun rounded(value: Double, fractionDigits: Int): BigDecimal =
    BigDecimal(value).setScale(fractionDigits, RoundingMode.HALF_UP)

private fun String.withCommaDecimal(): String = replace('.', ',')
