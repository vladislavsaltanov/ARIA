package aria.monitor.app

import aria.monitor.R
import aria.monitor.app.ui.AppFlowViewModel
import aria.monitor.app.ui.Destination
import aria.monitor.app.ui.MonitorScreen
import aria.monitor.app.ui.MonitorTheme
import aria.monitor.app.ui.MonitorViewModel
import aria.monitor.app.ui.OnboardingScreen
import aria.monitor.app.ui.PairingScreen
import aria.monitor.app.ui.StageColors
import aria.monitor.app.ui.StoppedDialog
import android.graphics.Color
import android.os.Bundle
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.viewModels
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.ui.platform.LocalContext
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import androidx.compose.ui.Modifier

class MainActivity : ComponentActivity() {
    private val sessions by lazy { AppStores.sessions(applicationContext) }

    private val appFlow: AppFlowViewModel by viewModels {
        viewModelFactory {
            initializer {
                val stores = AppStores.settings(applicationContext)
                AppFlowViewModel(stores, stores, sessions, AppStores.scope)
            }
        }
    }

    private val viewModel: MonitorViewModel by viewModels {
        viewModelFactory { initializer { MonitorViewModel(sessions, appFlow::openPairing) } }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge(
            statusBarStyle = SystemBarStyle.dark(Color.TRANSPARENT),
            navigationBarStyle = SystemBarStyle.dark(Color.TRANSPARENT),
        )
        setContent { MonitorTheme { AppRoot(appFlow, viewModel) } }
    }

    override fun onStart() {
        super.onStart()
        sessions.bind()
        appFlow.resume()
    }

    override fun onStop() {
        appFlow.onActivityStopped(isChangingConfigurations)
        sessions.unbind()
        super.onStop()
    }
}

@Composable
private fun AppRoot(flow: AppFlowViewModel, monitor: MonitorViewModel) {
    val destination by flow.destination.collectAsStateWithLifecycle()
    val pairingNotSaved by flow.pairingNotSaved.collectAsStateWithLifecycle()
    val monitorStopped by flow.monitorStopped.collectAsStateWithLifecycle()
    val context = LocalContext.current
    LaunchedEffect(pairingNotSaved) {
        if (pairingNotSaved) Toast.makeText(context, R.string.pairing_not_saved, Toast.LENGTH_LONG).show()
    }
    when (val current = destination) {
        Destination.Loading -> Box(Modifier.fillMaxSize().background(StageColors.Background))
        Destination.Onboarding -> OnboardingScreen(onFinish = flow::finishOnboarding)
        is Destination.Pairing -> PairingScreen(
            canCancel = current.canCancel,
            onPaired = flow::paired,
            onBack = flow::cancelPairing,
        )
        Destination.Monitor -> {
            MonitorScreen(monitor)
            if (monitorStopped) StoppedDialog(onReconnect = flow::reconnect)
        }
    }
}
