package aria.monitor.app

import aria.monitor.app.ui.AppFlowViewModel
import aria.monitor.app.ui.Destination
import aria.monitor.app.ui.MonitorScreen
import aria.monitor.app.ui.MonitorTheme
import aria.monitor.app.ui.MonitorViewModel
import aria.monitor.app.ui.OnboardingScreen
import aria.monitor.app.ui.PairingScreen
import aria.monitor.app.ui.StageColors
import android.graphics.Color
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.viewModels
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import androidx.compose.ui.Modifier

class MainActivity : ComponentActivity() {
    private val appFlow: AppFlowViewModel by viewModels {
        viewModelFactory {
            initializer {
                val stores = AppStores.settings(applicationContext)
                AppFlowViewModel(stores, stores, ServiceNotConnectedYetSource)
            }
        }
    }

    private val viewModel: MonitorViewModel by viewModels {
        viewModelFactory { initializer { MonitorViewModel(ServiceNotConnectedYetSource, appFlow::openPairing) } }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge(
            statusBarStyle = SystemBarStyle.dark(Color.TRANSPARENT),
            navigationBarStyle = SystemBarStyle.dark(Color.TRANSPARENT),
        )
        setContent { MonitorTheme { AppRoot(appFlow, viewModel) } }
    }
}

@Composable
private fun AppRoot(flow: AppFlowViewModel, monitor: MonitorViewModel) {
    val destination by flow.destination.collectAsStateWithLifecycle()
    when (val current = destination) {
        Destination.Loading -> Box(Modifier.fillMaxSize().background(StageColors.Background))
        Destination.Onboarding -> OnboardingScreen(onFinish = flow::finishOnboarding)
        is Destination.Pairing -> PairingScreen(
            canCancel = current.canCancel,
            onPaired = flow::paired,
            onBack = flow::cancelPairing,
        )
        Destination.Monitor -> MonitorScreen(monitor)
    }
}
