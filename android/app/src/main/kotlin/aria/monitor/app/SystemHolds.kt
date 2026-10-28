package aria.monitor.app

import android.annotation.SuppressLint
import android.content.Context
import android.media.AudioAttributes
import android.media.AudioFocusRequest
import android.media.AudioManager
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.net.NetworkRequest
import android.net.wifi.WifiManager
import android.os.Build
import android.os.PowerManager
import android.util.Log

private const val TAG = "AriaMonitor"
private const val LOCK_TAG = "aria:monitor"
private const val LOW_LATENCY_WIFI_SDK = 29

@SuppressLint("InlinedApi")
@Suppress("DEPRECATION")
internal fun wifiLockMode(sdkInt: Int): Int =
    if (sdkInt >= LOW_LATENCY_WIFI_SDK) WifiManager.WIFI_MODE_FULL_LOW_LATENCY else WifiManager.WIFI_MODE_FULL_HIGH_PERF

internal class SystemHolds(context: Context, private val processNetwork: OwnedBinding) : Holds {
    private val app = context.applicationContext
    private val power = app.getSystemService(PowerManager::class.java)
    private val wifi = app.getSystemService(WifiManager::class.java)
    private val audio = app.getSystemService(AudioManager::class.java)
    private val connectivity = app.getSystemService(ConnectivityManager::class.java)
    private val wakeLock = power?.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, LOCK_TAG)?.apply { setReferenceCounted(false) }
    private val wifiLock = wifi?.createWifiLock(wifiLockMode(Build.VERSION.SDK_INT), LOCK_TAG)?.apply { setReferenceCounted(false) }
    private val focus = AudioFocusRequest.Builder(AudioManager.AUDIOFOCUS_GAIN)
        .setAudioAttributes(
            AudioAttributes.Builder()
                .setUsage(AudioAttributes.USAGE_MEDIA)
                .setContentType(AudioAttributes.CONTENT_TYPE_MUSIC)
                .build(),
        )
        .setWillPauseWhenDucked(false)
        .setOnAudioFocusChangeListener { }
        .build()
    private val wifiNetwork = object : ConnectivityManager.NetworkCallback() {
        @Volatile
        private var bound: Network? = null

        override fun onAvailable(network: Network) {
            processNetwork.claim(this@SystemHolds) {
                (connectivity?.bindProcessToNetwork(network) == true).also { if (it) bound = network }
            }
        }

        override fun onLost(network: Network) {
            if (bound == network) unbind()
        }

        fun unbind() {
            bound = null
            processNetwork.release(this@SystemHolds) { connectivity?.bindProcessToNetwork(null) }
        }
    }

    @SuppressLint("WakelockTimeout")
    override fun acquire() {
        guarded("wake lock") { wakeLock?.acquire() }
        guarded("wifi lock") { wifiLock?.acquire() }
        guarded("audio focus") { audio?.requestAudioFocus(focus) }
        guarded("wifi network") {
            val request = NetworkRequest.Builder()
                .addTransportType(NetworkCapabilities.TRANSPORT_WIFI)
                .removeCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
                .build()
            connectivity?.registerNetworkCallback(request, wifiNetwork)
        }
    }

    override fun release() {
        guarded("wifi network") {
            connectivity?.unregisterNetworkCallback(wifiNetwork)
            wifiNetwork.unbind()
        }
        guarded("audio focus") { audio?.abandonAudioFocusRequest(focus) }
        guarded("wifi lock") { if (wifiLock?.isHeld == true) wifiLock.release() }
        guarded("wake lock") { if (wakeLock?.isHeld == true) wakeLock.release() }
    }

    private fun guarded(what: String, action: () -> Unit) {
        try {
            action()
        } catch (failure: RuntimeException) {
            Log.w(TAG, "monitor $what", failure)
        }
    }
}
