package aria.monitor.app.ui

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.unit.sp

object StageColors {
    val Background = Color(0xFF0F0F0F)
    val Surface = Color(0xFF161616)
    val SurfaceAlt = Color(0xFF1F1F1F)
    val SurfaceHigh = Color(0xFF2A2A2A)
    val Line = Color(0xFF2C2C2C)
    val Outline = Color(0xFF7A7A7A)
    val Foreground = Color(0xFFECECEC)
    val Dim = Color(0xFF9A9A9A)
    val Signal = Color(0xFFE5A63B)
    val SignalSoft = Color(0xFF2A2113)
    val Ok = Color(0xFF6FCF7B)
    val Fault = Color(0xFFFF7075)
    val FaultSoft = Color(0xFF281314)
}

object StageText {
    val Mono = TextStyle(fontFamily = FontFamily.Monospace, color = StageColors.Foreground, fontSize = 16.sp)
    val Label = Mono.copy(fontSize = 12.sp, letterSpacing = 1.5.sp, color = StageColors.Dim)
    val Chip = Mono.copy(fontSize = 13.sp, letterSpacing = 1.sp)
    val Counter = Mono.copy(fontSize = 72.sp, letterSpacing = (-1.5).sp)
    val Meta = Mono.copy(fontSize = 16.sp, color = StageColors.Dim)
    val Title = TextStyle(fontSize = 22.sp, color = StageColors.Foreground)
    val Value = Mono.copy(fontSize = 16.sp)
    val Hint = TextStyle(fontSize = 13.sp, color = StageColors.Dim)
}

private val StageColorScheme = darkColorScheme(
    primary = StageColors.Signal,
    onPrimary = StageColors.Background,
    secondary = StageColors.Signal,
    onSecondary = StageColors.Background,
    secondaryContainer = StageColors.SurfaceHigh,
    onSecondaryContainer = StageColors.Dim,
    background = StageColors.Background,
    onBackground = StageColors.Foreground,
    surface = StageColors.Surface,
    onSurface = StageColors.Foreground,
    surfaceVariant = StageColors.SurfaceHigh,
    onSurfaceVariant = StageColors.Dim,
    surfaceContainerHigh = StageColors.SurfaceAlt,
    surfaceContainerHighest = StageColors.SurfaceHigh,
    outline = StageColors.Outline,
    outlineVariant = StageColors.Line,
    error = StageColors.Fault,
    onError = StageColors.Background,
)

@Composable
fun MonitorTheme(content: @Composable () -> Unit) {
    MaterialTheme(colorScheme = StageColorScheme, content = content)
}
