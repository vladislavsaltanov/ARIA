package aria.monitor.core

enum class LinkState { Offline, Connecting, Syncing, Locked }

data class SessionState(
    val link: LinkState,
    val uncertaintyUs: Long?,
    val timeline: ServerMessage.Timeline?,
    val levels: Levels,
    val trimMs: Int,
    val outputOk: Boolean,
    val testClick: Boolean,
    val kicked: Boolean,
    val needsPairing: Boolean,
    val name: String,
    val audioOk: Boolean = true,
)
