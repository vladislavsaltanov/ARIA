package aria.monitor.core

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith

class RenderPlanTest {
    @Test
    fun guidHiLoSplitsTheThirtyTwoHexDigitsIntoTwoSixtyFourBitHalves() {
        val (hi, lo) = guidHiLo("01234567-89ab-cdef-0123-456789abcdef")

        assertEquals(0x0123456789ABCDEFL, hi)
        assertEquals(0x0123456789ABCDEFL, lo)
    }

    @Test
    fun guidHiLoIsCaseInsensitive() {
        assertEquals(
            guidHiLo("0a1b2c3d-4e5f-6a7b-8c9d-0e1f2a3b4c5d"),
            guidHiLo("0A1B2C3D-4E5F-6A7B-8C9D-0E1F2A3B4C5D"),
        )
    }

    @Test
    fun guidHiLoKeepsTheSignBitOfAHighBitSetHalfAsANegativeLong() {
        val (hi, lo) = guidHiLo("ffffffff-ffff-ffff-8000-000000000001")

        assertEquals(-1L, hi)
        assertEquals(Long.MIN_VALUE + 1, lo)
    }

    @Test
    fun guidHiLoAcceptsTheZeroGuid() {
        assertEquals(0L to 0L, guidHiLo("00000000-0000-0000-0000-000000000000"))
    }

    @Test
    fun guidHiLoIgnoresDashesAndNeedsOnlyThirtyTwoHexDigits() {
        assertEquals(guidHiLo("0123456789abcdef0123456789abcdef"), guidHiLo("01234567-89ab-cdef-0123-456789abcdef"))
    }

    @Test
    fun guidHiLoRejectsInvalidText() {
        listOf(
            "",
            "not-a-guid",
            "01234567-89ab-cdef-0123-456789abcde",
            "01234567-89ab-cdef-0123-456789abcdef0",
            "01234567-89ab-cdef-0123-456789abcdeg",
            "{01234567-89ab-cdef-0123-456789abcdef}",
            "+1234567-89ab-cdef-0123-456789abcdef",
            "０1234567-89ab-cdef-0123-456789abcdef",
            " 01234567-89ab-cdef-0123-456789abcdef",
        ).forEach { text ->
            assertFailsWith<IllegalArgumentException>("\"$text\"") { guidHiLo(text) }
        }
    }
}
