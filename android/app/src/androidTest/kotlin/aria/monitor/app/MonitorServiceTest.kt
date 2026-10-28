package aria.monitor.app

import aria.monitor.core.Pairing
import android.Manifest
import android.app.ActivityManager
import android.app.Notification
import android.app.NotificationManager
import android.content.Context
import android.content.Intent
import android.net.ConnectivityManager
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.rule.GrantPermissionRule
import androidx.test.rule.ServiceTestRule
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertSame
import org.junit.Assert.assertTrue
import org.junit.Assume.assumeTrue
import org.junit.Before
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import java.io.IOException
import java.io.InputStream
import java.net.InetAddress
import java.net.ServerSocket
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit
import kotlin.concurrent.thread

private const val DEADLINE_MS = 10_000L
private const val POLL_MS = 50L
private const val SETTLE_MS = 500L
private const val HANDOVER_SETTLE_MS = 6000L
private const val READ_TIMEOUT_MS = 2000
private const val PROBE_IDENTIFIER = "probe-phone"
private const val PROBE_PASSWORD = "probe-secret"

private class AuthProbe : AutoCloseable {
    private val server = ServerSocket(0, 8, InetAddress.getByName("127.0.0.1"))
    val port: Int get() = server.localPort
    val requests = LinkedBlockingQueue<String>()

    init {
        thread(isDaemon = true) {
            while (!server.isClosed) {
                try {
                    server.accept().use { socket ->
                        socket.soTimeout = READ_TIMEOUT_MS
                        requests.add(readRequest(socket.getInputStream()))
                        socket.getOutputStream()
                            .write("HTTP/1.1 503 Service Unavailable\r\nContent-Length: 0\r\nConnection: close\r\n\r\n".toByteArray())
                    }
                } catch (closed: IOException) {
                    continue
                }
            }
        }
    }

    private fun readRequest(input: InputStream): String {
        val head = StringBuilder()
        while (!head.endsWith("\r\n\r\n")) {
            val next = input.read()
            if (next < 0) break
            head.append(next.toChar())
        }
        val length = Regex("(?i)content-length: *(\\d+)").find(head)?.groupValues?.get(1)?.toInt() ?: 0
        val body = ByteArray(length)
        var read = 0
        while (read < length) {
            val count = input.read(body, read, length - read)
            if (count < 0) break
            read += count
        }
        return head.toString() + String(body, 0, read)
    }

    override fun close() = server.close()
}

@RunWith(AndroidJUnit4::class)
class MonitorServiceTest {
    @get:Rule
    val notifications: GrantPermissionRule = GrantPermissionRule.grant(Manifest.permission.POST_NOTIFICATIONS)

    @get:Rule
    val serviceRule = ServiceTestRule()

    private val context: Context = InstrumentationRegistry.getInstrumentation().targetContext
    private val settings = AppStores.settings(context)

    @Before
    fun forgetPairing() = runBlocking { settings.savePairing(null) }

    @After
    fun stopService() {
        serviceRule.unbindService()
        context.stopService(Intent(context, MonitorService::class.java))
        eventually { !serviceAlive() }
        runBlocking { settings.savePairing(null) }
    }

    @Test
    fun startMakesTheServiceForeground() {
        assertTrue(MonitorService.start(context, null))

        eventually { foregroundNotification() != null }
        assertTrue(serviceRunning())
    }

    @Test
    fun stopActionStopsTheService() {
        assertTrue(MonitorService.start(context, null))
        eventually { foregroundNotification() != null }

        context.startService(MonitorService.stopIntent(context))

        eventually { !serviceRunning() }
        eventually { foregroundNotification() == null }
    }

    @Test
    fun binderExposesTheSession() {
        assertTrue(MonitorService.start(context, null))
        eventually { foregroundNotification() != null }

        val access = bind()

        eventually { access.session.value?.state?.value?.needsPairing == true }
    }

    @Test
    fun restartWithoutIntentUsesSavedPairing() {
        AuthProbe().use { probe ->
            runBlocking { settings.savePairing(Pairing("127.0.0.1", probe.port, PROBE_IDENTIFIER, PROBE_PASSWORD)) }

            context.startService(Intent(context, MonitorService::class.java))

            val request = probe.requests.poll(DEADLINE_MS, TimeUnit.MILLISECONDS)
            assertNotNull("the saved pairing was not used", request)
            assertTrue(request, request!!.startsWith("POST /auth "))
            assertTrue(request, request.contains("\"identifier\":\"$PROBE_IDENTIFIER\""))
            val access = bind()
            eventually { access.session.value != null }
            assertTrue(access.session.value?.state?.value?.needsPairing == false)
        }
    }

    @Test
    fun startTwiceDoesNotRestartTheEngine() {
        assertTrue(MonitorService.start(context, null))
        eventually { foregroundNotification() != null }
        val access = bind()
        eventually { access.sessionStarts == 1 }
        val session = access.session.value

        assertTrue(MonitorService.start(context, null))
        Thread.sleep(SETTLE_MS)

        assertSame(session, access.session.value)
        assertEquals(1, access.sessionStarts)
    }

    @Test
    fun stopThenImmediateStartKeepsTheWifiBinding() {
        val connectivity = context.getSystemService(ConnectivityManager::class.java)
        assertTrue(MonitorService.start(context, null))
        eventually { foregroundNotification() != null }
        val hasWifi = waitFor { connectivity.boundNetworkForProcess != null }
        assumeTrue("no Wi-Fi network on this device", hasWifi)

        context.startService(MonitorService.stopIntent(context))
        eventually { !serviceAlive() }
        assertTrue(MonitorService.start(context, null))
        eventually { foregroundNotification() != null && serviceRunning() }
        Thread.sleep(HANDOVER_SETTLE_MS)

        assertNotNull(connectivity.boundNetworkForProcess)
        assertTrue(serviceRunning())
    }

    @Test
    fun aStartRightAfterStopKeepsTheMonitorRunning() {
        assertTrue(MonitorService.start(context, null))
        eventually { foregroundNotification() != null }

        context.startService(MonitorService.stopIntent(context))
        assertTrue(MonitorService.start(context, null))
        Thread.sleep(HANDOVER_SETTLE_MS)

        assertTrue(serviceRunning())
        assertNotNull(foregroundNotification())
    }

    private fun bind(): MonitorService.Access =
        serviceRule.bindService(Intent(context, MonitorService::class.java)) as MonitorService.Access

    private fun foregroundNotification(): Notification? =
        context.getSystemService(NotificationManager::class.java).activeNotifications
            .firstOrNull { it.id == MONITOR_NOTIFICATION_ID && it.notification.flags and Notification.FLAG_FOREGROUND_SERVICE != 0 }
            ?.notification

    @Suppress("DEPRECATION")
    private fun serviceRunning(): Boolean =
        context.getSystemService(ActivityManager::class.java).getRunningServices(Int.MAX_VALUE)
            .any { it.service.className == MonitorService::class.java.name && it.started }

    @Suppress("DEPRECATION")
    private fun serviceAlive(): Boolean =
        context.getSystemService(ActivityManager::class.java).getRunningServices(Int.MAX_VALUE)
            .any { it.service.className == MonitorService::class.java.name }

    private fun waitFor(condition: () -> Boolean): Boolean {
        val deadline = System.currentTimeMillis() + DEADLINE_MS
        while (!condition()) {
            if (System.currentTimeMillis() > deadline) return false
            Thread.sleep(POLL_MS)
        }
        return true
    }

    private fun eventually(condition: () -> Boolean) {
        val deadline = System.currentTimeMillis() + DEADLINE_MS
        while (!condition()) {
            if (System.currentTimeMillis() > deadline) throw AssertionError("condition not reached in $DEADLINE_MS ms")
            Thread.sleep(POLL_MS)
        }
    }
}
