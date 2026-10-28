package aria.monitor.core

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonElement
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlin.test.assertNotNull
import kotlin.test.assertNull

class ProtocolTest {
    private fun fixture(name: String): String =
        checkNotNull(ProtocolTest::class.java.getResource("/monitor/$name.json")) { "fixture $name missing" }.readText()

    private fun tree(text: String): JsonElement = Json.parseToJsonElement(text)

    private fun timeline(name: String): ServerMessage.Timeline =
        assertIs<ServerMessage.Timeline>(ProtocolCodec.decode(fixture(name)))

    @Test
    fun decodesWelcome() {
        assertEquals(ServerMessage.Welcome("L1", "listen", 48000), ProtocolCodec.decode(fixture("welcome")))
    }

    @Test
    fun decodesTimelineWithFadeGridAndNext() {
        val message = timeline("timeline")

        assertEquals(42L, message.seq)
        assertEquals(48000.0, message.rate)
        assertEquals(2, message.voices.size)
        val first = message.voices[0]
        assertEquals(1, first.id)
        assertEquals("entry", first.kind)
        assertEquals(96000L, first.anchorFrame)
        assertEquals(-3.0, first.gainDb)
        assertEquals(true, first.playing)
        assertNull(first.endFrame)
        assertNull(first.fade)
        val second = message.voices[1]
        assertEquals("backing", second.kind)
        assertEquals(480000L, second.endFrame)
        val fade = assertNotNull(second.fade)
        assertEquals("Logarithmic", fade.curve)
        assertEquals(1.0, fade.from)
        assertEquals(0.0, fade.to)
        assertEquals(0.25, fade.progress)
        assertEquals(38400, fade.frames)
        assertEquals(true, fade.stopAtEnd)
        assertEquals(GridTimeline(voice = 1, bpm = 120.0, beatsPerBar = 4, firstBeatFrame = 0L), message.grid)
        assertEquals("Playing", message.deck.status)
        assertEquals("Song", message.deck.title)
        assertEquals("Next song", message.deck.next?.title)
        assertEquals("entry", message.deck.next?.kind)
        assertEquals(24000L, message.deck.next?.startFrame)
        assertNull(message.test)
    }

    @Test
    fun decodesTimelineWithTestClick() {
        val message = timeline("timeline-test")

        assertEquals(TestTimeline(anchorUs = 13_000_000_500_000L, intervalUs = 1_000_000.0), message.test)
    }

    @Test
    fun int64SurvivesDecode() {
        val message = timeline("timeline")

        assertEquals(13_000_000_000_000L, message.voices[0].anchorUs)
        assertEquals(13_000_000_020_000L, message.voices[1].anchorUs)
    }

    @Test
    fun decodesStatusAndLevels() {
        assertEquals(ServerMessage.Status(true), ProtocolCodec.decode(fixture("status")))
        assertEquals(
            ServerMessage.LevelsUpdate(Levels(backingDb = -6.0, backingOn = true, clickDb = -3.0, clickOn = false)),
            ProtocolCodec.decode(fixture("levels")),
        )
    }

    @Test
    fun decodesPongKickedRevoked() {
        assertEquals(
            ServerMessage.Pong(t0 = 123.5, t1Us = 13_000_000_000_100L, t2Us = 13_000_000_000_150L),
            ProtocolCodec.decode(fixture("pong")),
        )
        assertEquals(ServerMessage.Kicked, ProtocolCodec.decode(fixture("kicked")))
        assertEquals(ServerMessage.Revoked, ProtocolCodec.decode(fixture("revoked")))
    }

    @Test
    fun unknownTypeReturnsNull() {
        assertNull(ProtocolCodec.decode("""{"type":"somethingNew","x":1}"""))
        assertNull(ProtocolCodec.decode("""{"listener":"L1"}"""))
        assertNull(ProtocolCodec.decode("""{"type":7}"""))
    }

    @Test
    fun malformedJsonReturnsNull() {
        assertNull(ProtocolCodec.decode(""))
        assertNull(ProtocolCodec.decode("{"))
        assertNull(ProtocolCodec.decode("not json"))
        assertNull(ProtocolCodec.decode("[1,2]"))
        assertNull(ProtocolCodec.decode("null"))
        assertNull(ProtocolCodec.decode("""{"type":"welcome","listener":"L1"}"""))
        assertNull(ProtocolCodec.decode("""{"type":"welcome","listener":"L1","role":"listen","sampleRate":"fast"}"""))
        assertNull(ProtocolCodec.decode("""{"type":"timeline","seq":1,"rate":48000,"voices":"none","deck":{"status":"Stopped"}}"""))
    }

    @Test
    fun deeplyNestedJsonReturnsNull() {
        assertNull(ProtocolCodec.decode("[".repeat(200_000)))
        assertNull(ProtocolCodec.decode("""{"type":"welcome","x":""" + "[".repeat(200_000)))
    }

    @Test
    fun missingOptionalKeysDecodeAsNull() {
        val message = assertIs<ServerMessage.Timeline>(
            ProtocolCodec.decode("""{"type":"timeline","seq":1,"rate":48000,"voices":[],"deck":{"status":"Stopped"}}"""),
        )

        assertEquals(emptyList(), message.voices)
        assertNull(message.grid)
        assertNull(message.test)
        assertNull(message.deck.title)
        assertNull(message.deck.next)
    }

    @Test
    fun ignoresUnknownKeys() {
        assertEquals(
            ServerMessage.Welcome("L2", "control", 48000),
            ProtocolCodec.decode("""{"type":"welcome","listener":"L2","role":"control","sampleRate":48000,"future":{"a":1}}"""),
        )
    }

    @Test
    fun encodesPing() {
        assertEquals(tree("""{"type":"ping","t0":123.5}"""), tree(ProtocolCodec.encode(ClientMessage.Ping(123.5))))
    }

    @Test
    fun encodesHelloWithLevels() {
        val encoded = ProtocolCodec.encode(
            ClientMessage.Hello("Барабаны", Levels(backingDb = -6.0, backingOn = true, clickDb = -3.0, clickOn = false)),
        )

        assertEquals(
            tree("""{"type":"hello","name":"Барабаны","levels":{"backingDb":-6.0,"backingOn":true,"clickDb":-3.0,"clickOn":false}}"""),
            tree(encoded),
        )
    }

    @Test
    fun encodesLevelsChangedFlat() {
        val encoded = ProtocolCodec.encode(
            ClientMessage.LevelsChanged(Levels(backingDb = -12.0, backingOn = false, clickDb = 2.0, clickOn = true)),
        )

        assertEquals(
            tree("""{"type":"levels","backingDb":-12.0,"backingOn":false,"clickDb":2.0,"clickOn":true}"""),
            tree(encoded),
        )
    }

    @Test
    fun encodesStats() {
        val encoded = ProtocolCodec.encode(
            ClientMessage.Stats(
                offsetUs = 1834L,
                uncertaintyUs = 900L,
                rttUs = 6100L,
                outputLatencyMs = 21.3,
                trimMs = 4.0,
                state = "locked",
            ),
        )

        assertEquals(
            tree("""{"type":"stats","offsetUs":1834,"uncertaintyUs":900,"rttUs":6100,"outputLatencyMs":21.3,"trimMs":4.0,"state":"locked"}"""),
            tree(encoded),
        )
    }

    @Test
    fun encodesInt64WithoutLossOrFraction() {
        val encoded = ProtocolCodec.encode(
            ClientMessage.Stats(
                offsetUs = 13_000_000_000_000L,
                uncertaintyUs = 0L,
                rttUs = 0L,
                outputLatencyMs = 0.0,
                trimMs = 0.0,
                state = "syncing",
            ),
        )

        assertEquals(true, """"offsetUs":13000000000000,""" in encoded)
    }
}
