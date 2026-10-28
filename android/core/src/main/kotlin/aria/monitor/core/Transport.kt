package aria.monitor.core

interface SocketListener {
    fun onOpen()

    fun onText(text: String)

    fun onClosed(unauthorized: Boolean)
}

interface MonitorSocket {
    fun send(text: String)

    fun close()
}

interface MonitorTransport {
    fun open(pairing: Pairing, token: String, listener: SocketListener): MonitorSocket
}
