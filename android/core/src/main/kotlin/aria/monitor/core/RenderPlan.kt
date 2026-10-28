package aria.monitor.core

private const val GUID_HEX_DIGITS = 32
private const val HALF_HEX_DIGITS = 16
private const val HEX_RADIX = 16

data class TimeMap(
    val baseFrame: Long,
    val hostUsAtBaseFrame: Long,
    val usPerFrame: Double,
)

data class VoicePlan(
    val id: Int,
    val epoch: Int,
    val trackHi: Long,
    val trackLo: Long,
    val gainDb: Double,
    val anchorUs: Long,
    val anchorFrame: Long,
    val playing: Boolean,
    val endFrame: Long?,
    val fade: FadeTimeline?,
)

data class RenderPlan(
    val generation: Long,
    val rate: Double,
    val voices: List<VoicePlan>,
    val grid: GridTimeline?,
    val test: TestTimeline?,
    val backingGain: Float,
    val clickGain: Float,
    val masterGain: Float,
    val masterRampSeconds: Float,
    val trimUs: Long,
    val timeMap: TimeMap,
)

fun guidHiLo(guid: String): Pair<Long, Long> {
    val hex = guid.replace("-", "")
    require(hex.length == GUID_HEX_DIGITS && hex.all { it in '0'..'9' || it in 'a'..'f' || it in 'A'..'F' }) {
        "not a GUID: $guid"
    }
    return hex.substring(0, HALF_HEX_DIGITS).toULong(HEX_RADIX).toLong() to
        hex.substring(HALF_HEX_DIGITS).toULong(HEX_RADIX).toLong()
}
