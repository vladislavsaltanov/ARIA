package aria.monitor.app.ui

import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp

private val ButtonShape = RoundedCornerShape(6.dp)
private val ButtonMinHeight = 56.dp

@Composable
internal fun StageButton(text: String, primary: Boolean, onClick: () -> Unit, modifier: Modifier = Modifier) {
    val sized = modifier.fillMaxWidth().heightIn(min = ButtonMinHeight)
    if (primary) {
        Button(onClick = onClick, shape = ButtonShape, modifier = sized) { Text(text, style = StageText.Chip, color = StageColors.Background) }
    } else {
        OutlinedButton(
            onClick = onClick,
            shape = ButtonShape,
            colors = ButtonDefaults.outlinedButtonColors(contentColor = StageColors.Foreground),
            modifier = sized,
        ) { Text(text, style = StageText.Chip) }
    }
}
