package aria.monitor.app.ui

import aria.monitor.R
import aria.monitor.core.Pairing
import aria.monitor.core.pairingFromScan
import androidx.annotation.OptIn
import androidx.camera.core.ExperimentalGetImage
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.ImageProxy
import androidx.camera.view.CameraController
import androidx.camera.view.LifecycleCameraController
import androidx.camera.view.PreviewView
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.viewinterop.AndroidView
import androidx.lifecycle.compose.LocalLifecycleOwner
import com.google.mlkit.vision.barcode.BarcodeScanner
import com.google.mlkit.vision.barcode.BarcodeScannerOptions
import com.google.mlkit.vision.barcode.BarcodeScanning
import com.google.mlkit.vision.barcode.common.Barcode
import com.google.mlkit.vision.common.InputImage
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean

internal fun interface FrameDetector {
    fun detect(image: ImageProxy, done: (List<String>?) -> Unit)
}

internal class PairingScanAnalyzer(
    private val detector: FrameDetector,
    private val onPairing: (Pairing) -> Unit,
    private val onForeign: () -> Unit,
) : ImageAnalysis.Analyzer {
    private val finished = AtomicBoolean(false)

    override fun analyze(image: ImageProxy) {
        if (finished.get()) {
            image.close()
            return
        }
        val closed = AtomicBoolean(false)
        val release = { if (closed.compareAndSet(false, true)) image.close() }
        try {
            detector.detect(image) { payloads ->
                try {
                    if (payloads != null) inspect(payloads)
                } finally {
                    release()
                }
            }
        } catch (failure: RuntimeException) {
            release()
        }
    }

    private fun inspect(payloads: List<String>) {
        val pairing = payloads.firstNotNullOfOrNull(::pairingFromScan)
        when {
            pairing != null -> if (finished.compareAndSet(false, true)) onPairing(pairing)
            payloads.isNotEmpty() && !finished.get() -> onForeign()
        }
    }
}

@OptIn(ExperimentalGetImage::class)
internal class MlKitFrameDetector(private val scanner: BarcodeScanner) : FrameDetector {
    override fun detect(image: ImageProxy, done: (List<String>?) -> Unit) {
        val media = image.image
        if (media == null) {
            done(null)
            return
        }
        scanner.process(InputImage.fromMediaImage(media, image.imageInfo.rotationDegrees))
            .addOnSuccessListener { barcodes -> done(barcodes.mapNotNull { it.rawValue }) }
            .addOnFailureListener { done(null) }
    }
}

@Composable
fun QrScanner(onPairing: (Pairing) -> Unit, onForeign: () -> Unit, modifier: Modifier = Modifier) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    val paired by rememberUpdatedState(onPairing)
    val foreign by rememberUpdatedState(onForeign)
    val scanner = remember {
        BarcodeScanning.getClient(BarcodeScannerOptions.Builder().setBarcodeFormats(Barcode.FORMAT_QR_CODE).build())
    }
    val executor = remember { Executors.newSingleThreadExecutor() }
    val controller = remember { LifecycleCameraController(context) }
    DisposableEffect(lifecycleOwner, controller) {
        val analyzer = PairingScanAnalyzer(
            detector = MlKitFrameDetector(scanner),
            onPairing = {
                controller.clearImageAnalysisAnalyzer()
                paired(it)
            },
            onForeign = { foreign() },
        )
        controller.setEnabledUseCases(CameraController.IMAGE_ANALYSIS)
        controller.setImageAnalysisBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
        controller.setImageAnalysisAnalyzer(executor, analyzer)
        controller.bindToLifecycle(lifecycleOwner)
        onDispose {
            controller.clearImageAnalysisAnalyzer()
            controller.unbind()
            executor.shutdown()
            scanner.close()
        }
    }
    val description = stringResource(R.string.scanner_description)
    AndroidView(
        factory = { PreviewView(it).apply { this.controller = controller } },
        modifier = modifier.semantics { contentDescription = description },
    )
}
