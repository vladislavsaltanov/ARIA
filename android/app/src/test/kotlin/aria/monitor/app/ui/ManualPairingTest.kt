package aria.monitor.app.ui

import aria.monitor.core.Pairing
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs

private fun valid(host: String = "192.168.1.20", port: String = "5000", id: String = "phone", key: String = "secret"): Pairing =
    assertIs<ManualPairingResult.Valid>(validateManualPairing(host, port, id, key)).pairing

private fun errors(host: String = "192.168.1.20", port: String = "5000", id: String = "phone", key: String = "secret"): Map<ManualField, ManualError> =
    assertIs<ManualPairingResult.Invalid>(validateManualPairing(host, port, id, key)).errors

class ManualPairingTest {
    @Test
    fun plainInputBecomesAPairing() {
        assertEquals(Pairing("192.168.1.20", 5000, "phone", "secret"), valid())
    }

    @Test
    fun hostnamesAreAccepted() {
        assertEquals("studio-pc.local", valid(host = "studio-pc.local").host)
        assertEquals("ARIA", valid(host = "ARIA").host)
    }

    @Test
    fun surroundingSpacesAreTrimmedFromHostPortAndIdentifierButNotFromThePassword() {
        assertEquals(
            Pairing("192.168.1.20", 5000, "phone", " secret "),
            valid(host = " 192.168.1.20 ", port = " 5000 ", id = " phone ", key = " secret "),
        )
    }

    @Test
    fun blankHostIsRejected() {
        assertEquals(mapOf(ManualField.Host to ManualError.HostBlank), errors(host = ""))
        assertEquals(mapOf(ManualField.Host to ManualError.HostBlank), errors(host = "   "))
    }

    @Test
    fun ipv6LiteralGetsBracketsSoThatTheUrlComposes() {
        assertEquals("[fe80::1]", valid(host = "fe80::1").host)
        assertEquals("[2001:db8::7334]", valid(host = " 2001:db8::7334 ").host)
    }

    @Test
    fun bracketedIpv6LiteralIsKeptAsIs() {
        assertEquals("[fe80::1]", valid(host = "[fe80::1]").host)
    }

    @Test
    fun malformedIpv6LiteralsAreRejected() {
        assertEquals(mapOf(ManualField.Host to ManualError.HostInvalid), errors(host = "[fe80::1"))
        assertEquals(mapOf(ManualField.Host to ManualError.HostInvalid), errors(host = "fe80::1]"))
        assertEquals(mapOf(ManualField.Host to ManualError.HostInvalid), errors(host = "[192.168.1.1]"))
        assertEquals(mapOf(ManualField.Host to ManualError.HostInvalid), errors(host = "[]"))
    }

    @Test
    fun hostWithEmbeddedPortSchemeOrPathIsRejected() {
        assertEquals(mapOf(ManualField.Host to ManualError.HostInvalid), errors(host = "192.168.1.20:5000"))
        assertEquals(mapOf(ManualField.Host to ManualError.HostInvalid), errors(host = "http://192.168.1.20"))
        assertEquals(mapOf(ManualField.Host to ManualError.HostInvalid), errors(host = "192.168.1.20/monitor"))
        assertEquals(mapOf(ManualField.Host to ManualError.HostInvalid), errors(host = "user@192.168.1.20"))
        assertEquals(mapOf(ManualField.Host to ManualError.HostInvalid), errors(host = "192.168.1.20?x=1"))
        assertEquals(mapOf(ManualField.Host to ManualError.HostInvalid), errors(host = "my pc"))
    }

    @Test
    fun portBoundsAreOneAndSixtyFiveFiveThirtyFive() {
        assertEquals(1, valid(port = "1").port)
        assertEquals(65535, valid(port = "65535").port)
        assertEquals(mapOf(ManualField.Port to ManualError.PortOutOfRange), errors(port = "0"))
        assertEquals(mapOf(ManualField.Port to ManualError.PortOutOfRange), errors(port = "65536"))
        assertEquals(mapOf(ManualField.Port to ManualError.PortOutOfRange), errors(port = "99999"))
        assertEquals(mapOf(ManualField.Port to ManualError.PortOutOfRange), errors(port = "100000"))
        assertEquals(mapOf(ManualField.Port to ManualError.PortOutOfRange), errors(port = "99999999999999999999"))
    }

    @Test
    fun portMustBeADecimalNumber() {
        for (port in listOf("", "  ", "abc", "50 00", "-1", "+80", "80.0", "0x50", "٥٠٠٠")) {
            assertEquals(mapOf(ManualField.Port to ManualError.PortNotNumber), errors(port = port), "port '$port'")
        }
    }

    @Test
    fun leadingZerosInThePortAreAccepted() {
        assertEquals(80, valid(port = "0080").port)
    }

    @Test
    fun blankIdentifierAndPasswordAreRejected() {
        assertEquals(mapOf(ManualField.Identifier to ManualError.IdentifierBlank), errors(id = ""))
        assertEquals(mapOf(ManualField.Identifier to ManualError.IdentifierBlank), errors(id = "  "))
        assertEquals(mapOf(ManualField.Password to ManualError.PasswordBlank), errors(key = ""))
        assertEquals(mapOf(ManualField.Password to ManualError.PasswordBlank), errors(key = " \t "))
    }

    @Test
    fun everyBrokenFieldIsReportedAtOnce() {
        assertEquals(
            mapOf(
                ManualField.Host to ManualError.HostBlank,
                ManualField.Port to ManualError.PortNotNumber,
                ManualField.Identifier to ManualError.IdentifierBlank,
                ManualField.Password to ManualError.PasswordBlank,
            ),
            errors(host = "", port = "", id = "", key = ""),
        )
    }

    @Test
    fun manualPairingComposesTheSameUrlAsAScannedOne() {
        val scanned = aria.monitor.core.pairingFromScan("http://[fe80::1]:5000/monitor?id=phone&key=secret")
        assertEquals(scanned, valid(host = "fe80::1", port = "5000"))
    }
}
