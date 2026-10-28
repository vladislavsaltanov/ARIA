package aria.monitor.app.ui

import aria.monitor.core.Levels
import aria.monitor.core.LinkState
import aria.monitor.core.SessionState
import aria.monitor.core.formatUncertaintyMs

sealed interface LinkStatus {
    data object Offline : LinkStatus

    data object Syncing : LinkStatus

    data class Locked(val uncertaintyMs: String) : LinkStatus
}

data class MonitorUi(
    val hasSession: Boolean,
    val status: LinkStatus,
    val outputMissing: Boolean,
    val backingFallback: Boolean,
    val levels: Levels,
    val trimMs: Int,
    val testClick: Boolean,
    val name: String,
    val kicked: Boolean,
    val needsPairing: Boolean,
) {
    companion object {
        val NoSession = MonitorUi(
            hasSession = false,
            status = LinkStatus.Offline,
            outputMissing = false,
            backingFallback = false,
            levels = Levels(backingDb = 0.0, backingOn = true, clickDb = 0.0, clickOn = true),
            trimMs = 0,
            testClick = false,
            name = "",
            kicked = false,
            needsPairing = false,
        )
    }
}

fun SessionState.toUi(): MonitorUi =
    MonitorUi(
        hasSession = true,
        status = linkStatus(),
        outputMissing = !outputOk,
        backingFallback = timeline?.voices?.any { it.fallback } == true,
        levels = levels,
        trimMs = trimMs,
        testClick = testClick,
        name = name,
        kicked = kicked,
        needsPairing = needsPairing,
    )

private fun SessionState.linkStatus(): LinkStatus =
    when (link) {
        LinkState.Offline, LinkState.Connecting -> LinkStatus.Offline
        LinkState.Syncing -> LinkStatus.Syncing
        LinkState.Locked -> uncertaintyUs?.let { LinkStatus.Locked(formatUncertaintyMs(it)) } ?: LinkStatus.Syncing
    }
