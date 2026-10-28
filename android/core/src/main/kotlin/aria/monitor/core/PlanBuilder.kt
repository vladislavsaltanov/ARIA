package aria.monitor.core

private const val MASTER_UNITY_GAIN = 1.0f
private const val MICROS_PER_MILLI = 1000L

object PlanBuilder {
    fun build(
        timeline: ServerMessage.Timeline,
        levels: Levels,
        trimMs: Int,
        testClick: Boolean,
        timeMap: TimeMap,
        generation: Long,
        masterRampSeconds: Float = 0.02f,
    ): RenderPlan {
        val backingOpen = !testClick && levels.backingOn
        val clickOpen = if (testClick) timeline.test != null else levels.clickOn
        return RenderPlan(
            generation = generation,
            rate = timeline.rate,
            voices = timeline.voices.mapNotNull(::voicePlanOrNull),
            grid = timeline.grid,
            test = timeline.test.takeIf { testClick },
            backingGain = if (backingOpen) dbToGain(levels.backingDb).toFloat() else 0f,
            clickGain = if (clickOpen) dbToGain(levels.clickDb).toFloat() else 0f,
            masterGain = MASTER_UNITY_GAIN,
            masterRampSeconds = masterRampSeconds,
            trimUs = trimMs.toLong() * MICROS_PER_MILLI,
            timeMap = timeMap,
        )
    }

    private fun voicePlanOrNull(voice: VoiceTimeline): VoicePlan? {
        val (trackHi, trackLo) = try {
            guidHiLo(voice.track)
        } catch (_: IllegalArgumentException) {
            return null
        }
        return VoicePlan(
            id = voice.id,
            epoch = voice.epoch,
            trackHi = trackHi,
            trackLo = trackLo,
            gainDb = voice.gainDb,
            anchorUs = voice.anchorUs,
            anchorFrame = voice.anchorFrame,
            playing = voice.playing,
            endFrame = voice.endFrame,
            fade = voice.fade,
        )
    }
}
