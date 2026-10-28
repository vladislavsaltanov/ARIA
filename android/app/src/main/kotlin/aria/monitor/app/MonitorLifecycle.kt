package aria.monitor.app

import aria.monitor.core.Pairing
import aria.monitor.core.Session
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withTimeoutOrNull

private const val HANDOVER_WAIT_MS = 6000L

internal interface Holds {
    fun acquire()

    fun release()
}

internal class MonitorLifecycle(private val scope: CoroutineScope, private val waitMs: Long = HANDOVER_WAIT_MS) {
    private var last: Job? = null

    @Synchronized
    fun enqueue(step: suspend () -> Unit): Job {
        val previous = last
        return scope.launch {
            if (previous != null) withTimeoutOrNull(waitMs) { previous.join() }
            step()
        }.also { last = it }
    }
}

internal class OwnedBinding {
    private var owner: Any? = null

    @Synchronized
    fun claim(by: Any, bind: () -> Boolean) {
        if (bind()) owner = by
    }

    @Synchronized
    fun release(by: Any, unbind: () -> Unit) {
        if (owner !== by) return
        owner = null
        unbind()
    }
}

internal class MonitorInstance(
    private val lifecycle: MonitorLifecycle,
    private val runtimeFactory: () -> MonitorRuntime,
    private val holdsFactory: () -> Holds,
) {
    private val current = MutableStateFlow<MonitorRuntime?>(null)
    private val currentSession = MutableStateFlow<Session?>(null)
    val runtime: StateFlow<MonitorRuntime?> = current.asStateFlow()
    val session: StateFlow<Session?> = currentSession.asStateFlow()
    private var holds: Holds? = null

    fun open(): Job = lifecycle.enqueue {
        val runtime = runtimeFactory()
        holds = holdsFactory().also { it.acquire() }
        current.value = runtime
        currentSession.value = runtime.session
    }

    fun start(pairing: Pairing?): Job = lifecycle.enqueue { current.value?.start(pairing) }

    fun close(): Job = lifecycle.enqueue {
        val runtime = current.value
        current.value = null
        currentSession.value = null
        runtime?.shutdown()
        holds?.release()
        holds = null
    }
}
