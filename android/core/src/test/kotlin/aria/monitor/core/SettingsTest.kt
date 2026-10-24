package aria.monitor.core

import kotlin.test.Test
import kotlin.test.assertEquals

class SettingsTest {
    @Test
    fun sanitizeDefaultsWhenNothingStored() {
        assertEquals(
            Settings("", Levels(0.0, true, 0.0, true), 0),
            Settings.sanitized(),
        )
    }

    @Test
    fun sanitizeKeepsValidValues() {
        val settings = Settings.sanitized(
            name = "Drums",
            backingDb = -12.5,
            backingOn = false,
            clickDb = 3.0,
            clickOn = false,
            trimMs = 120.0,
        )
        assertEquals(Settings("Drums", Levels(-12.5, false, 3.0, false), 120), settings)
    }

    @Test
    fun sanitizeClampsBackingTo40ToPlus6() {
        assertEquals(-40.0, Settings.sanitized(backingDb = -99.0).levels.backingDb)
        assertEquals(6.0, Settings.sanitized(backingDb = 99.0).levels.backingDb)
        assertEquals(-40.0, Settings.sanitized(backingDb = -40.0).levels.backingDb)
        assertEquals(6.0, Settings.sanitized(backingDb = 6.0).levels.backingDb)
        assertEquals(-40.0, Settings.sanitized(clickDb = -99.0).levels.clickDb)
        assertEquals(6.0, Settings.sanitized(clickDb = 99.0).levels.clickDb)
    }

    @Test
    fun sanitizeClampsTrimToMinus100To500() {
        assertEquals(-100, Settings.sanitized(trimMs = -1000.0).trimMs)
        assertEquals(500, Settings.sanitized(trimMs = 1000.0).trimMs)
        assertEquals(-100, Settings.sanitized(trimMs = -100.0).trimMs)
        assertEquals(500, Settings.sanitized(trimMs = 500.0).trimMs)
    }

    @Test
    fun sanitizeRoundsFractionalTrimAfterClamping() {
        assertEquals(13, Settings.sanitized(trimMs = 12.5).trimMs)
        assertEquals(12, Settings.sanitized(trimMs = 12.4).trimMs)
        assertEquals(-12, Settings.sanitized(trimMs = -12.4).trimMs)
        assertEquals(-12, Settings.sanitized(trimMs = -12.5).trimMs)
        assertEquals(-13, Settings.sanitized(trimMs = -12.6).trimMs)
        assertEquals(500, Settings.sanitized(trimMs = 500.4).trimMs)
        assertEquals(-100, Settings.sanitized(trimMs = -100.4).trimMs)
        assertEquals(0, Settings.sanitized(trimMs = 0.4).trimMs)
    }

    @Test
    fun sanitizeTruncatesNameTo64() {
        assertEquals("a".repeat(64), Settings.sanitized(name = "a".repeat(100)).name)
        assertEquals("a".repeat(64), Settings.sanitized(name = "a".repeat(64)).name)
        assertEquals("a".repeat(63), Settings.sanitized(name = "a".repeat(63)).name)
        assertEquals("", Settings.sanitized(name = null).name)
    }

    @Test
    fun sanitizeNameNeverSplitsSurrogatePair() {
        val pair = "🎵"
        assertEquals("a".repeat(63), Settings.sanitized(name = "a".repeat(63) + pair).name)
        assertEquals("a".repeat(62) + pair, Settings.sanitized(name = "a".repeat(62) + pair + "b").name)
        assertEquals("a".repeat(62) + pair, Settings.sanitized(name = "a".repeat(62) + pair).name)
    }

    @Test
    fun sanitizeKeepsNaNAsDefault() {
        val settings = Settings.sanitized(
            backingDb = Double.NaN,
            clickDb = Double.NaN,
            trimMs = Double.NaN,
        )
        assertEquals(Settings("", Levels(0.0, true, 0.0, true), 0), settings)
    }

    @Test
    fun sanitizeTreatsInfinityAsDefaultLikeNumberIsFinite() {
        val settings = Settings.sanitized(
            backingDb = Double.POSITIVE_INFINITY,
            clickDb = Double.NEGATIVE_INFINITY,
            trimMs = Double.POSITIVE_INFINITY,
        )
        assertEquals(Settings("", Levels(0.0, true, 0.0, true), 0), settings)
    }

    @Test
    fun sanitizeFlagsAreOnUnlessExplicitlyFalse() {
        assertEquals(true, Settings.sanitized(backingOn = null, clickOn = null).levels.backingOn)
        assertEquals(true, Settings.sanitized(backingOn = true).levels.backingOn)
        assertEquals(false, Settings.sanitized(backingOn = false).levels.backingOn)
        assertEquals(false, Settings.sanitized(clickOn = false).levels.clickOn)
    }
}
