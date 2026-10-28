package aria.monitor.app.ui

import aria.monitor.R
import aria.monitor.core.DeckTimeline
import aria.monitor.core.GridTimeline
import aria.monitor.core.Levels
import aria.monitor.core.LinkState
import aria.monitor.core.ServerMessage
import aria.monitor.core.SessionState
import aria.monitor.core.VoiceTimeline
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

private fun voice(id: Int, fallback: Boolean) = VoiceTimeline(
    id = id,
    epoch = 1,
    track = "1db4df96-26fe-4f49-a954-fcacd871e9c0",
    kind = "backing",
    fallback = fallback,
    gainDb = 0.0,
    anchorUs = 0L,
    anchorFrame = 0L,
    playing = true,
    endFrame = null,
    fade = null,
)

private fun timeline(vararg voices: VoiceTimeline) = ServerMessage.Timeline(
    seq = 1,
    rate = 48000.0,
    voices = voices.toList(),
    grid = GridTimeline(voice = 1, bpm = 120.0, beatsPerBar = 4, firstBeatFrame = 0L),
    deck = DeckTimeline(status = "Playing", title = "Song", next = null),
    test = null,
)

private fun state(
    link: LinkState = LinkState.Offline,
    uncertaintyUs: Long? = null,
    timeline: ServerMessage.Timeline? = null,
    outputOk: Boolean = true,
    kicked: Boolean = false,
    needsPairing: Boolean = false,
    audioOk: Boolean = true,
) = SessionState(
    link = link,
    uncertaintyUs = uncertaintyUs,
    timeline = timeline,
    levels = Levels(backingDb = -12.0, backingOn = false, clickDb = 3.0, clickOn = true),
    trimMs = 40,
    outputOk = outputOk,
    testClick = true,
    kicked = kicked,
    needsPairing = needsPairing,
    name = "Drums",
    audioOk = audioOk,
)

class MonitorUiTest {
    @Test
    fun offlineAndConnectingBothShowNoLink() {
        assertEquals(LinkStatus.Offline, state(LinkState.Offline).toUi().status)
        assertEquals(LinkStatus.Offline, state(LinkState.Connecting).toUi().status)
    }

    @Test
    fun syncingShowsSyncing() {
        assertEquals(LinkStatus.Syncing, state(LinkState.Syncing, uncertaintyUs = 900).toUi().status)
    }

    @Test
    fun lockedShowsUncertaintyWithCommaDecimal() {
        assertEquals(LinkStatus.Locked("0,3"), state(LinkState.Locked, uncertaintyUs = 300).toUi().status)
        assertEquals(LinkStatus.Locked("1,0"), state(LinkState.Locked, uncertaintyUs = 1000).toUi().status)
    }

    @Test
    fun lockedWithoutUncertaintyFallsBackToSyncing() {
        assertEquals(LinkStatus.Syncing, state(LinkState.Locked, uncertaintyUs = null).toUi().status)
    }

    @Test
    fun outputBadgeShowsOnlyWhenHostHasNoOutput() {
        assertFalse(state(outputOk = true).toUi().outputMissing)
        assertTrue(state(outputOk = false).toUi().outputMissing)
    }

    @Test
    fun backingBadgeShowsWhenAnyVoiceIsFallback() {
        assertFalse(state(timeline = null).toUi().backingFallback)
        assertFalse(state(timeline = timeline(voice(1, false), voice(2, false))).toUi().backingFallback)
        assertTrue(state(timeline = timeline(voice(1, false), voice(2, true))).toUi().backingFallback)
    }

    @Test
    fun copiesControlsAndOverlayFlagsFromTheSession() {
        val ui = state(kicked = true, needsPairing = true).toUi()

        assertTrue(ui.hasSession)
        assertEquals(Levels(backingDb = -12.0, backingOn = false, clickDb = 3.0, clickOn = true), ui.levels)
        assertEquals(40, ui.trimMs)
        assertTrue(ui.testClick)
        assertEquals("Drums", ui.name)
        assertTrue(ui.kicked)
        assertTrue(ui.needsPairing)
    }

    @Test
    fun noSessionIsOfflineAndInert() {
        val empty = MonitorUi.NoSession

        assertFalse(empty.hasSession)
        assertEquals(LinkStatus.Offline, empty.status)
        assertFalse(empty.outputMissing)
        assertFalse(empty.backingFallback)
        assertFalse(empty.kicked)
        assertFalse(empty.needsPairing)
    }

    @Test
    fun linkedWithoutAudioShowsNoAudio() {
        assertEquals(LinkStatus.NoAudio, state(LinkState.Syncing, audioOk = false).toUi().status)
        assertEquals(LinkStatus.NoAudio, state(LinkState.Locked, uncertaintyUs = 300, audioOk = false).toUi().status)
        assertEquals(LinkStatus.NoAudio, state(LinkState.Locked, uncertaintyUs = null, audioOk = false).toUi().status)
    }

    @Test
    fun noLinkTakesPrecedenceOverNoAudio() {
        assertEquals(LinkStatus.Offline, state(LinkState.Offline, audioOk = false).toUi().status)
        assertEquals(LinkStatus.Offline, state(LinkState.Connecting, audioOk = false).toUi().status)
    }

    @Test
    fun everyStatusMapsToItsRussianText() {
        assertEquals(R.string.status_offline, LinkStatus.Offline.textRes)
        assertEquals(R.string.status_syncing, LinkStatus.Syncing.textRes)
        assertEquals(R.string.status_no_audio, LinkStatus.NoAudio.textRes)
        assertEquals(R.string.status_locked, LinkStatus.Locked("0,3").textRes)
    }
}
