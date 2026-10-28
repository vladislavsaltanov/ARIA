package aria.monitor.app.ui

import aria.monitor.core.BeatDisplay
import kotlin.test.Test
import kotlin.test.assertEquals

private const val MS = 1_000_000L

private fun beat(barBeat: String, litBeat: Int?) =
    BeatDisplay(barBeat = barBeat, bpm = "120 BPM", meter = "4/4", beatsPerBar = 4, litBeat = litBeat)

class BeatFlasherTest {
    @Test
    fun noFlashWhileNotPlaying() {
        val flasher = BeatFlasher()

        assertEquals(Flash.None, flasher.update(beat("1:1", null), 0))
        assertEquals(Flash.None, flasher.update(beat("1:2", null), 50 * MS))
    }

    @Test
    fun accentFlashOnTheDownbeatAndPlainFlashOnOtherBeats() {
        val flasher = BeatFlasher()

        assertEquals(Flash.Accent, flasher.update(beat("1:1", 0), 0))
        assertEquals(Flash.Beat, flasher.update(beat("1:2", 1), 500 * MS))
    }

    @Test
    fun flashLastsExactlyOneHundredMilliseconds() {
        val flasher = BeatFlasher()
        flasher.update(beat("1:2", 1), 0)

        assertEquals(Flash.Beat, flasher.update(beat("1:2", 1), 99 * MS))
        assertEquals(Flash.None, flasher.update(beat("1:2", 1), 100 * MS))
    }

    @Test
    fun sameBeatDoesNotFlashAgainAfterExpiry() {
        val flasher = BeatFlasher()
        flasher.update(beat("1:2", 1), 0)
        flasher.update(beat("1:2", 1), 200 * MS)

        assertEquals(Flash.None, flasher.update(beat("1:2", 1), 300 * MS))
    }

    @Test
    fun nextBeatFlashesAgain() {
        val flasher = BeatFlasher()
        flasher.update(beat("1:2", 1), 0)
        flasher.update(beat("1:2", 1), 200 * MS)

        assertEquals(Flash.Beat, flasher.update(beat("1:3", 2), 500 * MS))
    }

    @Test
    fun nextBeatInsideRunningFlashRestartsTheWindow() {
        val flasher = BeatFlasher()
        flasher.update(beat("1:2", 1), 0)
        flasher.update(beat("1:3", 2), 60 * MS)

        assertEquals(Flash.Beat, flasher.update(beat("1:3", 2), 150 * MS))
        assertEquals(Flash.None, flasher.update(beat("1:3", 2), 160 * MS))
    }

    @Test
    fun stoppingEndsTheFlashAndResumingOnTheSameBeatFlashesAgain() {
        val flasher = BeatFlasher()
        flasher.update(beat("1:2", 1), 0)

        assertEquals(Flash.None, flasher.update(beat("1:2", null), 10 * MS))
        assertEquals(Flash.Beat, flasher.update(beat("1:2", 1), 20 * MS))
    }
}
