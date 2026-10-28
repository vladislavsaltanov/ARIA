package aria.monitor.core

import kotlin.math.roundToInt

data class Settings(val name: String, val levels: Levels, val trimMs: Int) {
    companion object {
        private const val NAME_MAX_LENGTH = 64
        private const val DB_MIN = -40.0
        private const val DB_MAX = 6.0
        private const val TRIM_MIN = -100.0
        private const val TRIM_MAX = 500.0

        fun sanitized(
            name: String? = null,
            backingDb: Double? = null,
            backingOn: Boolean? = null,
            clickDb: Double? = null,
            clickOn: Boolean? = null,
            trimMs: Double? = null,
        ): Settings = Settings(
            name = truncated(name.orEmpty()),
            levels = Levels(
                backingDb = clamp(backingDb, DB_MIN, DB_MAX),
                backingOn = backingOn != false,
                clickDb = clamp(clickDb, DB_MIN, DB_MAX),
                clickOn = clickOn != false,
            ),
            trimMs = clamp(trimMs, TRIM_MIN, TRIM_MAX).roundToInt(),
        )

        private fun clamp(value: Double?, min: Double, max: Double): Double =
            if (value != null && value.isFinite()) value.coerceIn(min, max) else 0.0

        private fun truncated(name: String): String {
            if (name.length <= NAME_MAX_LENGTH) return name
            val head = name.take(NAME_MAX_LENGTH)
            return if (head.last().isHighSurrogate()) head.dropLast(1) else head
        }
    }
}

interface SettingsStore {
    suspend fun load(): Settings
    suspend fun save(settings: Settings)
    suspend fun loadPairing(): Pairing?
    suspend fun savePairing(pairing: Pairing?)
}
