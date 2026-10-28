package aria.monitor.app

import android.Manifest
import android.content.ActivityNotFoundException
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.PowerManager
import android.provider.Settings

private const val NOTIFICATION_PERMISSION_SDK = 33

enum class OnboardingItem { Notifications, BatteryExemption }

data class OnboardingStep(val item: OnboardingItem, val done: Boolean)

fun onboardingSteps(sdkInt: Int, notificationsGranted: Boolean, batteryExempt: Boolean): List<OnboardingStep> =
    buildList {
        if (sdkInt >= NOTIFICATION_PERMISSION_SDK) add(OnboardingStep(OnboardingItem.Notifications, notificationsGranted))
        add(OnboardingStep(OnboardingItem.BatteryExemption, batteryExempt))
    }

fun List<OnboardingStep>.allDone(): Boolean = all { it.done }

internal fun Context.cameraGranted(): Boolean = granted(Manifest.permission.CAMERA)

internal fun Context.notificationsGranted(): Boolean =
    Build.VERSION.SDK_INT < NOTIFICATION_PERMISSION_SDK || granted(Manifest.permission.POST_NOTIFICATIONS)

internal fun Context.batteryExempt(): Boolean =
    getSystemService(PowerManager::class.java).isIgnoringBatteryOptimizations(packageName)

internal fun Context.currentOnboardingSteps(): List<OnboardingStep> =
    onboardingSteps(Build.VERSION.SDK_INT, notificationsGranted(), batteryExempt())

internal fun Context.requestBatteryExemption(): Boolean =
    launchSafely(Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS, Uri.fromParts("package", packageName, null))) ||
        launchSafely(Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS))

internal fun Context.openAppSettings(): Boolean =
    launchSafely(Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.fromParts("package", packageName, null)))

private fun Context.granted(permission: String): Boolean =
    checkSelfPermission(permission) == PackageManager.PERMISSION_GRANTED

private fun Context.launchSafely(intent: Intent): Boolean =
    try {
        startActivity(intent)
        true
    } catch (missing: ActivityNotFoundException) {
        false
    }
