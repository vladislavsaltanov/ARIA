package aria.monitor.app.ui

import aria.monitor.R
import aria.monitor.app.OnboardingItem
import aria.monitor.app.OnboardingStep
import aria.monitor.app.allDone
import aria.monitor.app.currentOnboardingSteps
import aria.monitor.app.openAppSettings
import aria.monitor.app.requestBatteryExemption
import android.Manifest
import android.os.Build
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.LifecycleResumeEffect

private val CardShape = RoundedCornerShape(8.dp)

@Composable
fun OnboardingScreen(onFinish: () -> Unit) {
    val context = LocalContext.current
    var steps by remember { mutableStateOf(context.currentOnboardingSteps()) }
    val notificationPermission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) {
        steps = context.currentOnboardingSteps()
    }
    LifecycleResumeEffect(context) {
        steps = context.currentOnboardingSteps()
        onPauseOrDispose {}
    }
    Box(Modifier.fillMaxSize().background(StageColors.Background).safeDrawingPadding()) {
        Column(
            modifier = Modifier
                .align(Alignment.TopCenter)
                .widthIn(max = 520.dp)
                .fillMaxWidth()
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 16.dp, vertical = 12.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            Text(
                text = stringResource(R.string.onboarding_title),
                style = StageText.Title,
                modifier = Modifier.semantics { heading() },
            )
            Text(stringResource(R.string.onboarding_intro), style = StageText.Hint)
            steps.forEach { step ->
                when (step.item) {
                    OnboardingItem.Notifications -> StepCard(
                        title = stringResource(R.string.onboarding_notifications_title),
                        body = stringResource(R.string.onboarding_notifications_body),
                        step = step,
                        action = stringResource(R.string.onboarding_allow),
                        onAction = {
                            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                                notificationPermission.launch(Manifest.permission.POST_NOTIFICATIONS)
                            }
                        },
                    )
                    OnboardingItem.BatteryExemption -> StepCard(
                        title = stringResource(R.string.onboarding_battery_title),
                        body = stringResource(R.string.onboarding_battery_body),
                        step = step,
                        action = stringResource(R.string.onboarding_battery_action),
                        onAction = { context.requestBatteryExemption() },
                    )
                }
            }
            Card {
                Text(stringResource(R.string.onboarding_vendor_title), style = StageText.Label)
                Text(stringResource(R.string.onboarding_vendor_body), style = StageText.Hint)
                StageButton(
                    text = stringResource(R.string.onboarding_app_settings),
                    primary = false,
                    onClick = { context.openAppSettings() },
                )
            }
            StageButton(
                text = stringResource(if (steps.allDone()) R.string.onboarding_done else R.string.onboarding_skip),
                primary = steps.allDone(),
                onClick = onFinish,
            )
        }
    }
}

@Composable
private fun StepCard(title: String, body: String, step: OnboardingStep, action: String, onAction: () -> Unit) {
    Card {
        Text(title, style = StageText.Label)
        Text(body, style = StageText.Hint)
        if (step.done) {
            Text(stringResource(R.string.onboarding_granted), style = StageText.Chip, color = StageColors.Ok)
        } else {
            StageButton(text = action, primary = true, onClick = onAction)
        }
    }
}

@Composable
private fun Card(content: @Composable () -> Unit) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .background(StageColors.Surface, CardShape)
            .border(1.dp, StageColors.Line, CardShape)
            .padding(12.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        content()
    }
}
