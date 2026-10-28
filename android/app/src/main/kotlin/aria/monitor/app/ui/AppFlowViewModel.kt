package aria.monitor.app.ui

import aria.monitor.app.SessionSource
import aria.monitor.core.Pairing
import aria.monitor.core.SettingsStore
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
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
) : ViewModel() {
    private val current = MutableStateFlow<Destination>(Destination.Loading)
    val destination: StateFlow<Destination> = current.asStateFlow()

    init {
        viewModelScope.launch {
            current.value = if (onboarding.isDone()) afterOnboarding() else Destination.Onboarding
        }
    }

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
        viewModelScope.launch {
            settings.savePairing(pairing)
            sessions.session.value?.start(pairing)
        }
    }

    private suspend fun afterOnboarding(): Destination =
        if (settings.loadPairing() == null) Destination.Pairing(canCancel = false) else Destination.Monitor
}
