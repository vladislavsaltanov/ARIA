package aria.monitor.app.ui

import aria.monitor.app.SessionSource
import aria.monitor.core.Levels
import aria.monitor.core.MonitorFrame
import aria.monitor.core.Session
import aria.monitor.core.SessionState
import aria.monitor.core.monitorFrame
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.stateIn

private const val UI_STOP_TIMEOUT_MS = 5_000L

interface MonitorControls {
    fun setBackingDb(db: Double)

    fun toggleBacking()

    fun setClickDb(db: Double)

    fun toggleClick()

    fun adjustTrim(deltaMs: Int)

    fun toggleTestClick()

    fun setName(name: String)

    fun retryAfterKick()

    fun setKeepScreenOn(on: Boolean)

    fun openPairing()
}

@OptIn(ExperimentalCoroutinesApi::class)
class MonitorViewModel(private val source: SessionSource, private val pairingRequested: () -> Unit = {}) : ViewModel(), MonitorControls {
    val ui: StateFlow<MonitorUi> = source.session
        .flatMapLatest { session -> session?.state?.map(SessionState::toUi) ?: flowOf(MonitorUi.NoSession) }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(UI_STOP_TIMEOUT_MS), MonitorUi.NoSession)

    private val keepOn = MutableStateFlow(true)
    val keepScreenOn: StateFlow<Boolean> = keepOn.asStateFlow()
    private var requestedName: Pair<Session, String>? = null

    fun frame(): MonitorFrame {
        val session = source.session.value
        return monitorFrame(session?.state?.value?.timeline, session?.hostNowUs())
    }

    override fun setBackingDb(db: Double) = changeLevels { it.copy(backingDb = db) }

    override fun toggleBacking() = changeLevels { it.copy(backingOn = !it.backingOn) }

    override fun setClickDb(db: Double) = changeLevels { it.copy(clickDb = db) }

    override fun toggleClick() = changeLevels { it.copy(clickOn = !it.clickOn) }

    override fun adjustTrim(deltaMs: Int) {
        val session = source.session.value ?: return
        session.setTrim(session.state.value.trimMs + deltaMs)
    }

    override fun toggleTestClick() {
        val session = source.session.value ?: return
        session.setTestClick(!session.state.value.testClick)
    }

    override fun setName(name: String) {
        val session = source.session.value ?: return
        val trimmed = name.trim()
        val current = requestedName?.takeIf { it.first === session }?.second ?: session.state.value.name
        if (trimmed == current) return
        requestedName = session to trimmed
        session.setName(name)
    }

    override fun retryAfterKick() {
        source.session.value?.retryAfterKick()
    }

    override fun openPairing() = pairingRequested()

    override fun setKeepScreenOn(on: Boolean) {
        keepOn.value = on
    }

    private fun changeLevels(change: (Levels) -> Levels) {
        val session = source.session.value ?: return
        val current = session.state.value.levels
        val next = change(current)
        if (next != current) session.setLevels(next)
    }
}
