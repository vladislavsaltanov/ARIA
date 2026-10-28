package aria.monitor.app.ui

import aria.monitor.app.SessionSource
import aria.monitor.core.Pairing
import aria.monitor.core.SettingsStore
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

interface OnboardingStore {
    suspend fun isDone(): Boolean

    suspend fun markDone()
}

sealed interface Destination {
    data object Loading : Destination

    data object Onboarding : Destination

    data class Pairing(val canCancel: Boolean) : Destination

    data object Monitor : Destination
}

class AppFlowViewModel(
    private val settings: SettingsStore,
    private val onboarding: OnboardingStore,
    private val sessions: SessionSource,
    private val appScope: CoroutineScope,
) : ViewModel() {
    private val current = MutableStateFlow<Destination>(Destination.Loading)
    val destination: StateFlow<Destination> = current.asStateFlow()
    private val notSaved = MutableStateFlow(false)
    val pairingNotSaved: StateFlow<Boolean> = notSaved.asStateFlow()
    private val stopped = MutableStateFlow(false)
    val monitorStopped: StateFlow<Boolean> = stopped.asStateFlow()

    init {
        viewModelScope.launch {
            current.value = if (onboarding.isDone()) afterOnboarding() else Destination.Onboarding
        }
        viewModelScope.launch {
            var hadSession = false
            sessions.session.collect { session ->
                if (session != null) {
                    hadSession = true
                    stopped.value = false
                } else if (hadSession) {
                    hadSession = false
                    stopped.value = true
                }
            }
        }
    }

    fun resume() {
        if (current.value == Destination.Monitor) arm(null)
    }

    fun reconnect() = resume()

    fun finishOnboarding() {
        if (current.value != Destination.Onboarding) return
        viewModelScope.launch {
            onboarding.markDone()
            current.value = afterOnboarding()
        }
    }

    fun openPairing() {
        if (current.value == Destination.Monitor) current.value = Destination.Pairing(canCancel = true)
    }

    fun cancelPairing() {
        if ((current.value as? Destination.Pairing)?.canCancel == true) current.value = Destination.Monitor
    }

    fun paired(pairing: Pairing) {
        if (current.value !is Destination.Pairing) return
        current.value = Destination.Monitor
        appScope.launch {
            settings.savePairing(pairing)
            notSaved.value = settings.loadPairing() != pairing
            arm(pairing)
        }
    }

    private suspend fun afterOnboarding(): Destination {
        if (settings.loadPairing() == null) return Destination.Pairing(canCancel = false)
        arm(null)
        return Destination.Monitor
    }

    private fun arm(pairing: Pairing?) {
        stopped.value = !sessions.start(pairing)
    }
}
