package aria.monitor.app.ui

import aria.monitor.R
import aria.monitor.app.cameraGranted
import aria.monitor.app.openAppSettings
import aria.monitor.core.Pairing
import android.Manifest
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.LifecycleResumeEffect

private val PanelShape = RoundedCornerShape(8.dp)
private val FieldMinHeight = 56.dp

@Composable
fun PairingScreen(canCancel: Boolean, onPaired: (Pairing) -> Unit, onBack: () -> Unit) {
    BackHandler(enabled = canCancel, onBack = onBack)
    val context = LocalContext.current
    var cameraAllowed by remember { mutableStateOf(context.cameraGranted()) }
    var cameraAsked by rememberSaveable { mutableStateOf(false) }
    var foreignSeen by remember { mutableStateOf(false) }
    val cameraPermission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) {
        cameraAllowed = it
    }
    LaunchedEffect(Unit) {
        if (!cameraAllowed && !cameraAsked) {
            cameraAsked = true
            cameraPermission.launch(Manifest.permission.CAMERA)
        }
    }
    LifecycleResumeEffect(context) {
        cameraAllowed = context.cameraGranted()
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
                text = stringResource(R.string.pairing_screen_title),
                style = StageText.Title,
                modifier = Modifier.semantics { heading() },
            )
            if (cameraAllowed) {
                Text(stringResource(R.string.scan_hint), style = StageText.Hint)
                QrScanner(
                    onPairing = onPaired,
                    onForeign = { foreignSeen = true },
                    modifier = Modifier
                        .fillMaxWidth()
                        .aspectRatio(1f)
                        .clip(PanelShape)
                        .border(1.dp, StageColors.Line, PanelShape),
                )
                if (foreignSeen) {
                    Text(stringResource(R.string.scan_foreign), style = StageText.Hint, color = StageColors.Signal)
                }
            } else {
                Text(stringResource(R.string.camera_denied), style = StageText.Hint, color = StageColors.Signal)
                StageButton(
                    text = stringResource(R.string.camera_request),
                    primary = false,
                    onClick = { cameraPermission.launch(Manifest.permission.CAMERA) },
                )
                StageButton(
                    text = stringResource(R.string.onboarding_app_settings),
                    primary = false,
                    onClick = { context.openAppSettings() },
                )
            }
            ManualEntry(onPaired)
            if (canCancel) StageButton(text = stringResource(R.string.pairing_back), primary = false, onClick = onBack)
        }
    }
}

@Composable
private fun ManualEntry(onPaired: (Pairing) -> Unit) {
    var host by rememberSaveable { mutableStateOf("") }
    var port by rememberSaveable { mutableStateOf("") }
    var identifier by rememberSaveable { mutableStateOf("") }
    var password by rememberSaveable { mutableStateOf("") }
    var errors by remember { mutableStateOf(emptyMap<ManualField, ManualError>()) }
    val submit = {
        when (val result = validateManualPairing(host, port, identifier, password)) {
            is ManualPairingResult.Valid -> onPaired(result.pairing)
            is ManualPairingResult.Invalid -> errors = result.errors
        }
    }
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .background(StageColors.Surface, PanelShape)
            .border(1.dp, StageColors.Line, PanelShape)
            .padding(12.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Text(stringResource(R.string.manual_title), style = StageText.Label)
        EntryField(
            value = host,
            onValueChange = { host = it; errors = errors - ManualField.Host },
            label = R.string.field_host,
            error = errors[ManualField.Host],
            keyboard = KeyboardType.Uri,
            ime = ImeAction.Next,
            onDone = submit,
        )
        EntryField(
            value = port,
            onValueChange = { port = it; errors = errors - ManualField.Port },
            label = R.string.field_port,
            error = errors[ManualField.Port],
            keyboard = KeyboardType.Number,
            ime = ImeAction.Next,
            onDone = submit,
        )
        EntryField(
            value = identifier,
            onValueChange = { identifier = it; errors = errors - ManualField.Identifier },
            label = R.string.field_identifier,
            error = errors[ManualField.Identifier],
            keyboard = KeyboardType.Ascii,
            ime = ImeAction.Next,
            onDone = submit,
        )
        EntryField(
            value = password,
            onValueChange = { password = it; errors = errors - ManualField.Password },
            label = R.string.field_password,
            error = errors[ManualField.Password],
            keyboard = KeyboardType.Password,
            ime = ImeAction.Done,
            onDone = submit,
        )
        StageButton(text = stringResource(R.string.manual_submit), primary = true, onClick = submit)
    }
}

@Composable
private fun EntryField(
    value: String,
    onValueChange: (String) -> Unit,
    label: Int,
    error: ManualError?,
    keyboard: KeyboardType,
    ime: ImeAction,
    onDone: () -> Unit,
) {
    OutlinedTextField(
        value = value,
        onValueChange = onValueChange,
        label = { Text(stringResource(label)) },
        isError = error != null,
        supportingText = error?.let { { Text(stringResource(it.message())) } },
        singleLine = true,
        textStyle = StageText.Value,
        keyboardOptions = KeyboardOptions(keyboardType = keyboard, imeAction = ime, autoCorrectEnabled = false),
        keyboardActions = KeyboardActions(onDone = { onDone() }),
        modifier = Modifier.fillMaxWidth().heightIn(min = FieldMinHeight),
    )
}

private fun ManualError.message(): Int =
    when (this) {
        ManualError.HostBlank -> R.string.error_host_blank
        ManualError.HostInvalid -> R.string.error_host_invalid
        ManualError.PortNotNumber -> R.string.error_port_not_number
        ManualError.PortOutOfRange -> R.string.error_port_range
        ManualError.IdentifierBlank -> R.string.error_identifier_blank
        ManualError.PasswordBlank -> R.string.error_password_blank
    }
