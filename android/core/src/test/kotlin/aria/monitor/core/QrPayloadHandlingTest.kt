package aria.monitor.core

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

private const val VALID = "http://192.168.1.10:5000/monitor?id=abc&key=xyz"
private val EXPECTED = Pairing("192.168.1.10", 5000, "abc", "xyz")

class QrPayloadHandlingTest {
    @Test
    fun nullAndEmptyAreNotPairings() {
        assertNull(pairingFromScan(null))
        assertNull(pairingFromScan(""))
    }

    @Test
    fun whitespaceOnlyIsNotAPairing() {
        assertNull(pairingFromScan("   "))
        assertNull(pairingFromScan("\n\t \r\n"))
    }

    @Test
    fun foreignPayloadsAreNotPairings() {
        assertNull(pairingFromScan("hello"))
        assertNull(pairingFromScan("https://example.com/"))
        assertNull(pairingFromScan("WIFI:T:WPA;S:studio;P:secret;;"))
        assertNull(pairingFromScan("http://192.168.1.10:5000/?id=abc&key=xyz"))
        assertNull(pairingFromScan("mailto:someone@example.com"))
    }

    @Test
    fun validLinkIsAPairing() {
        assertEquals(EXPECTED, pairingFromScan(VALID))
    }

    @Test
    fun surroundingWhitespaceAndNewlinesAreIgnored() {
        assertEquals(EXPECTED, pairingFromScan("  $VALID  "))
        assertEquals(EXPECTED, pairingFromScan("\n$VALID\r\n"))
        assertEquals(EXPECTED, pairingFromScan("\t$VALID\t"))
    }

    @Test
    fun whitespaceInsideTheLinkIsNotRepaired() {
        assertNull(pairingFromScan("http://192.168.1.10:5000/monitor?id=a bc&key=xyz"))
        assertNull(pairingFromScan("http://192.168.1.10:5000/monitor?id=abc\n&key=xyz"))
    }

    @Test
    fun truncatedLinksAreNotPairings() {
        assertNull(pairingFromScan("http://192.168.1.10:5000/monitor?id=abc&key="))
        assertNull(pairingFromScan("http://192.168.1.10:5000/monitor?id=abc"))
        assertNull(pairingFromScan("http://192.168.1.10:5000/monit"))
        assertNull(pairingFromScan("http://"))
        assertNull(pairingFromScan("http"))
    }

    @Test
    fun ipv6LiteralKeepsItsBrackets() {
        assertEquals(
            Pairing("[fe80::1]", 5000, "abc", "xyz"),
            pairingFromScan(" http://[fe80::1]:5000/monitor?id=abc&key=xyz\n"),
        )
    }

    @Test
    fun hugeInputIsRejectedWithoutThrowing() {
        assertNull(pairingFromScan("a".repeat(2_000_000)))
        assertNull(pairingFromScan(" ".repeat(2_000_000)))
        assertNull(pairingFromScan("http://192.168.1.10:5000/monitor?id=abc&key=" + "k".repeat(2_000_000)))
    }

    @Test
    fun linkAtTheLengthLimitIsAcceptedAndOnePastItIsNot() {
        val prefix = "http://192.168.1.10:5000/monitor?id=abc&key="
        assertEquals(2048, (prefix + "k".repeat(2048 - prefix.length)).length)
        assertEquals(
            Pairing("192.168.1.10", 5000, "abc", "k".repeat(2048 - prefix.length)),
            pairingFromScan(prefix + "k".repeat(2048 - prefix.length)),
        )
        assertNull(pairingFromScan(prefix + "k".repeat(2049 - prefix.length)))
    }

    @Test
    fun nonAsciiAndControlGarbageIsNotAPairing() {
        assertNull(pairingFromScan("\u0000\u0001\u0002"))
        assertNull(pairingFromScan("ссылка на монитор"))
    }
}
