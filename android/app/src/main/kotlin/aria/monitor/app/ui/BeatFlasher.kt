package aria.monitor.app.ui

import aria.monitor.core.BeatDisplay

private const val FLASH_NANOS = 100_000_000L

enum class Flash { None, Beat, Accent }

class BeatFlasher {
    private var lastBarBeat: String? = null
    private var flashedAtNanos = 0L
    private var flash = Flash.None

    fun update(beat: BeatDisplay, nowNanos: Long): Flash {
        val lit = beat.litBeat
        if (lit == null) {
            lastBarBeat = null
            flash = Flash.None
        } else if (beat.barBeat != lastBarBeat) {
            lastBarBeat = beat.barBeat
            flashedAtNanos = nowNanos
            flash = if (lit == 0) Flash.Accent else Flash.Beat
        } else if (nowNanos - flashedAtNanos >= FLASH_NANOS) {
            flash = Flash.None
        }
        return flash
    }
}
