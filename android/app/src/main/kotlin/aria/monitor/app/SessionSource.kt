package aria.monitor.app

import aria.monitor.core.Session
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

interface SessionSource {
    val session: StateFlow<Session?>
}

object ServiceNotConnectedYetSource : SessionSource {
    override val session: StateFlow<Session?> = MutableStateFlow<Session?>(null).asStateFlow()
}
