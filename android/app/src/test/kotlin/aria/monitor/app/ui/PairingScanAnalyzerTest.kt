package aria.monitor.app.ui

import aria.monitor.core.Pairing
import android.graphics.Rect
import android.media.Image
import androidx.camera.core.ExperimentalGetImage
import androidx.camera.core.ImageInfo
import androidx.camera.core.ImageProxy
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertTrue

private const val LINK = "http://192.168.1.10:5000/monitor?id=abc&key=xyz"
private val LINK_PAIRING = Pairing("192.168.1.10", 5000, "abc", "xyz")

private class FakeFrame : ImageProxy {
    var closes = 0

    override fun close() {
        closes++
    }

    override fun getCropRect(): Rect = throw UnsupportedOperationException()

    override fun setCropRect(rect: Rect?) = throw UnsupportedOperationException()

    override fun getFormat(): Int = throw UnsupportedOperationException()

    override fun getHeight(): Int = throw UnsupportedOperationException()

    override fun getWidth(): Int = throw UnsupportedOperationException()

    override fun getPlanes(): Array<ImageProxy.PlaneProxy> = throw UnsupportedOperationException()

    override fun getImageInfo(): ImageInfo = throw UnsupportedOperationException()

    @ExperimentalGetImage
    override fun getImage(): Image? = throw UnsupportedOperationException()
}

private class ScriptedDetector(private val answer: (ImageProxy, (List<String>?) -> Unit) -> Unit) : FrameDetector {
    var calls = 0

    override fun detect(image: ImageProxy, done: (List<String>?) -> Unit) {
        calls++
        answer(image, done)
    }
}

private class Outcome {
    val pairings = mutableListOf<Pairing>()
    var foreign = 0
}

private fun analyzerOver(detector: FrameDetector, outcome: Outcome) =
    PairingScanAnalyzer(detector, { outcome.pairings += it }, { outcome.foreign++ })

class PairingScanAnalyzerTest {
    @Test
    fun aFrameWithAPairingQrDeliversItAndIsClosed() {
        val outcome = Outcome()
        val frame = FakeFrame()

        analyzerOver(ScriptedDetector { _, done -> done(listOf(LINK)) }, outcome).analyze(frame)

        assertEquals(listOf(LINK_PAIRING), outcome.pairings)
        assertEquals(1, frame.closes)
    }

    @Test
    fun theFrameStaysOpenUntilTheDetectorFinishes() {
        val outcome = Outcome()
        val frame = FakeFrame()
        var finish: ((List<String>?) -> Unit)? = null

        analyzerOver(ScriptedDetector { _, done -> finish = done }, outcome).analyze(frame)

        assertEquals(0, frame.closes)
        finish!!(listOf(LINK))
        assertEquals(1, frame.closes)
        assertEquals(listOf(LINK_PAIRING), outcome.pairings)
    }

    @Test
    fun theFirstValidPairingWinsAndLaterFramesAreNotAnalysed() {
        val outcome = Outcome()
        val detector = ScriptedDetector { _, done -> done(listOf(LINK)) }
        val analyzer = analyzerOver(detector, outcome)
        val frames = List(4) { FakeFrame() }

        frames.forEach(analyzer::analyze)

        assertEquals(1, outcome.pairings.size)
        assertEquals(1, detector.calls)
        assertEquals(listOf(1, 1, 1, 1), frames.map { it.closes })
    }

    @Test
    fun twoCodesInOneFrameDeliverTheFirstValidOnly() {
        val outcome = Outcome()
        val other = "http://10.0.0.2:6000/monitor?id=x&key=y"

        analyzerOver(ScriptedDetector { _, done -> done(listOf("hello", LINK, other)) }, outcome).analyze(FakeFrame())

        assertEquals(listOf(LINK_PAIRING), outcome.pairings)
        assertEquals(0, outcome.foreign)
    }

    @Test
    fun resultsArrivingAfterTheWinnerAreIgnoredButTheirFramesAreClosed() {
        val outcome = Outcome()
        val pending = mutableListOf<(List<String>?) -> Unit>()
        val analyzer = analyzerOver(ScriptedDetector { _, done -> pending += done }, outcome)
        val first = FakeFrame()
        val second = FakeFrame()
        analyzer.analyze(first)
        analyzer.analyze(second)

        pending[0](listOf(LINK))
        pending[1](listOf("http://10.0.0.2:6000/monitor?id=x&key=y"))

        assertEquals(listOf(LINK_PAIRING), outcome.pairings)
        assertEquals(listOf(1, 1), listOf(first.closes, second.closes))
    }

    @Test
    fun foreignCodeIsReportedAndNeverPairs() {
        val outcome = Outcome()
        val frame = FakeFrame()

        analyzerOver(ScriptedDetector { _, done -> done(listOf("https://example.com")) }, outcome).analyze(frame)

        assertTrue(outcome.pairings.isEmpty())
        assertEquals(1, outcome.foreign)
        assertEquals(1, frame.closes)
    }

    @Test
    fun aForeignResultArrivingAfterTheWinnerIsNotReported() {
        val outcome = Outcome()
        val pending = mutableListOf<(List<String>?) -> Unit>()
        val analyzer = analyzerOver(ScriptedDetector { _, done -> pending += done }, outcome)
        analyzer.analyze(FakeFrame())
        analyzer.analyze(FakeFrame())

        pending[0](listOf(LINK))
        pending[1](listOf("https://example.com"))

        assertEquals(0, outcome.foreign)
    }

    @Test
    fun emptyAndFailedFramesAreClosedWithoutOutcome() {
        val outcome = Outcome()
        val empty = FakeFrame()
        val failed = FakeFrame()

        analyzerOver(ScriptedDetector { _, done -> done(emptyList()) }, outcome).analyze(empty)
        analyzerOver(ScriptedDetector { _, done -> done(null) }, outcome).analyze(failed)

        assertTrue(outcome.pairings.isEmpty())
        assertEquals(0, outcome.foreign)
        assertEquals(listOf(1, 1), listOf(empty.closes, failed.closes))
    }

    @Test
    fun aDetectorThatThrowsStillGetsItsFrameClosedOnce() {
        val outcome = Outcome()
        val frame = FakeFrame()

        analyzerOver(ScriptedDetector { _, _ -> throw IllegalStateException("scanner closed") }, outcome).analyze(frame)

        assertEquals(1, frame.closes)
        assertTrue(outcome.pairings.isEmpty())
    }

    @Test
    fun aDetectorThatThrowsAfterReportingDoesNotCloseTwice() {
        val outcome = Outcome()
        val frame = FakeFrame()

        analyzerOver(
            ScriptedDetector { _, done ->
                done(listOf(LINK))
                throw IllegalStateException("late")
            },
            outcome,
        ).analyze(frame)

        assertEquals(1, frame.closes)
        assertEquals(listOf(LINK_PAIRING), outcome.pairings)
    }

    @Test
    fun aDetectorReportingTwiceClosesOnce() {
        val outcome = Outcome()
        val frame = FakeFrame()

        analyzerOver(ScriptedDetector { _, done -> done(null); done(null) }, outcome).analyze(frame)

        assertEquals(1, frame.closes)
    }

    @Test
    fun aFailingPairingCallbackStillClosesTheFrame() {
        val frame = FakeFrame()
        val analyzer = PairingScanAnalyzer(ScriptedDetector { _, done -> done(listOf(LINK)) }, { throw IllegalStateException("ui") }, {})

        runCatching { analyzer.analyze(frame) }

        assertEquals(1, frame.closes)
    }

    @Test
    fun aDetectorThatThrowsAnErrorStillGetsItsFrameClosedAndTheErrorSurfaces() {
        val outcome = Outcome()
        val frame = FakeFrame()

        assertFailsWith<UnsatisfiedLinkError> {
            analyzerOver(ScriptedDetector { _, _ -> throw UnsatisfiedLinkError("barhopper") }, outcome).analyze(frame)
        }

        assertEquals(1, frame.closes)
        assertTrue(outcome.pairings.isEmpty())
    }
}
