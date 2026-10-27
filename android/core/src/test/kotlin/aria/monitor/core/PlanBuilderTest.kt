package aria.monitor.core

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertSame
import kotlin.test.assertTrue

private const val GUID_A = "01234567-89ab-cdef-0123-456789abcdef"
private const val GUID_B = "8f3a1c2b-4d5e-6f70-fedc-ba9876543210"
private const val GUID_BAD = "not-a-guid"

private val levelsOn = Levels(backingDb = -6.0, backingOn = true, clickDb = -12.0, clickOn = true)
private val levelsOff = Levels(backingDb = -6.0, backingOn = false, clickDb = -12.0, clickOn = false)
private val someTimeMap = TimeMap(baseFrame = 5_000_000_000L, hostUsAtBaseFrame = 13_000_000_123_456L, usPerFrame = 20.833)
private val someTest = TestTimeline(anchorUs = 13_000_000_000_000L, intervalUs = 500_000.0)

private fun voice(
    id: Int,
    track: String = GUID_A,
    anchorUs: Long = 13_000_000_000_000L,
    anchorFrame: Long = 0L,
    playing: Boolean = true,
    endFrame: Long? = null,
    fade: FadeTimeline? = null,
) = VoiceTimeline(
    id = id,
    epoch = id + 10,
    track = track,
    kind = "Backing",
    fallback = false,
    gainDb = -3.0,
    anchorUs = anchorUs,
    anchorFrame = anchorFrame,
    playing = playing,
    endFrame = endFrame,
    fade = fade,
)

private fun timeline(
    voices: List<VoiceTimeline> = listOf(voice(1)),
    grid: GridTimeline? = null,
    test: TestTimeline? = null,
    rate: Double = 1.0,
) = ServerMessage.Timeline(
    seq = 1L,
    rate = rate,
    voices = voices,
    grid = grid,
    deck = DeckTimeline(status = "Playing", title = null, next = null),
    test = test,
)

private fun build(
    timeline: ServerMessage.Timeline = timeline(),
    levels: Levels = levelsOn,
    trimMs: Int = 0,
    testClick: Boolean = false,
    timeMap: TimeMap = someTimeMap,
    generation: Long = 1L,
) = PlanBuilder.build(timeline, levels, trimMs, testClick, timeMap, generation)

class PlanBuilderTest {
    @Test
    fun levelsOffGiveZeroGains() {
        val plan = build(levels = levelsOff)

        assertEquals(0f, plan.backingGain)
        assertEquals(0f, plan.clickGain)
    }

    @Test
    fun backingAndClickAreIndependent() {
        val backingOnly = build(levels = levelsOn.copy(clickOn = false))
        val clickOnly = build(levels = levelsOn.copy(backingOn = false))

        assertTrue(backingOnly.backingGain > 0f)
        assertEquals(0f, backingOnly.clickGain)
        assertEquals(0f, clickOnly.backingGain)
        assertTrue(clickOnly.clickGain > 0f)
    }

    @Test
    fun dbToLinearGains() {
        val plan = build(levels = Levels(backingDb = -6.0, backingOn = true, clickDb = 0.0, clickOn = true))

        assertEquals(0.501f, plan.backingGain, 0.001f)
        assertEquals(1.0f, plan.clickGain, 1e-6f)
    }

    @Test
    fun testModeMutesBackingAndOpensClick() {
        val plan = build(timeline = timeline(test = someTest), levels = levelsOff.copy(backingOn = true, clickOn = false), testClick = true)

        assertEquals(0f, plan.backingGain)
        assertEquals(dbToGain(-12.0).toFloat(), plan.clickGain)
        assertTrue(plan.clickGain > 0f)
        assertEquals(someTest, plan.test)
    }

    @Test
    fun testModeWithoutTimelineTestIsSilentOnBothBuses() {
        val plan = build(timeline = timeline(test = null), testClick = true)

        assertEquals(0f, plan.backingGain)
        assertEquals(0f, plan.clickGain)
        assertNull(plan.test)
    }

    @Test
    fun timelineTestIsIgnoredWhenTestClickIsOff() {
        val plan = build(timeline = timeline(test = someTest), testClick = false)

        assertNull(plan.test)
        assertEquals(dbToGain(-6.0).toFloat(), plan.backingGain)
        assertEquals(dbToGain(-12.0).toFloat(), plan.clickGain)
    }

    @Test
    fun trimMsBecomesTrimUs() {
        assertEquals(250_000L, build(trimMs = 250).trimUs)
        assertEquals(-100_000L, build(trimMs = -100).trimUs)
        assertEquals(0L, build(trimMs = 0).trimUs)
        assertEquals(2_147_483_647_000L, build(trimMs = Int.MAX_VALUE).trimUs)
    }

    @Test
    fun voiceTrackGuidSplitIntoHiLo() {
        val plan = build(timeline = timeline(voices = listOf(voice(1, GUID_A), voice(2, GUID_B))))

        val (hiA, loA) = guidHiLo(GUID_A)
        val (hiB, loB) = guidHiLo(GUID_B)
        assertEquals(listOf(hiA to loA, hiB to loB), plan.voices.map { it.trackHi to it.trackLo })
        assertEquals(0x8F3A1C2B4D5E6F70uL.toLong(), plan.voices[1].trackHi)
    }

    @Test
    fun voiceWithInvalidTrackGuidIsSkippedAndTheOthersKeepTheirOrder() {
        val plan = build(timeline = timeline(voices = listOf(voice(1, GUID_A), voice(2, GUID_BAD), voice(3, GUID_B))))

        assertEquals(listOf(1, 3), plan.voices.map { it.id })
    }

    @Test
    fun allVoicesInvalidGiveAnEmptyPlanWithoutError() {
        val plan = build(timeline = timeline(voices = listOf(voice(1, GUID_BAD), voice(2, ""))))

        assertEquals(emptyList(), plan.voices)
    }

    @Test
    fun emptyTimelineVoicesGiveEmptyPlanVoices() {
        val plan = build(timeline = timeline(voices = emptyList()))

        assertEquals(emptyList(), plan.voices)
    }

    @Test
    fun gridWithMissingVoiceKept() {
        val grid = GridTimeline(voice = 99, bpm = 128.0, beatsPerBar = 4, firstBeatFrame = 24_000L)

        val plan = build(timeline = timeline(voices = listOf(voice(1)), grid = grid))

        assertEquals(grid, plan.grid)
        assertEquals(listOf(1), plan.voices.map { it.id })
    }

    @Test
    fun gridIsKeptWhenItsVoiceWasSkippedForAnInvalidGuid() {
        val grid = GridTimeline(voice = 2, bpm = 100.0, beatsPerBar = 3, firstBeatFrame = 0L)

        val plan = build(timeline = timeline(voices = listOf(voice(1), voice(2, GUID_BAD)), grid = grid))

        assertEquals(grid, plan.grid)
        assertEquals(listOf(1), plan.voices.map { it.id })
    }

    @Test
    fun gridAbsentStaysAbsent() {
        assertNull(build(timeline = timeline(grid = null)).grid)
    }

    @Test
    fun voiceFieldsPassThroughWithoutNarrowing() {
        val fade = FadeTimeline(from = 1.0, to = 0.0, progress = 0.25, frames = 96_000, curve = "SCurve", stopAtEnd = true)
        val source = voice(
            id = 7,
            anchorUs = 13_000_000_123_456L,
            anchorFrame = 3_000_000_000L,
            playing = false,
            endFrame = 3_480_000_000L,
            fade = fade,
        )

        val planned = build(timeline = timeline(voices = listOf(source))).voices.single()

        assertEquals(7, planned.id)
        assertEquals(17, planned.epoch)
        assertEquals(-3.0, planned.gainDb)
        assertEquals(13_000_000_123_456L, planned.anchorUs)
        assertEquals(3_000_000_000L, planned.anchorFrame)
        assertEquals(false, planned.playing)
        assertEquals(3_480_000_000L, planned.endFrame)
        assertEquals(fade, planned.fade)
    }

    @Test
    fun playingVoiceWithoutEndOrFadeKeepsThemNull() {
        val planned = build(timeline = timeline(voices = listOf(voice(1, endFrame = null, fade = null)))).voices.single()

        assertTrue(planned.playing)
        assertNull(planned.endFrame)
        assertNull(planned.fade)
    }

    @Test
    fun rateGenerationAndTimeMapPassThrough() {
        val plan = build(timeline = timeline(rate = 0.5), generation = 4_294_967_298L)

        assertEquals(0.5, plan.rate)
        assertEquals(4_294_967_298L, plan.generation)
        assertSame(someTimeMap, plan.timeMap)
    }

    @Test
    fun masterIsUnityWithTheDefaultRamp() {
        val plan = build()

        assertEquals(1.0f, plan.masterGain)
        assertEquals(0.02f, plan.masterRampSeconds)
    }

    @Test
    fun masterRampCanBeOverridden() {
        val plan = PlanBuilder.build(timeline(), levelsOn, 0, false, someTimeMap, 1L, masterRampSeconds = 0.5f)

        assertEquals(0.5f, plan.masterRampSeconds)
        assertEquals(1.0f, plan.masterGain)
    }
}
