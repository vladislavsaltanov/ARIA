package aria.monitor.core

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

class PairingLinkTest {
    @Test
    fun parseMonitorQr() {
        assertEquals(
            Pairing("192.168.1.10", 5000, "abc", "xyz"),
            PairingLink.parse("http://192.168.1.10:5000/monitor?id=abc&key=xyz"),
        )
    }

    @Test
    fun parseRejectsControlQr() {
        assertNull(PairingLink.parse("http://192.168.1.10:5000/?id=abc&key=xyz"))
        assertNull(PairingLink.parse("http://192.168.1.10:5000/?id=abc&password=xyz"))
    }

    @Test
    fun parseRejectsOtherPaths() {
        assertNull(PairingLink.parse("http://192.168.1.10:5000/monitor/?id=abc&key=xyz"))
        assertNull(PairingLink.parse("http://192.168.1.10:5000/monitor/ws?id=abc&key=xyz"))
        assertNull(PairingLink.parse("http://192.168.1.10:5000?id=abc&key=xyz"))
    }

    @Test
    fun parseRejectsMissingKey() {
        assertNull(PairingLink.parse("http://192.168.1.10:5000/monitor?id=abc"))
        assertNull(PairingLink.parse("http://192.168.1.10:5000/monitor?id=abc&key="))
        assertNull(PairingLink.parse("http://192.168.1.10:5000/monitor?id=abc&key"))
    }

    @Test
    fun parseRejectsMissingId() {
        assertNull(PairingLink.parse("http://192.168.1.10:5000/monitor?key=xyz"))
        assertNull(PairingLink.parse("http://192.168.1.10:5000/monitor?id=&key=xyz"))
        assertNull(PairingLink.parse("http://192.168.1.10:5000/monitor"))
    }

    @Test
    fun parseRejectsGarbage() {
        assertNull(PairingLink.parse(""))
        assertNull(PairingLink.parse("   "))
        assertNull(PairingLink.parse("not a url"))
        assertNull(PairingLink.parse("garbage"))
        assertNull(PairingLink.parse("http://"))
        assertNull(PairingLink.parse("http:///monitor?id=a&key=b"))
        assertNull(PairingLink.parse("http://[::1/monitor?id=a&key=b"))
        assertNull(PairingLink.parse("/monitor?id=a&key=b"))
    }

    @Test
    fun parseRejectsMalformedPercentEscape() {
        assertNull(PairingLink.parse("http://192.168.1.10:5000/monitor?id=%zz&key=b"))
        assertNull(PairingLink.parse("http://192.168.1.10:5000/monitor?id=a&key=%"))
    }

    @Test
    fun parseRejectsNonHttpScheme() {
        assertNull(PairingLink.parse("https://192.168.1.10:5000/monitor?id=abc&key=xyz"))
        assertNull(PairingLink.parse("ftp://192.168.1.10:5000/monitor?id=abc&key=xyz"))
        assertNull(PairingLink.parse("aria://192.168.1.10:5000/monitor?id=abc&key=xyz"))
    }

    @Test
    fun parseRejectsPortOutOfRange() {
        assertNull(PairingLink.parse("http://192.168.1.10:0/monitor?id=a&key=b"))
        assertNull(PairingLink.parse("http://192.168.1.10:65536/monitor?id=a&key=b"))
        assertNull(PairingLink.parse("http://192.168.1.10:99999999999/monitor?id=a&key=b"))
        assertEquals(1, PairingLink.parse("http://192.168.1.10:1/monitor?id=a&key=b")?.port)
        assertEquals(65535, PairingLink.parse("http://192.168.1.10:65535/monitor?id=a&key=b")?.port)
    }

    @Test
    fun parseMissingPortDefaultsTo80() {
        assertEquals(
            Pairing("192.168.1.10", 80, "a", "b"),
            PairingLink.parse("http://192.168.1.10/monitor?id=a&key=b"),
        )
    }

    @Test
    fun parseDecodesPercentEscapesFromUriEscapeDataString() {
        val pairing = PairingLink.parse(
            "http://192.168.1.10:5000/monitor?id=a%20b%2Bc%26d%3De&key=%D0%BF%D0%B0%D1%80%D0%BE%D0%BB%D1%8C%20%2B%26%3D%E2%82%AC",
        )
        assertEquals(Pairing("192.168.1.10", 5000, "a b+c&d=e", "пароль +&=€"), pairing)
    }

    @Test
    fun parseDecodesLiteralPercentEscapedAs25() {
        assertEquals(
            Pairing("192.168.1.10", 5000, "a", "100%"),
            PairingLink.parse("http://192.168.1.10:5000/monitor?id=a&key=100%25"),
        )
    }

    @Test
    fun parseKeepsIpv6BracketsInHost() {
        val pairing = PairingLink.parse("http://[fe80::1]:48713/monitor?id=a&key=b")
        assertEquals(Pairing("[fe80::1]", 48713, "a", "b"), pairing)
        assertEquals("http://[fe80::1]:48713/", "http://${pairing?.host}:${pairing?.port}/")
    }

    @Test
    fun parseTakesFirstOccurrenceAndIgnoresExtras() {
        assertEquals(
            Pairing("192.168.1.10", 5000, "first", "k"),
            PairingLink.parse("http://192.168.1.10:5000/monitor?x=1&id=first&id=second&key=k#frag"),
        )
    }
}
