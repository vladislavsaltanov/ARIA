package aria.monitor.core

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

private const val RATE = 48000.0
private const val HALF_SECOND_US = 500_000.0
private const val NO_BEAT_BAR = "—"
private val NO_GRID = BeatDisplay(barBeat = "—", bpm = "— BPM", meter = "—/—", beatsPerBar = 0, litBeat = null)

private fun grid(bpm: Double = 120.0, beatsPerBar: Int = 4, firstBeatFrame: Long = 0L) =
    GridTimeline(voice = 1, bpm = bpm, beatsPerBar = beatsPerBar, firstBeatFrame = firstBeatFrame)

private fun voice(
    id: Int = 1,
    playing: Boolean = true,
    anchorUs: Long = 0L,
    anchorFrame: Long = 0L,
    endFrame: Long? = null,
) = VoiceTimeline(
    id = id,
    epoch = 1,
    track = "1db4df96-26fe-4f49-a954-fcacd871e9c0",
    kind = "backing",
    fallback = false,
    gainDb = 0.0,
    anchorUs = anchorUs,
    anchorFrame = anchorFrame,
    playing = playing,
    endFrame = endFrame,
    fade = null,
)

private fun beat(
    hostNowUs: Double,
    grid: GridTimeline? = grid(),
    voice: VoiceTimeline? = voice(),
    rate: Double = RATE,
) = beatDisplay(grid, voice, hostNowUs, rate)

private fun timeline(
    voices: List<VoiceTimeline>,
    grid: GridTimeline? = null,
    title: String? = "Song",
    next: String? = null,
) = ServerMessage.Timeline(
    seq = 1,
    rate = RATE,
    voices = voices,
    grid = grid,
    deck = DeckTimeline(
        status = "Playing",
        title = title,
        next = next?.let { NextTimeline(title = it, track = "t", kind = "backing", startFrame = 0L) },
    ),
    test = null,
)

class UiFormatTest {
    @Test
    fun formatTimeShowsMinutesAndPaddedSeconds() {
        assertEquals("1:05", formatTime(65.4))
        assertEquals("0:00", formatTime(0.0))
        assertEquals("0:09", formatTime(9.0))
        assertEquals("10:00", formatTime(600.0))
    }

    @Test
    fun formatTimeFloorsFractionalSecondsInsteadOfRounding() {
        assertEquals("0:59", formatTime(59.999))
        assertEquals("1:05", formatTime(65.9))
    }

    @Test
    fun formatTimeNaNIsPlaceholder() {
        assertEquals("--:--", formatTime(Double.NaN))
    }

    @Test
    fun formatTimeNegativeIsPlaceholder() {
        assertEquals("--:--", formatTime(-1.0))
        assertEquals("--:--", formatTime(-0.001))
    }

    @Test
    fun formatTimeInfinityIsPlaceholder() {
        assertEquals("--:--", formatTime(Double.POSITIVE_INFINITY))
        assertEquals("--:--", formatTime(Double.NEGATIVE_INFINITY))
    }

    @Test
    fun formatTimeNegativeZeroIsZero() {
        assertEquals("0:00", formatTime(-0.0))
    }

    @Test
    fun formatTimeBeyondAnHourKeepsCountingMinutes() {
        assertEquals("62:05", formatTime(3725.0))
        assertEquals("60:00", formatTime(3600.0))
    }

    @Test
    fun formatTimeHugeValueDoesNotOverflow() {
        assertEquals("16666666666666666:40", formatTime(1e18))
    }

    @Test
    fun formatUncertaintyKeepsOneDecimalWithComma() {
        assertEquals("0,3", formatUncertaintyMs(300))
        assertEquals("1,0", formatUncertaintyMs(1000))
        assertEquals("12,3", formatUncertaintyMs(12_345))
        assertEquals("0,0", formatUncertaintyMs(0))
    }

    @Test
    fun formatUncertaintyRoundsHalfUp() {
        assertEquals("1,3", formatUncertaintyMs(1250))
        assertEquals("0,0", formatUncertaintyMs(49))
        assertEquals("0,1", formatUncertaintyMs(50))
    }

    @Test
    fun formatDbDropsFractionZerosAndUsesComma() {
        assertEquals("0", formatDb(0.0))
        assertEquals("-12", formatDb(-12.0))
        assertEquals("6", formatDb(6.0))
        assertEquals("-12,5", formatDb(-12.5))
        assertEquals("0", formatDb(-0.0))
        assertEquals("—", formatDb(Double.NaN))
        assertEquals("—", formatDb(Double.POSITIVE_INFINITY))
    }

    @Test
    fun beatDisplayFirstBeatIs1_1() {
        val shown = beat(hostNowUs = 0.0)

        assertEquals("1:1", shown.barBeat)
        assertEquals(0, shown.litBeat)
        assertEquals(4, shown.beatsPerBar)
    }

    @Test
    fun beatDisplayAdvancesBeatThenBar() {
        assertEquals("1:2", beat(HALF_SECOND_US).barBeat)
        assertEquals("1:4", beat(3 * HALF_SECOND_US).barBeat)
        assertEquals("2:1", beat(4 * HALF_SECOND_US).barBeat)
        assertEquals(0, beat(4 * HALF_SECOND_US).litBeat)
        assertEquals("3:3", beat(10 * HALF_SECOND_US).barBeat)
        assertEquals(2, beat(10 * HALF_SECOND_US).litBeat)
    }

    @Test
    fun beatDisplayNegativeIndexUsesModulo() {
        val countIn = grid(firstBeatFrame = 24_000L)

        val oneBefore = beat(hostNowUs = 0.0, grid = countIn)
        assertEquals("0:4", oneBefore.barBeat)
        assertEquals(3, oneBefore.litBeat)

        val twoBefore = beat(hostNowUs = 0.0, grid = grid(firstBeatFrame = 48_000L))
        assertEquals("0:3", twoBefore.barBeat)
        assertEquals(2, twoBefore.litBeat)

        val fullBarBefore = beat(hostNowUs = 0.0, grid = grid(firstBeatFrame = 96_000L))
        assertEquals("0:1", fullBarBefore.barBeat)
        assertEquals(0, fullBarBefore.litBeat)

        val barAndABeatBefore = beat(hostNowUs = 0.0, grid = grid(firstBeatFrame = 120_000L))
        assertEquals("-1:4", barAndABeatBefore.barBeat)
        assertEquals(3, barAndABeatBefore.litBeat)
    }

    @Test
    fun beatDisplayLitBeatSwitchesExactlyOnTheBeatBoundary() {
        assertEquals(0, beat(499_999.0).litBeat)
        assertEquals(1, beat(500_000.0).litBeat)
        assertEquals(1, beat(500_001.0).litBeat)
        assertEquals(3, beat(1_999_999.0).litBeat)
        assertEquals(0, beat(2_000_000.0).litBeat)
    }

    @Test
    fun beatDisplayBeatBoundaryFollowsFirstBeatFrame() {
        val offset = grid(firstBeatFrame = 1000L)

        assertEquals("0:4", beat(hostNowUs = 0.0, grid = offset).barBeat)
        assertEquals("0:4", beat(hostNowUs = (1000.0 - 1.0) / RATE * 1e6, grid = offset).barBeat)
        assertEquals("1:1", beat(hostNowUs = 1000.0 / RATE * 1e6, grid = offset).barBeat)
    }

    @Test
    fun beatDisplayNotPlayingShowsPositionWithoutLitBeat() {
        val paused = voice(playing = false, anchorFrame = 24_000L)

        val shown = beat(hostNowUs = 60_000_000.0, voice = paused)

        assertEquals("1:2", shown.barBeat)
        assertNull(shown.litBeat)
        assertEquals(4, shown.beatsPerBar)
        assertEquals("120 BPM", shown.bpm)
    }

    @Test
    fun beatDisplayStopsAtVoiceEndFrame() {
        val ending = voice(endFrame = 48_000L)

        assertEquals("1:3", beat(hostNowUs = 60_000_000.0, voice = ending).barBeat)
        assertEquals("1:2", beat(hostNowUs = HALF_SECOND_US, voice = ending).barBeat)
    }

    @Test
    fun beatDisplayUsesTimelineRateForPosition() {
        val half = beat(hostNowUs = 1_000_000.0, rate = RATE / 2)

        assertEquals("1:2", half.barBeat)
    }

    @Test
    fun beatDisplayHandlesHugeAnchorAndHostClock() {
        val anchored = voice(anchorUs = 13_000_000_000_000L)

        val shown = beat(hostNowUs = 13_000_000_000_000.0 + HALF_SECOND_US, voice = anchored)

        assertEquals("1:2", shown.barBeat)
        assertEquals(1, shown.litBeat)
    }

    @Test
    fun beatDisplayHandlesFramesBeyondInt32() {
        val late = voice(anchorFrame = 3_000_000_000L)

        val shown = beat(hostNowUs = 0.0, voice = late)

        assertEquals("31251:1", shown.barBeat)
        assertEquals(0, shown.litBeat)
        assertEquals("31251:2", beat(hostNowUs = HALF_SECOND_US, voice = late).barBeat)
    }

    @Test
    fun beatDisplayHandlesHugeHostClockFarFromAnchor() {
        val shown = beat(hostNowUs = 1.3e13 + 600_000.0)

        assertEquals("6500001:2", shown.barBeat)
        assertEquals(1, shown.litBeat)
    }

    @Test
    fun beatDisplayNonFiniteClockKeepsGridButHidesPosition() {
        for (hostNow in listOf(Double.NaN, Double.POSITIVE_INFINITY, Double.NEGATIVE_INFINITY)) {
            val shown = beat(hostNowUs = hostNow)

            assertEquals(NO_BEAT_BAR, shown.barBeat)
            assertNull(shown.litBeat)
            assertEquals("120 BPM", shown.bpm)
            assertEquals("4/4", shown.meter)
            assertEquals(4, shown.beatsPerBar)
        }
    }

    @Test
    fun beatDisplayNonFiniteRateHidesPosition() {
        assertEquals(NO_BEAT_BAR, beat(hostNowUs = 1.0, rate = Double.NaN).barBeat)
    }

    @Test
    fun bpmUsesCommaDecimal() {
        assertEquals("90,5 BPM", beat(0.0, grid = grid(bpm = 90.5)).bpm)
        assertEquals("120 BPM", beat(0.0, grid = grid(bpm = 120.0)).bpm)
    }

    @Test
    fun bpmRoundsToTwoDecimalsAndDropsTrailingZeros() {
        assertEquals("90,46 BPM", beat(0.0, grid = grid(bpm = 90.456)).bpm)
        assertEquals("100 BPM", beat(0.0, grid = grid(bpm = 99.999)).bpm)
        assertEquals("90,1 BPM", beat(0.0, grid = grid(bpm = 90.1)).bpm)
        assertEquals("90,13 BPM", beat(0.0, grid = grid(bpm = 90.125)).bpm)
        assertEquals("90 BPM", beat(0.0, grid = grid(bpm = 90.005)).bpm)
    }

    @Test
    fun meterShows3Over4() {
        assertEquals("3/4", beat(0.0, grid = grid(beatsPerBar = 3)).meter)
        assertEquals(3, beat(0.0, grid = grid(beatsPerBar = 3)).beatsPerBar)
        assertEquals("6/4", beat(0.0, grid = grid(beatsPerBar = 6)).meter)
    }

    @Test
    fun beatDisplayWrapsAroundShorterBar() {
        val waltz = grid(beatsPerBar = 3)

        assertEquals("1:3", beat(2 * HALF_SECOND_US, grid = waltz).barBeat)
        assertEquals("2:1", beat(3 * HALF_SECOND_US, grid = waltz).barBeat)
    }

    @Test
    fun noGridShowsDashes() {
        assertEquals(NO_GRID, beatDisplay(null, voice(), 0.0, RATE))
        assertEquals(NO_GRID, beatDisplay(grid(), null, 0.0, RATE))
        assertEquals(NO_GRID, beatDisplay(null, null, 0.0, RATE))
        assertEquals("—", NO_GRID.barBeat)
        assertEquals("— BPM", NO_GRID.bpm)
        assertEquals("—/—", NO_GRID.meter)
    }

    @Test
    fun nonPositiveOrNonFiniteBpmShowsDashes() {
        for (bpm in listOf(0.0, -120.0, Double.NaN, Double.POSITIVE_INFINITY, Double.NEGATIVE_INFINITY)) {
            assertEquals(NO_GRID, beat(0.0, grid = grid(bpm = bpm)), "bpm=$bpm")
        }
    }

    @Test
    fun nonPositiveBeatsPerBarShowsDashes() {
        assertEquals(NO_GRID, beat(0.0, grid = grid(beatsPerBar = 0)))
        assertEquals(NO_GRID, beat(0.0, grid = grid(beatsPerBar = -4)))
    }

    @Test
    fun nowDisplayReadsTitleNextAndPositionOfTheGridVoice() {
        val timeline = timeline(
            voices = listOf(voice(id = 2, anchorFrame = 0L), voice(id = 1, anchorFrame = 48_000L * 65)),
            grid = grid(),
            next = "Encore",
        )

        val shown = nowDisplay(timeline, hostNowUs = 0.0)

        assertEquals(NowDisplay(title = "Song", nextTitle = "Encore", position = "1:05"), shown)
    }

    @Test
    fun nowDisplayPositionAdvancesWithHostClock() {
        val timeline = timeline(voices = listOf(voice()))

        assertEquals("1:05", nowDisplay(timeline, hostNowUs = 65_400_000.0).position)
    }

    @Test
    fun nowDisplayFallsBackToPlayingVoiceThenFirstVoice() {
        val idle = voice(id = 1, playing = false, anchorFrame = 48_000L * 10)
        val live = voice(id = 2, playing = true, anchorFrame = 48_000L * 20)

        assertEquals("0:20", nowDisplay(timeline(listOf(idle, live)), 0.0).position)
        assertEquals("0:10", nowDisplay(timeline(listOf(idle)), 0.0).position)
    }

    @Test
    fun nowDisplayPrefersGridVoiceOverPlayingVoice() {
        val gridVoice = voice(id = 1, playing = false, anchorFrame = 48_000L * 5)
        val other = voice(id = 2, playing = true, anchorFrame = 48_000L * 20)

        assertEquals("0:05", nowDisplay(timeline(listOf(other, gridVoice), grid = grid()), 0.0).position)
    }

    @Test
    fun nowDisplayWithoutVoicesHasNoPosition() {
        assertEquals("--:--", nowDisplay(timeline(voices = emptyList()), 0.0).position)
    }

    @Test
    fun nowDisplayWithoutTimelineIsEmpty() {
        assertEquals(NowDisplay(title = null, nextTitle = null, position = "--:--"), nowDisplay(null, 0.0))
    }

    @Test
    fun nowDisplayWithoutTitleOrNextKeepsThemNull() {
        val shown = nowDisplay(timeline(listOf(voice()), title = null), 0.0)

        assertNull(shown.title)
        assertNull(shown.nextTitle)
    }

    @Test
    fun nowDisplayEmptyTitleCountsAsNoTitle() {
        assertNull(nowDisplay(timeline(listOf(voice()), title = ""), 0.0).title)
    }

    @Test
    fun nowDisplayWithoutClockKeepsTitlesButNoPosition() {
        val shown = nowDisplay(timeline(listOf(voice()), next = "Encore"), hostNowUs = Double.NaN)

        assertEquals(NowDisplay(title = "Song", nextTitle = "Encore", position = "--:--"), shown)
    }

    @Test
    fun monitorFrameCombinesBeatAndNow() {
        val timeline = timeline(voices = listOf(voice()), grid = grid(bpm = 90.5, beatsPerBar = 3))

        val frame = monitorFrame(timeline, hostNowUs = HALF_SECOND_US)

        assertEquals("1:1", frame.beat.barBeat)
        assertEquals("90,5 BPM", frame.beat.bpm)
        assertEquals("3/4", frame.beat.meter)
        assertEquals("0:00", frame.now.position)
    }

    @Test
    fun monitorFrameGridWithoutItsVoiceShowsDashes() {
        val timeline = timeline(voices = listOf(voice(id = 7)), grid = grid())

        assertEquals(NO_GRID, monitorFrame(timeline, hostNowUs = 0.0).beat)
    }

    @Test
    fun monitorFrameWithoutTimelineIsEmpty() {
        assertEquals(MonitorFrame(NO_GRID, NowDisplay(null, null, "--:--")), monitorFrame(null, hostNowUs = 0.0))
    }

    @Test
    fun monitorFrameWithoutClockShowsGridButNotPosition() {
        val timeline = timeline(voices = listOf(voice()), grid = grid())

        val frame = monitorFrame(timeline, hostNowUs = null)

        assertEquals("—", frame.beat.barBeat)
        assertEquals("120 BPM", frame.beat.bpm)
        assertEquals("--:--", frame.now.position)
    }
}
