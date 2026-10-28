package aria.monitor.app.ui

import aria.monitor.R
import aria.monitor.core.MonitorFrame
import aria.monitor.core.formatDb
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.toggleable
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Slider
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.State
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.runtime.withFrameNanos
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.DialogProperties
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.repeatOnLifecycle
import kotlin.math.abs
import kotlin.math.roundToInt

private const val DB_MIN = -40f
private const val DB_MAX = 6f
private const val DB_SLIDER_STEPS = 45
private const val NAME_MAX_LENGTH = 64
private const val DISABLED_ALPHA = 0.4f
private const val MAX_CONTENT_WIDTH_DP = 520
private val TrimSteps = listOf(-10, -1, 1, 10)
private val MinTouch = 48.dp
private val PanelShape = RoundedCornerShape(8.dp)
private val ControlShape = RoundedCornerShape(6.dp)

data class ShownFrame(val frame: MonitorFrame, val flash: Flash)

private enum class Tone { Neutral, Ok, Warn, Alarm }

@Composable
fun MonitorScreen(viewModel: MonitorViewModel) {
    val ui by viewModel.ui.collectAsStateWithLifecycle()
    val keepScreenOn by viewModel.keepScreenOn.collectAsStateWithLifecycle()
    MonitorContent(ui, rememberShownFrame(viewModel), keepScreenOn, viewModel)
}

@Composable
fun MonitorContent(ui: MonitorUi, shown: State<ShownFrame>, keepScreenOn: Boolean, controls: MonitorControls) {
    KeepScreenOn(keepScreenOn)
    var pairingDismissed by rememberSaveable(ui.needsPairing) { mutableStateOf(false) }
    var kickedDismissed by rememberSaveable(ui.kicked) { mutableStateOf(false) }
    Box(Modifier.fillMaxSize().background(StageColors.Background).safeDrawingPadding()) {
        Column(
            modifier = Modifier
                .align(Alignment.TopCenter)
                .widthIn(max = MAX_CONTENT_WIDTH_DP.dp)
                .fillMaxWidth()
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 16.dp, vertical = 12.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            StatusRow(ui, showPairingAction = pairingDismissed, showRetryAction = kickedDismissed, controls)
            BeatPanel(shown)
            NowPlaying(shown)
            MixPanel(ui, controls)
            TrimPanel(ui, controls)
            DevicePanel(ui, keepScreenOn, controls)
        }
    }
    Overlays(
        ui = ui,
        pairingDismissed = pairingDismissed,
        kickedDismissed = kickedDismissed,
        onDismissPairing = { pairingDismissed = true },
        onDismissKicked = { kickedDismissed = true },
        controls = controls,
    )
}

@Composable
private fun rememberShownFrame(viewModel: MonitorViewModel): State<ShownFrame> {
    val lifecycleOwner = LocalLifecycleOwner.current
    val shown = remember { mutableStateOf(ShownFrame(viewModel.frame(), Flash.None)) }
    LaunchedEffect(viewModel, lifecycleOwner) {
        val flasher = BeatFlasher()
        lifecycleOwner.lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
            while (true) {
                withFrameNanos { nanos ->
                    val frame = viewModel.frame()
                    shown.value = ShownFrame(frame, flasher.update(frame.beat, nanos))
                }
            }
        }
    }
    return shown
}

@Composable
private fun KeepScreenOn(enabled: Boolean) {
    val view = LocalView.current
    DisposableEffect(view, enabled) {
        view.keepScreenOn = enabled
        onDispose { view.keepScreenOn = false }
    }
}

@Composable
private fun StatusRow(ui: MonitorUi, showPairingAction: Boolean, showRetryAction: Boolean, controls: MonitorControls) {
    FlowRow(
        horizontalArrangement = Arrangement.spacedBy(8.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        when (val status = ui.status) {
            LinkStatus.Offline -> Chip(stringResource(R.string.status_offline), Tone.Alarm)
            LinkStatus.Syncing -> Chip(stringResource(R.string.status_syncing), Tone.Warn)
            is LinkStatus.Locked -> Chip(stringResource(R.string.status_locked, status.uncertaintyMs), Tone.Ok)
        }
        if (ui.outputMissing) Chip(stringResource(R.string.badge_no_output), Tone.Alarm)
        if (ui.backingFallback) Chip(stringResource(R.string.badge_no_backing), Tone.Neutral)
        if (ui.needsPairing && showPairingAction) ActionChip(stringResource(R.string.pairing_action), controls::openPairing)
        if (ui.kicked && showRetryAction) ActionChip(stringResource(R.string.kicked_retry), controls::retryAfterKick)
    }
}

@Composable
private fun Chip(text: String, tone: Tone) {
    val background = if (tone == Tone.Alarm) StageColors.FaultSoft else StageColors.SurfaceHigh
    val content = when (tone) {
        Tone.Neutral -> StageColors.Dim
        Tone.Ok -> StageColors.Ok
        Tone.Warn -> StageColors.Signal
        Tone.Alarm -> StageColors.Fault
    }
    Text(
        text = text,
        style = StageText.Chip,
        color = content,
        modifier = Modifier
            .background(background, RoundedCornerShape(4.dp))
            .padding(horizontal = 10.dp, vertical = 8.dp),
    )
}

@Composable
private fun ActionChip(text: String, onClick: () -> Unit) {
    Text(
        text = text,
        style = StageText.Chip,
        color = StageColors.Signal,
        modifier = Modifier
            .defaultMinSize(minHeight = MinTouch)
            .clip(RoundedCornerShape(4.dp))
            .border(1.dp, StageColors.Signal, RoundedCornerShape(4.dp))
            .clickable(role = Role.Button, onClick = onClick)
            .padding(horizontal = 12.dp, vertical = 14.dp),
    )
}

@Composable
private fun BeatPanel(shown: State<ShownFrame>) {
    val current = shown.value
    val beat = current.frame.beat
    val description = stringResource(R.string.beat_description, beat.barBeat, beat.bpm, beat.meter)
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .background(if (current.flash == Flash.None) StageColors.Surface else StageColors.SignalSoft, PanelShape)
            .border(1.dp, if (current.flash == Flash.Accent) StageColors.Signal else StageColors.Line, PanelShape)
            .padding(16.dp)
            .clearAndSetSemantics { contentDescription = description },
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text(beat.barBeat, style = StageText.Counter, maxLines = 1, softWrap = false)
        FlowRow(
            modifier = Modifier.fillMaxWidth().heightIn(min = 18.dp).padding(vertical = 14.dp),
            horizontalArrangement = Arrangement.spacedBy(10.dp, Alignment.CenterHorizontally),
            verticalArrangement = Arrangement.spacedBy(10.dp),
            itemVerticalAlignment = Alignment.CenterVertically,
        ) {
            repeat(beat.beatsPerBar) { index -> BeatDot(accent = index == 0, lit = index == beat.litBeat) }
        }
        Text("${beat.bpm} · ${beat.meter}", style = StageText.Meta)
    }
}

@Composable
private fun BeatDot(accent: Boolean, lit: Boolean) {
    Box(
        Modifier
            .size(if (accent) 18.dp else 14.dp)
            .background(if (lit) StageColors.Signal else StageColors.SurfaceHigh, CircleShape),
    )
}

@Composable
private fun NowPlaying(shown: State<ShownFrame>) {
    val now = shown.value.frame.now
    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
        Text(
            text = now.title ?: stringResource(R.string.title_none),
            style = StageText.Title,
            maxLines = 2,
            overflow = TextOverflow.Ellipsis,
        )
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
            Text(now.position, style = StageText.Meta)
            Text(
                text = stringResource(R.string.next_format, now.nextTitle ?: stringResource(R.string.next_none)),
                style = StageText.Meta,
                textAlign = TextAlign.End,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f, fill = false).padding(start = 12.dp),
            )
        }
    }
}

@Composable
private fun Panel(modifier: Modifier = Modifier, content: @Composable () -> Unit) {
    Column(
        modifier = modifier
            .fillMaxWidth()
            .background(StageColors.Surface, PanelShape)
            .border(1.dp, StageColors.Line, PanelShape)
            .padding(12.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        content()
    }
}

@Composable
private fun MixPanel(ui: MonitorUi, controls: MonitorControls) {
    Panel {
        LevelRow(
            label = stringResource(R.string.label_backing),
            sliderDescription = stringResource(R.string.backing_level_description),
            toggleDescription = stringResource(R.string.backing_toggle_description),
            db = ui.levels.backingDb,
            on = ui.levels.backingOn,
            enabled = ui.hasSession,
            onDb = controls::setBackingDb,
            onToggle = controls::toggleBacking,
        )
        LevelRow(
            label = stringResource(R.string.label_click),
            sliderDescription = stringResource(R.string.click_level_description),
            toggleDescription = stringResource(R.string.click_toggle_description),
            db = ui.levels.clickDb,
            on = ui.levels.clickOn,
            enabled = ui.hasSession,
            onDb = controls::setClickDb,
            onToggle = controls::toggleClick,
        )
    }
}

@Composable
private fun LevelRow(
    label: String,
    sliderDescription: String,
    toggleDescription: String,
    db: Double,
    on: Boolean,
    enabled: Boolean,
    onDb: (Double) -> Unit,
    onToggle: () -> Unit,
) {
    val dbText = stringResource(R.string.level_db, formatDb(db))
    Column {
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Text(label, style = StageText.Label, modifier = Modifier.weight(1f))
            Text(dbText, style = StageText.Value)
            Spacer(Modifier.width(12.dp))
            ToggleButton(
                text = stringResource(if (on) R.string.toggle_on else R.string.toggle_off),
                on = on,
                enabled = enabled,
                description = toggleDescription,
                onToggle = onToggle,
            )
        }
        Slider(
            value = db.toFloat().coerceIn(DB_MIN, DB_MAX),
            onValueChange = { onDb(it.roundToInt().toDouble()) },
            valueRange = DB_MIN..DB_MAX,
            steps = DB_SLIDER_STEPS,
            enabled = enabled,
            modifier = Modifier
                .fillMaxWidth()
                .semantics {
                    contentDescription = sliderDescription
                    stateDescription = dbText
                },
        )
    }
}

@Composable
private fun ToggleButton(
    text: String,
    on: Boolean,
    enabled: Boolean,
    description: String,
    onToggle: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Box(
        modifier = modifier
            .alpha(if (enabled) 1f else DISABLED_ALPHA)
            .defaultMinSize(minWidth = 72.dp, minHeight = MinTouch)
            .clip(ControlShape)
            .background(if (on) StageColors.SignalSoft else StageColors.SurfaceAlt)
            .border(1.dp, if (on) StageColors.Signal else StageColors.Outline, ControlShape)
            .toggleable(value = on, enabled = enabled, role = Role.Switch, onValueChange = { onToggle() })
            .semantics { contentDescription = description }
            .padding(horizontal = 12.dp),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            text = text,
            style = StageText.Chip,
            color = if (on) StageColors.Signal else StageColors.Foreground,
            modifier = Modifier.clearAndSetSemantics {},
        )
    }
}

@Composable
private fun TrimPanel(ui: MonitorUi, controls: MonitorControls) {
    Panel {
        Text(stringResource(R.string.label_trim), style = StageText.Label)
        Text(
            text = stringResource(R.string.trim_value, ui.trimMs),
            style = StageText.Value.copy(fontSize = 28.sp),
            textAlign = TextAlign.Center,
            modifier = Modifier.fillMaxWidth(),
        )
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            TrimSteps.forEach { step -> StepButton(step, ui.hasSession, controls) }
        }
        ToggleButton(
            text = stringResource(R.string.test_click),
            on = ui.testClick,
            enabled = ui.hasSession,
            description = stringResource(R.string.test_click_description),
            onToggle = controls::toggleTestClick,
            modifier = Modifier.fillMaxWidth(),
        )
        Text(stringResource(R.string.trim_hint), style = StageText.Hint)
    }
}

@Composable
private fun RowScope.StepButton(step: Int, enabled: Boolean, controls: MonitorControls) {
    val size = abs(step)
    val down = step < 0
    val description = stringResource(if (down) R.string.trim_down_description else R.string.trim_up_description, size)
    OutlinedButton(
        onClick = { controls.adjustTrim(step) },
        enabled = enabled,
        shape = ControlShape,
        contentPadding = PaddingValues(0.dp),
        colors = ButtonDefaults.outlinedButtonColors(contentColor = StageColors.Foreground),
        modifier = Modifier
            .weight(1f)
            .heightIn(min = MinTouch)
            .semantics { contentDescription = description },
    ) {
        Text(
            text = stringResource(if (down) R.string.trim_step_down else R.string.trim_step_up, size),
            style = StageText.Value,
            color = Color.Unspecified,
            maxLines = 1,
            softWrap = false,
            modifier = Modifier.clearAndSetSemantics {},
        )
    }
}

@Composable
private fun DevicePanel(ui: MonitorUi, keepScreenOn: Boolean, controls: MonitorControls) {
    Panel {
        Text(stringResource(R.string.label_device), style = StageText.Label)
        NameField(ui.name, ui.hasSession, controls::setName)
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .heightIn(min = MinTouch)
                .toggleable(value = keepScreenOn, role = Role.Switch, onValueChange = controls::setKeepScreenOn),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(stringResource(R.string.keep_screen_on), style = StageText.Value, modifier = Modifier.weight(1f))
            Switch(checked = keepScreenOn, onCheckedChange = null)
        }
    }
}

@Composable
private fun NameField(name: String, enabled: Boolean, onCommit: (String) -> Unit) {
    var text by remember(name) { mutableStateOf(name) }
    val focus = LocalFocusManager.current
    val description = stringResource(R.string.device_description)
    OutlinedTextField(
        value = text,
        onValueChange = { text = it.take(NAME_MAX_LENGTH) },
        enabled = enabled,
        singleLine = true,
        textStyle = StageText.Value,
        placeholder = { Text(stringResource(R.string.device_placeholder), style = StageText.Value, color = StageColors.Dim) },
        keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done),
        keyboardActions = KeyboardActions(onDone = {
            onCommit(text)
            focus.clearFocus()
        }),
        modifier = Modifier
            .fillMaxWidth()
            .heightIn(min = MinTouch)
            .semantics { contentDescription = description }
            .onFocusChanged { if (!it.isFocused) onCommit(text) },
    )
}

@Composable
private fun Overlays(
    ui: MonitorUi,
    pairingDismissed: Boolean,
    kickedDismissed: Boolean,
    onDismissPairing: () -> Unit,
    onDismissKicked: () -> Unit,
    controls: MonitorControls,
) {
    when {
        ui.needsPairing && !pairingDismissed -> AlertDialog(
            onDismissRequest = onDismissPairing,
            properties = DialogProperties(dismissOnClickOutside = false),
            title = { Text(stringResource(R.string.pairing_title)) },
            text = { Text(stringResource(R.string.pairing_body)) },
            confirmButton = { DialogAction(stringResource(R.string.pairing_action), controls::openPairing) },
            dismissButton = { DialogAction(stringResource(R.string.dialog_dismiss), onDismissPairing) },
        )
        ui.kicked && !kickedDismissed -> AlertDialog(
            onDismissRequest = onDismissKicked,
            properties = DialogProperties(dismissOnClickOutside = false),
            title = { Text(stringResource(R.string.kicked_title)) },
            text = { Text(stringResource(R.string.kicked_body)) },
            confirmButton = { DialogAction(stringResource(R.string.kicked_retry), controls::retryAfterKick) },
            dismissButton = { DialogAction(stringResource(R.string.dialog_dismiss), onDismissKicked) },
        )
    }
}

@Composable
private fun DialogAction(text: String, onClick: () -> Unit) {
    TextButton(onClick = onClick, modifier = Modifier.heightIn(min = MinTouch)) {
        Text(text, style = StageText.Chip, color = StageColors.Signal)
    }
}
