package aria.monitor.app

import aria.monitor.app.ui.MonitorScreen
import aria.monitor.app.ui.MonitorTheme
import aria.monitor.app.ui.MonitorViewModel
import android.graphics.Color
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.viewModels
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory

class MainActivity : ComponentActivity() {
    private val viewModel: MonitorViewModel by viewModels {
        viewModelFactory { initializer { MonitorViewModel(ServiceNotConnectedYetSource) } }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge(
            statusBarStyle = SystemBarStyle.dark(Color.TRANSPARENT),
            navigationBarStyle = SystemBarStyle.dark(Color.TRANSPARENT),
        )
        setContent { MonitorTheme { MonitorScreen(viewModel) } }
    }
}
