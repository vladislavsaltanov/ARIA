package aria.monitor.app

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class PermissionsTest {
    @Test
    fun beforeAndroid13OnlyTheBatteryStepExists() {
        assertEquals(
            listOf(OnboardingStep(OnboardingItem.BatteryExemption, false)),
            onboardingSteps(sdkInt = 32, notificationsGranted = false, batteryExempt = false),
        )
        assertEquals(1, onboardingSteps(sdkInt = 26, notificationsGranted = true, batteryExempt = true).size)
    }

    @Test
    fun fromAndroid13NotificationsComeFirst() {
        assertEquals(
            listOf(
                OnboardingStep(OnboardingItem.Notifications, false),
                OnboardingStep(OnboardingItem.BatteryExemption, true),
            ),
            onboardingSteps(sdkInt = 33, notificationsGranted = false, batteryExempt = true),
        )
        assertEquals(2, onboardingSteps(sdkInt = 36, notificationsGranted = true, batteryExempt = true).size)
    }

    @Test
    fun doneFlagsFollowTheirOwnPermission() {
        val steps = onboardingSteps(sdkInt = 34, notificationsGranted = true, batteryExempt = false)

        assertTrue(steps.single { it.item == OnboardingItem.Notifications }.done)
        assertFalse(steps.single { it.item == OnboardingItem.BatteryExemption }.done)
    }

    @Test
    fun onboardingIsCompleteOnlyWhenEveryStepIsDone() {
        assertFalse(onboardingSteps(34, notificationsGranted = true, batteryExempt = false).allDone())
        assertFalse(onboardingSteps(34, notificationsGranted = false, batteryExempt = true).allDone())
        assertTrue(onboardingSteps(34, notificationsGranted = true, batteryExempt = true).allDone())
    }

    @Test
    fun ungrantedNotificationsDoNotBlockBelowAndroid13() {
        assertTrue(onboardingSteps(32, notificationsGranted = false, batteryExempt = true).allDone())
    }
}
