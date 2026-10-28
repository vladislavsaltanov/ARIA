package aria.monitor.core

import java.net.URI
import java.net.URLDecoder

data class Pairing(val host: String, val port: Int, val identifier: String, val password: String)

object PairingLink {
    private const val DEFAULT_HTTP_PORT = 80
    private const val UTF_8 = "UTF-8"

    fun parse(url: String): Pairing? {
        val uri = try {
            URI(url)
        } catch (malformed: Exception) {
            return null
        }
        if (!"http".equals(uri.scheme, ignoreCase = true) || uri.rawPath != "/monitor") return null
        val host = uri.host?.takeIf { it.isNotEmpty() } ?: return null
        val port = if (uri.port == -1) DEFAULT_HTTP_PORT else uri.port
        if (port !in 1..65535) return null
        val query = try {
            queryParameters(uri.rawQuery ?: return null)
        } catch (malformed: IllegalArgumentException) {
            return null
        }
        val identifier = query["id"]?.takeIf { it.isNotEmpty() } ?: return null
        val password = query["key"]?.takeIf { it.isNotEmpty() } ?: return null
        return Pairing(host, port, identifier, password)
    }

    private fun queryParameters(rawQuery: String): Map<String, String> {
        val parameters = LinkedHashMap<String, String>()
        for (pair in rawQuery.split('&')) {
            val separator = pair.indexOf('=')
            val name = URLDecoder.decode(if (separator < 0) pair else pair.substring(0, separator), UTF_8)
            val value = if (separator < 0) "" else URLDecoder.decode(pair.substring(separator + 1), UTF_8)
            parameters.putIfAbsent(name, value)
        }
        return parameters
    }
}

private const val MAX_SCAN_LENGTH = 2048

fun pairingFromScan(raw: String?): Pairing? {
    val payload = raw?.trim() ?: return null
    return if (payload.length in 1..MAX_SCAN_LENGTH) PairingLink.parse(payload) else null
}
