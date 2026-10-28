package aria.monitor.core

import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlin.coroutines.ContinuationInterceptor
import kotlin.math.roundToLong

private const val DISCONNECT_GRACE_MS = 10_000.0
private const val FAST_PING_WINDOW_MS = 5000.0
private const val FAST_PING_MS = 250L
private const val SLOW_PING_MS = 1000L
private const val STATS_MS = 2000L
private const val STALE_CHECK_MS = 500L
private const val AUDIO_TICK_MS = 50L
private const val STAMP_TICK_MS = 250L
private const val SILENCE_RAMP_SECONDS = 1.0f
private const val MICROS_PER_MILLI = 1000.0

class Session(
    scope: CoroutineScope,
    private val clock: MonotonicClock,
    private val authenticator: Authenticator,
    private val transport: MonitorTransport,
    pcm: PcmSource,
    private val engine: AudioEngine,
    private val store: SettingsStore,
) {
    private val control = CoroutineScope(scope.coroutineContext + SupervisorJob(scope.coroutineContext[Job]) + serial(scope))
    private val loader = ChunkLoader(control, pcm, engine, { clock.nowMs().toLong() }, onUnauthorized = { onControl { pcmUnauthorized() } })
    private val estimator = TimeMapEstimator()
    private val mutableState = MutableStateFlow(idle(Settings.sanitized(), needsPairing = false))
    val state: StateFlow<SessionState> = mutableState.asStateFlow()

    @Volatile
    private var clockSync = ClockSync()
    private var run: CoroutineScope? = null
    private var engineStarted = false
    private var pairing: Pairing? = null
    private var socket: MonitorSocket? = null
    private var socketId = 0L
    private var connected = false
    private var connectedAtMs = 0.0
    private var lastMessageAtMs = 0.0
    private var reconnectDelayMs = LinkPolicy.RECONNECT_MIN_MS
    private var authJob: Job? = null
    private var connectionJob: Job? = null
    private var lastSeq: Long? = null
    private var silenced = false
    private var generation = 0L
    private var xruns = 0

    init {
        engine.setEventListener { event -> onControl { onEngineEvent(event) } }
    }

    fun start(pairing: Pairing?) {
        onControl {
            halt()
            val runScope = CoroutineScope(control.coroutineContext + SupervisorJob(control.coroutineContext[Job]))
            run = runScope
            runScope.launch { begin(pairing) }
        }
    }

    fun stop(): Job = control.launch { halt() }

    fun setLevels(levels: Levels) {
        onControl {
            val settings = adopt(levels = levels)
            send(ClientMessage.LevelsChanged(settings.levels))
            store.save(settings)
        }
    }

    fun setTrim(ms: Int) {
        onControl { store.save(adopt(trimMs = ms)) }
    }

    fun setTestClick(on: Boolean) {
        onControl {
            mutableState.update { it.copy(testClick = on) }
            publish()
        }
    }

    fun setName(name: String) {
        onControl {
            val settings = adopt(name = name.trim())
            send(ClientMessage.Hello(settings.name, settings.levels))
            store.save(settings)
        }
    }

    fun retryAfterKick() {
        onControl {
            if (state.value.kicked) {
                mutableState.update { it.copy(kicked = false) }
                authenticate(afterBackoff = false, ::connect)
            }
        }
    }

    fun hostNowUs(): Double? {
        val sync = clockSync
        return if (sync.ready) sync.hostUsAt(clock.nowMs()) else null
    }

    private fun onControl(action: suspend () -> Unit) {
        control.launch { action() }
    }

    private suspend fun CoroutineScope.begin(given: Pairing?) {
        clockSync = ClockSync()
        estimator.reset()
        lastSeq = null
        silenced = false
        reconnectDelayMs = LinkPolicy.RECONNECT_MIN_MS
        val settings = store.load()
        val target = given ?: store.loadPairing()
        pairing = target
        mutableState.value = idle(settings, needsPairing = target == null)
        if (target == null) return
        engine.start()
        engineStarted = true
        xruns = engine.xrunCount
        every(AUDIO_TICK_MS, ::tickAudio)
        every(STAMP_TICK_MS, ::tickStamp)
        authenticate(afterBackoff = false, ::connect)
    }

    private fun halt() {
        run?.cancel()
        run = null
        dropSocket()
        if (engineStarted) engine.stop()
        engineStarted = false
        loader.want(emptyList())
        mutableState.update { it.copy(link = LinkState.Offline) }
    }

    private fun authenticate(afterBackoff: Boolean, onToken: (Pairing, String) -> Unit) {
        authJob?.cancel()
        authJob = run?.launch {
            if (afterBackoff) backoff()
            while (!signedIn(onToken)) backoff()
        }
    }

    private suspend fun signedIn(onToken: (Pairing, String) -> Unit): Boolean {
        val target = pairing
        if (target == null || state.value.kicked) return true
        return when (val outcome = signIn(target, authenticator)) {
            is SignInOutcome.Ok -> {
                onToken(target, outcome.token)
                true
            }
            SignInOutcome.Retry -> false
            SignInOutcome.NeedLogin, SignInOutcome.DeniedLogin -> {
                mutableState.update { it.copy(needsPairing = true) }
                true
            }
        }
    }

    private suspend fun backoff() {
        val waitMs = reconnectDelayMs
        reconnectDelayMs = LinkPolicy.nextDelay(waitMs)
        delay(waitMs)
    }

    private fun connect(target: Pairing, token: String) {
        dropSocket()
        loader.setToken(token)
        mutableState.update { it.copy(link = LinkState.Connecting) }
        socket = transport.open(target, token, Link(socketId))
    }

    private fun pcmUnauthorized() {
        if (connected && authJob?.isActive != true) authenticate(afterBackoff = true) { _, token -> loader.setToken(token) }
    }

    private fun opened() {
        connected = true
        connectedAtMs = clock.nowMs()
        lastMessageAtMs = connectedAtMs
        lastSeq = null
        mutableState.update { it.copy(link = linkState()) }
        send(ClientMessage.Hello(state.value.name, state.value.levels))
        connectionJob = run?.launch {
            launch { ping() }
            every(STATS_MS, ::sendStats)
            every(STALE_CHECK_MS, ::dropIfStale)
        }
    }

    private suspend fun ping() {
        while (true) {
            send(ClientMessage.Ping(clock.nowMs()))
            delay(if (clock.nowMs() - connectedAtMs < FAST_PING_WINDOW_MS) FAST_PING_MS else SLOW_PING_MS)
        }
    }

    private fun dropIfStale() {
        if (LinkPolicy.isStale(connected, clock.nowMs(), lastMessageAtMs)) connectionLost()
    }

    private fun connectionLost() {
        dropSocket()
        mutableState.update { it.copy(link = LinkState.Offline) }
        authenticate(afterBackoff = true, ::connect)
    }

    private fun dropSocket() {
        socketId++
        connectionJob?.cancel()
        connectionJob = null
        connected = false
        socket?.close()
        socket = null
    }

    private suspend fun received(text: String, atMs: Double) {
        lastMessageAtMs = atMs
        when (val message = ProtocolCodec.decode(text)) {
            null -> Unit
            is ServerMessage.Welcome -> reconnectDelayMs = LinkPolicy.RECONNECT_MIN_MS
            is ServerMessage.Pong -> synced(message, atMs)
            is ServerMessage.Timeline -> accept(message)
            is ServerMessage.Status -> mutableState.update { it.copy(outputOk = message.output) }
            is ServerMessage.LevelsUpdate -> {
                val settings = adopt(levels = message.levels)
                send(ClientMessage.LevelsChanged(settings.levels))
                store.save(settings)
            }
            ServerMessage.Kicked -> mutableState.update { it.copy(kicked = true) }
            ServerMessage.Revoked -> {
                pairing = null
                mutableState.update { it.copy(needsPairing = true) }
                store.savePairing(null)
            }
        }
    }

    private fun synced(pong: ServerMessage.Pong, atMs: Double) {
        val sync = clockSync
        sync.onPong(pong.t0, pong.t1Us, pong.t2Us, atMs)
        mutableState.update { it.copy(link = linkState(), uncertaintyUs = sync.uncertaintyUs) }
    }

    private fun linkState(): LinkState = if (clockSync.locked) LinkState.Locked else LinkState.Syncing

    private fun accept(timeline: ServerMessage.Timeline) {
        val last = lastSeq
        if (last != null && timeline.seq <= last) return
        lastSeq = timeline.seq
        silenced = false
        mutableState.update { it.copy(timeline = timeline) }
        publish()
    }

    private fun adopt(
        name: String = state.value.name,
        levels: Levels = state.value.levels,
        trimMs: Int = state.value.trimMs,
    ): Settings {
        val settings = Settings.sanitized(name, levels.backingDb, levels.backingOn, levels.clickDb, levels.clickOn, trimMs.toDouble())
        mutableState.update { it.copy(name = settings.name, levels = settings.levels, trimMs = settings.trimMs) }
        publish()
        return settings
    }

    private fun send(message: ClientMessage) {
        if (connected) socket?.send(ProtocolCodec.encode(message))
    }

    private fun sendStats() {
        val sync = clockSync
        if (!connected || !sync.ready) return
        send(
            ClientMessage.Stats(
                offsetUs = (sync.offsetAt(clock.nowMs()) * MICROS_PER_MILLI).roundToLong(),
                uncertaintyUs = sync.uncertaintyUs ?: 0L,
                rttUs = sync.minDelayMs?.let { (it * MICROS_PER_MILLI).roundToLong() } ?: 0L,
                outputLatencyMs = 0.0,
                trimMs = state.value.trimMs.toDouble(),
                state = if (sync.locked) "locked" else "syncing",
            ),
        )
    }

    private fun tickAudio() {
        val timeline = state.value.timeline ?: return
        val sync = clockSync
        if (!sync.ready) return
        val now = clock.nowMs()
        if (!connected && now - lastMessageAtMs > DISCONNECT_GRACE_MS) {
            if (!silenced) {
                silenced = true
                publish()
            }
            return
        }
        loader.want(windowsFor(timeline, sync.hostUsAt(now)))
    }

    private fun tickStamp() {
        val count = engine.xrunCount
        if (count > xruns) engine.growBuffer()
        xruns = count
        val stamp = engine.sampleStamp() ?: return
        estimator.add(stamp)
        publish()
    }

    private fun onEngineEvent(event: EngineEvent) {
        when (event) {
            EngineEvent.StreamReopened -> estimator.reset()
        }
    }

    private fun publish() {
        val current = state.value
        val timeline = current.timeline ?: return
        if (!engineStarted) return
        val timeMap = estimator.timeMap(clockSync) ?: return
        val plan = PlanBuilder.build(
            timeline = timeline,
            levels = current.levels,
            trimMs = current.trimMs,
            testClick = current.testClick,
            timeMap = timeMap,
            generation = ++generation,
        )
        engine.publish(if (silenced) plan.copy(masterGain = 0f, masterRampSeconds = SILENCE_RAMP_SECONDS) else plan)
    }

    private inner class Link(private val id: Long) : SocketListener {
        override fun onOpen() = whileCurrent { opened() }

        override fun onText(text: String) {
            val receivedAtMs = clock.nowMs()
            whileCurrent { received(text, receivedAtMs) }
        }

        override fun onClosed(unauthorized: Boolean) = whileCurrent { connectionLost() }

        private fun whileCurrent(action: suspend () -> Unit) = onControl { if (id == socketId) action() }
    }
}

private fun CoroutineScope.every(periodMs: Long, action: () -> Unit) {
    launch {
        while (true) {
            delay(periodMs)
            action()
        }
    }
}

private fun serial(scope: CoroutineScope): CoroutineDispatcher =
    (scope.coroutineContext[ContinuationInterceptor] as? CoroutineDispatcher ?: Dispatchers.Default).limitedParallelism(1)

private fun idle(settings: Settings, needsPairing: Boolean): SessionState =
    SessionState(
        link = LinkState.Offline,
        uncertaintyUs = null,
        timeline = null,
        levels = settings.levels,
        trimMs = settings.trimMs,
        outputOk = true,
        testClick = false,
        kicked = false,
        needsPairing = needsPairing,
        name = settings.name,
    )
