package aria.monitor.app.ui

import aria.monitor.core.Pairing
import java.net.URI

enum class ManualField { Host, Port, Identifier, Password }

enum class ManualError { HostBlank, HostInvalid, PortNotNumber, PortOutOfRange, IdentifierBlank, PasswordBlank }

sealed interface ManualPairingResult {
    data class Valid(val pairing: Pairing) : ManualPairingResult

    data class Invalid(val errors: Map<ManualField, ManualError>) : ManualPairingResult
}

private const val MAX_PORT_DIGITS = 5
private val PORTS = 1..65535

fun validateManualPairing(host: String, port: String, identifier: String, password: String): ManualPairingResult {
    val hostResult = checkedHost(host.trim())
    val portResult = checkedPort(port.trim())
    val errors = buildMap {
        (hostResult as? Failure)?.let { put(ManualField.Host, it.error) }
        (portResult as? Failure)?.let { put(ManualField.Port, it.error) }
        if (identifier.isBlank()) put(ManualField.Identifier, ManualError.IdentifierBlank)
        if (password.isBlank()) put(ManualField.Password, ManualError.PasswordBlank)
    }
    if (errors.isNotEmpty()) return ManualPairingResult.Invalid(errors)
    return ManualPairingResult.Valid(
        Pairing((hostResult as Success).value, (portResult as Success).value, identifier.trim(), password.trim()),
    )
}

private sealed interface Checked<out T>

private data class Success<T>(val value: T) : Checked<T>

private data class Failure(val error: ManualError) : Checked<Nothing>

private fun checkedPort(port: String): Checked<Int> {
    if (port.isEmpty() || port.any { it !in '0'..'9' }) return Failure(ManualError.PortNotNumber)
    val digits = port.trimStart('0')
    val number = if (digits.length > MAX_PORT_DIGITS) null else digits.ifEmpty { "0" }.toInt()
    return if (number != null && number in PORTS) Success(number) else Failure(ManualError.PortOutOfRange)
}

private fun checkedHost(host: String): Checked<String> {
    if (host.isEmpty()) return Failure(ManualError.HostBlank)
    val colons = host.count { it == ':' }
    val literal = when {
        host.startsWith('[') -> host.takeIf { it.endsWith(']') && colons >= 2 }
        colons == 0 -> host
        colons == 1 -> null
        else -> "[$host]"
    }
    return if (literal != null && isUrlHost(literal)) Success(literal) else Failure(ManualError.HostInvalid)
}

private fun isUrlHost(candidate: String): Boolean =
    try {
        URI("http://$candidate:80/").host == candidate
    } catch (malformed: java.net.URISyntaxException) {
        false
    }
