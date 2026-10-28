package aria.monitor.app

import aria.monitor.core.MonitorSocket
import aria.monitor.core.MonitorTransport
import aria.monitor.core.Pairing
import aria.monitor.core.SocketListener
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import java.util.concurrent.TimeUnit

private const val NORMAL_CLOSURE = 1000
private const val STATUS_UNAUTHORIZED = 401

internal fun OkHttpClient.forMonitorSocket(): OkHttpClient =
    newBuilder().readTimeout(0, TimeUnit.MILLISECONDS).pingInterval(0, TimeUnit.MILLISECONDS).build()

internal class OkHttpTransport(client: OkHttpClient) : MonitorTransport {
    private val client = client.forMonitorSocket()

    override fun open(pairing: Pairing, token: String, listener: SocketListener): MonitorSocket {
        val request = try {
            Request.Builder().url(pairing.url("monitor", "ws").addQueryParameter("token", token).build()).build()
        } catch (invalid: Exception) {
            listener.onClosed(unauthorized = false)
            return object : MonitorSocket {
                override fun send(text: String) = Unit

                override fun close() = Unit
            }
        }
        val webSocket = client.newWebSocket(request, Bridge(listener))
        return object : MonitorSocket {
            override fun send(text: String) {
                webSocket.send(text)
            }

            override fun close() {
                webSocket.close(NORMAL_CLOSURE, null)
            }
        }
    }

    private class Bridge(private val listener: SocketListener) : WebSocketListener() {
        override fun onOpen(webSocket: WebSocket, response: Response) = listener.onOpen()

        override fun onMessage(webSocket: WebSocket, text: String) = listener.onText(text)

        override fun onClosing(webSocket: WebSocket, code: Int, reason: String) {
            webSocket.close(NORMAL_CLOSURE, null)
        }

        override fun onClosed(webSocket: WebSocket, code: Int, reason: String) = listener.onClosed(unauthorized = false)

        override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) =
            listener.onClosed(unauthorized = response?.code == STATUS_UNAUTHORIZED)
    }
}
