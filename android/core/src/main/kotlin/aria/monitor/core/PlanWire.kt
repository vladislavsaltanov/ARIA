package aria.monitor.core

import java.io.ByteArrayOutputStream
import java.nio.BufferUnderflowException
import java.nio.ByteBuffer
import java.nio.ByteOrder

private const val MAGIC = 0x4C505241
private const val MIN_VOICE_BYTES = 51
private const val MAX_UNSIGNED_INT = 0xFFFFFFFFL

object PlanWire {
    const val VERSION = 1

    fun encode(plan: RenderPlan): ByteArray {
        val out = Writer()
        out.int(MAGIC)
        out.int(VERSION)
        out.long(plan.generation)
        out.double(plan.rate)
        out.list(plan.voices) { out.voice(it) }
        out.optional(plan.grid) { out.grid(it) }
        out.optional(plan.test) { out.test(it) }
        out.float(plan.backingGain)
        out.float(plan.clickGain)
        out.float(plan.masterGain)
        out.float(plan.masterRampSeconds)
        out.long(plan.trimUs)
        out.long(plan.timeMap.baseFrame)
        out.long(plan.timeMap.hostUsAtBaseFrame)
        out.double(plan.timeMap.usPerFrame)
        return out.toByteArray()
    }

    fun decode(bytes: ByteArray): RenderPlan {
        val input = Reader(ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN))
        try {
            require(input.int() == MAGIC) { "bad magic" }
            val version = input.int()
            require(version == VERSION) { "unsupported version $version" }
            val plan = RenderPlan(
                generation = input.long(),
                rate = input.double(),
                voices = input.list(MIN_VOICE_BYTES) { input.voice() },
                grid = input.optional { input.grid() },
                test = input.optional { input.test() },
                backingGain = input.float(),
                clickGain = input.float(),
                masterGain = input.float(),
                masterRampSeconds = input.float(),
                trimUs = input.long(),
                timeMap = TimeMap(input.long(), input.long(), input.double()),
            )
            require(input.remaining() == 0) { "${input.remaining()} trailing bytes" }
            return plan
        } catch (e: BufferUnderflowException) {
            throw IllegalArgumentException("truncated plan", e)
        }
    }

    private class Writer {
        private val out = ByteArrayOutputStream()
        private val scratch = ByteBuffer.allocate(Long.SIZE_BYTES).order(ByteOrder.LITTLE_ENDIAN)

        fun toByteArray(): ByteArray = out.toByteArray()

        private fun flush(size: Int) {
            out.write(scratch.array(), 0, size)
            scratch.clear()
        }

        fun int(value: Int) = flush(scratch.putInt(value).position())

        fun long(value: Long) = flush(scratch.putLong(value).position())

        fun float(value: Float) = flush(scratch.putFloat(value).position())

        fun double(value: Double) = flush(scratch.putDouble(value).position())

        fun boolean(value: Boolean) = out.write(if (value) 1 else 0)

        fun string(value: String) {
            val utf8 = value.toByteArray(Charsets.UTF_8)
            int(utf8.size)
            out.write(utf8, 0, utf8.size)
        }

        fun <T : Any> optional(value: T?, write: (T) -> Unit) {
            boolean(value != null)
            if (value != null) write(value)
        }

        fun <T> list(values: List<T>, write: (T) -> Unit) {
            int(values.size)
            values.forEach(write)
        }

        fun voice(voice: VoicePlan) {
            int(voice.id)
            int(voice.epoch)
            long(voice.trackHi)
            long(voice.trackLo)
            double(voice.gainDb)
            long(voice.anchorUs)
            long(voice.anchorFrame)
            boolean(voice.playing)
            optional(voice.endFrame) { long(it) }
            optional(voice.fade) { fade(it) }
        }

        fun fade(fade: FadeTimeline) {
            double(fade.from)
            double(fade.to)
            double(fade.progress)
            int(fade.frames)
            string(fade.curve)
            boolean(fade.stopAtEnd)
        }

        fun grid(grid: GridTimeline) {
            int(grid.voice)
            double(grid.bpm)
            int(grid.beatsPerBar)
            long(grid.firstBeatFrame)
        }

        fun test(test: TestTimeline) {
            long(test.anchorUs)
            double(test.intervalUs)
        }
    }

    private class Reader(private val buffer: ByteBuffer) {
        fun remaining(): Int = buffer.remaining()

        fun int(): Int = buffer.getInt()

        fun long(): Long = buffer.getLong()

        fun float(): Float = buffer.getFloat()

        fun double(): Double = buffer.getDouble()

        fun boolean(): Boolean =
            when (val flag = buffer.get().toInt()) {
                0 -> false
                1 -> true
                else -> throw IllegalArgumentException("invalid flag byte $flag")
            }

        private fun count(minElementBytes: Int): Int {
            val count = buffer.getInt().toLong() and MAX_UNSIGNED_INT
            require(count <= buffer.remaining() / minElementBytes) { "count $count exceeds remaining ${buffer.remaining()} bytes" }
            return count.toInt()
        }

        fun string(): String {
            val bytes = ByteArray(count(1))
            buffer.get(bytes)
            return String(bytes, Charsets.UTF_8)
        }

        fun <T : Any> optional(read: () -> T): T? = if (boolean()) read() else null

        fun <T> list(minElementBytes: Int, read: () -> T): List<T> = List(count(minElementBytes)) { read() }

        fun voice() = VoicePlan(
            id = int(),
            epoch = int(),
            trackHi = long(),
            trackLo = long(),
            gainDb = double(),
            anchorUs = long(),
            anchorFrame = long(),
            playing = boolean(),
            endFrame = optional { long() },
            fade = optional { fade() },
        )

        fun fade() = FadeTimeline(
            from = double(),
            to = double(),
            progress = double(),
            frames = int(),
            curve = string(),
            stopAtEnd = boolean(),
        )

        fun grid() = GridTimeline(voice = int(), bpm = double(), beatsPerBar = int(), firstBeatFrame = long())

        fun test() = TestTimeline(anchorUs = long(), intervalUs = double())
    }
}
