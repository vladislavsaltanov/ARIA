import { ClockSync } from "./clock.mjs";
import { OutputMap } from "./output.mjs";
import { Player } from "./player.mjs";
import { ClickScheduler } from "./click.mjs";
import { PcmStore, windowsFor } from "./store.mjs";
import { dbToGain, frameAt, SAMPLE_RATE } from "./timeline.mjs";
import { startKeepAwake, unlockAudioSession } from "./keepawake.mjs";

const PAIRING_KEY = "aria-monitor-pairing";
const CONTROL_PAIRING_KEY = "aria-remote-pairing";
const SETTINGS_KEY = "aria-monitor-settings";
const DISCONNECT_GRACE_MS = 10000;
const FAST_PING_MS = 250;
const SLOW_PING_MS = 1000;
const FAST_PING_WINDOW_MS = 5000;
const STATS_MS = 2000;

const element = (id) => document.getElementById(id);
const ui = {
  login: element("login"),
  loginId: element("login-id"),
  loginPassword: element("login-password"),
  loginError: element("login-error"),
  loginGo: element("login-go"),
  kicked: element("kicked"),
  kickedRetry: element("kicked-retry"),
  status: element("status"),
  badgeOutput: element("badge-output"),
  badgeBacking: element("badge-backing"),
  beatPanel: element("beat-panel"),
  barBeat: element("bar-beat"),
  beatDots: element("beat-dots"),
  bpm: element("bpm"),
  meter: element("meter"),
  title: element("title"),
  position: element("position"),
  next: element("next"),
  sound: element("sound"),
  resumeHint: element("resume-hint"),
  backingLevel: element("backing-level"),
  backingValue: element("backing-value"),
  backingToggle: element("backing-toggle"),
  clickLevel: element("click-level"),
  clickValue: element("click-value"),
  clickToggle: element("click-toggle"),
  trimValue: element("trim-value"),
  testToggle: element("test-toggle"),
  deviceName: element("device-name"),
};

const clamp = (value, min, max, fallback) => (Number.isFinite(value) ? Math.min(max, Math.max(min, value)) : fallback);

function loadSettings() {
  const fallback = { name: "", backingDb: 0, backingOn: true, clickDb: 0, clickOn: true, trimMs: 0 };
  try {
    const parsed = JSON.parse(localStorage.getItem(SETTINGS_KEY) || "{}");
    return {
      name: typeof parsed.name === "string" ? parsed.name.slice(0, 64) : "",
      backingDb: clamp(parsed.backingDb, -40, 6, 0),
      backingOn: parsed.backingOn !== false,
      clickDb: clamp(parsed.clickDb, -40, 6, 0),
      clickOn: parsed.clickOn !== false,
      trimMs: clamp(parsed.trimMs, -100, 500, 0),
    };
  } catch {
    return fallback;
  }
}

const settings = loadSettings();
const clock = new ClockSync();
let token = null;
let socket = null;
let connected = false;
let kicked = false;
let reconnectDelay = 500;
let connectedAt = 0;
let pingTimer = null;
let timeline = null;
let lastMessageAt = 0;
let outputOk = true;
let test = false;
let silenced = false;
let audio = null;
let lastBeat = null;

function saveSettings() {
  try {
    localStorage.setItem(SETTINGS_KEY, JSON.stringify(settings));
  } catch {}
}

function levels() {
  return { backingDb: settings.backingDb, backingOn: settings.backingOn, clickDb: settings.clickDb, clickOn: settings.clickOn };
}

function storedPairing() {
  for (const key of [PAIRING_KEY, CONTROL_PAIRING_KEY]) {
    try {
      const value = JSON.parse(localStorage.getItem(key) || "null");
      if (value && value.host === location.origin && value.identifier && value.password) return value;
    } catch {}
  }
  return null;
}

function savePairing(identifier, password) {
  try {
    localStorage.setItem(PAIRING_KEY, JSON.stringify({ host: location.origin, identifier, password }));
  } catch {}
}

function forgetPairing() {
  try {
    localStorage.removeItem(PAIRING_KEY);
  } catch {}
}

async function authenticate(identifier, password) {
  try {
    const response = await fetch("/auth", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ identifier, password }),
    });
    if (!response.ok) return null;
    return (await response.json()).token || null;
  } catch {
    return null;
  }
}

function showLogin(error) {
  ui.login.classList.remove("hidden");
  ui.loginError.classList.toggle("hidden", !error);
}

async function signIn() {
  const pairing = storedPairing();
  if (!pairing) {
    showLogin(false);
    return false;
  }
  token = await authenticate(pairing.identifier, pairing.password);
  if (!token) {
    showLogin(true);
    return false;
  }
  return true;
}

function send(message) {
  if (socket && socket.readyState === WebSocket.OPEN) socket.send(JSON.stringify(message));
}

function schedulePing() {
  clearTimeout(pingTimer);
  if (!connected) return;
  send({ type: "ping", t0: performance.now() });
  pingTimer = setTimeout(schedulePing, performance.now() - connectedAt < FAST_PING_WINDOW_MS ? FAST_PING_MS : SLOW_PING_MS);
}

function connect() {
  if (!token || kicked) return;
  const scheme = location.protocol === "https:" ? "wss://" : "ws://";
  socket = new WebSocket(`${scheme}${location.host}/monitor/ws?token=${encodeURIComponent(token)}`);
  socket.onopen = () => {
    connected = true;
    reconnectDelay = 500;
    connectedAt = performance.now();
    lastMessageAt = connectedAt;
    if (timeline) timeline = { ...timeline, seq: -Infinity };
    send({ type: "hello", name: settings.name, levels: levels() });
    schedulePing();
  };
  socket.onmessage = (event) => {
    lastMessageAt = performance.now();
    let message;
    try {
      message = JSON.parse(event.data);
    } catch {
      return;
    }
    handle(message);
  };
  socket.onclose = () => {
    connected = false;
    clearTimeout(pingTimer);
    if (kicked) return;
    setTimeout(async () => {
      if (await signIn()) connect();
    }, reconnectDelay);
    reconnectDelay = Math.min(reconnectDelay * 2, 5000);
  };
}

function handle(message) {
  switch (message.type) {
    case "pong":
      clock.onPong(message.t0, message.t1, message.t2, performance.now());
      break;
    case "timeline":
      if (timeline && message.seq <= timeline.seq) return;
      timeline = message;
      silenced = false;
      if (audio) {
        audio.player.update(timeline);
        audio.click.update(timeline);
        applyLevels();
      }
      break;
    case "status":
      outputOk = message.output !== false;
      break;
    case "levels":
      settings.backingDb = clamp(message.backingDb, -40, 6, settings.backingDb);
      settings.backingOn = message.backingOn !== false;
      settings.clickDb = clamp(message.clickDb, -40, 6, settings.clickDb);
      settings.clickOn = message.clickOn !== false;
      saveSettings();
      applyLevels();
      send({ type: "levels", ...levels() });
      break;
    case "kicked":
      kicked = true;
      ui.kicked.classList.remove("hidden");
      break;
    case "revoked":
      kicked = true;
      forgetPairing();
      showLogin(false);
      break;
  }
}

async function fetchChunk(track, from, frames) {
  const response = await fetch(`/monitor/pcm?token=${encodeURIComponent(token)}&track=${track}&from=${from}&frames=${frames}`);
  if (!response.ok) return { status: response.status };
  const count = Number(response.headers.get("X-Aria-Frames") || "0");
  return { status: 200, samples: new Int16Array(await response.arrayBuffer()), frames: count };
}

async function startAudio() {
  if (audio) {
    if (audio.ctx.state !== "running") await audio.ctx.resume().catch(() => {});
    ui.resumeHint.classList.add("hidden");
    return;
  }
  const Context = window.AudioContext || window.webkitAudioContext;
  const ctx = new Context({ latencyHint: "interactive" });
  unlockAudioSession();
  await ctx.resume().catch(() => {});
  startKeepAwake();
  const output = new OutputMap(ctx);
  output.trimMs = settings.trimMs;
  const ctxOfHost = (hostUs) => output.ctxTimeAt(clock.perfMsAt(hostUs));
  const hostOfCtx = (ctxTime) => {
    const probe = clock.hostUsAt(performance.now());
    return probe + (ctxTime - ctxOfHost(probe)) * 1e6;
  };
  const store = new PcmStore(fetchChunk, ctx);
  const player = new Player(ctx, store, output, clock, ctx.destination, ctxOfHost, hostOfCtx);
  const click = new ClickScheduler(ctx, output, clock, ctx.destination, ctxOfHost);
  audio = { ctx, output, store, player, click };
  ctx.onstatechange = () => ui.resumeHint.classList.toggle("hidden", ctx.state === "running");
  if (timeline) {
    player.update(timeline);
    click.update(timeline);
  }
  applyLevels();
  setInterval(() => output.sample(), 250);
  setInterval(tickAudio, 50);
  setInterval(() => click.tick(performance.now()), 25);
  ui.sound.textContent = "ЗВУК ВКЛЮЧЁН";
  ui.sound.classList.add("live");
}

function tickAudio() {
  if (!audio || !timeline || !clock.ready) return;
  const now = performance.now();
  if (!connected && now - lastMessageAt > DISCONNECT_GRACE_MS) {
    if (!silenced) {
      silenced = true;
      audio.player.silence(1);
      audio.click.setLevel(0);
    }
    return;
  }
  audio.store.want(windowsFor(timeline, clock.hostUsAt(now)));
  audio.player.tick();
}

function applyLevels() {
  ui.backingLevel.value = settings.backingDb;
  ui.backingValue.textContent = `${settings.backingDb} дБ`;
  ui.backingToggle.textContent = settings.backingOn ? "ВКЛ" : "ВЫКЛ";
  ui.backingToggle.classList.toggle("on", settings.backingOn);
  ui.clickLevel.value = settings.clickDb;
  ui.clickValue.textContent = `${settings.clickDb} дБ`;
  ui.clickToggle.textContent = settings.clickOn ? "ВКЛ" : "ВЫКЛ";
  ui.clickToggle.classList.toggle("on", settings.clickOn);
  ui.trimValue.textContent = `${settings.trimMs} мс`;
  ui.testToggle.classList.toggle("on", test);
  if (!audio || silenced) return;
  audio.output.trimMs = settings.trimMs;
  audio.player.setLevel(!test && settings.backingOn ? dbToGain(settings.backingDb) : 0);
  audio.click.setLevel(settings.clickOn || test ? dbToGain(settings.clickDb) : 0);
  audio.click.setTest(test);
}

function changeLevels(mutate) {
  mutate();
  saveSettings();
  applyLevels();
  send({ type: "levels", ...levels() });
}

function sendStats() {
  if (!connected || !clock.ready) return;
  const now = performance.now();
  send({
    type: "stats",
    offsetUs: Math.round(clock.offsetAt(now) * 1000),
    uncertaintyUs: Number.isFinite(clock.uncertaintyUs) ? clock.uncertaintyUs : 0,
    rttUs: Number.isFinite(clock.minDelay) ? Math.round(clock.minDelay * 1000) : 0,
    outputLatencyMs: audio ? audio.output.latencyMs : 0,
    trimMs: settings.trimMs,
    state: clock.locked ? "locked" : "syncing",
  });
}

function formatTime(seconds) {
  if (!Number.isFinite(seconds) || seconds < 0) return "--:--";
  const whole = Math.floor(seconds);
  return `${Math.floor(whole / 60)}:${String(whole % 60).padStart(2, "0")}`;
}

function renderStatus() {
  if (!connected) {
    ui.status.textContent = "НЕТ СВЯЗИ";
    ui.status.className = "chip alarm";
  } else if (!clock.locked) {
    ui.status.textContent = "СИНХРОНИЗАЦИЯ…";
    ui.status.className = "chip warn";
  } else {
    ui.status.textContent = `СИНХР. ±${(clock.uncertaintyUs / 1000).toFixed(1).replace(".", ",")} МС`;
    ui.status.className = "chip ok";
  }
  ui.badgeOutput.classList.toggle("hidden", outputOk);
  ui.badgeBacking.classList.toggle("hidden", !(timeline && timeline.voices.some((voice) => voice.fallback)));
}

function renderDots(count) {
  if (ui.beatDots.childElementCount === count) return;
  ui.beatDots.replaceChildren(...Array.from({ length: count }, (_, index) => {
    const dot = document.createElement("span");
    dot.className = index === 0 ? "dot accent" : "dot";
    return dot;
  }));
}

function renderBeat(hostNow) {
  const grid = timeline && timeline.grid;
  const voice = grid && timeline.voices.find((candidate) => candidate.id === grid.voice);
  if (!grid || !voice) {
    ui.barBeat.textContent = "—";
    ui.bpm.textContent = "— BPM";
    ui.meter.textContent = "—/—";
    renderDots(0);
    lastBeat = null;
    return;
  }
  ui.bpm.textContent = `${Number(grid.bpm.toFixed(2)).toString().replace(".", ",")} BPM`;
  ui.meter.textContent = `${grid.beatsPerBar}/4`;
  renderDots(grid.beatsPerBar);
  const framesPerBeat = (60 / grid.bpm) * SAMPLE_RATE;
  let frame = frameAt(voice, hostNow, timeline.rate);
  if (voice.endFrame != null) frame = Math.min(frame, voice.endFrame);
  const index = Math.floor((frame - grid.firstBeatFrame) / framesPerBeat);
  const bar = grid.beatsPerBar;
  const beat = ((index % bar) + bar) % bar;
  ui.barBeat.textContent = `${Math.floor(index / bar) + 1}:${beat + 1}`;
  [...ui.beatDots.children].forEach((dot, position) => dot.classList.toggle("lit", position === beat && voice.playing));
  if (voice.playing && index !== lastBeat) {
    lastBeat = index;
    ui.beatPanel.classList.add("flash");
    ui.beatPanel.classList.toggle("accent", beat === 0);
    setTimeout(() => ui.beatPanel.classList.remove("flash", "accent"), 100);
  }
}

function renderNow(hostNow) {
  const deck = timeline && timeline.deck;
  ui.title.textContent = deck && deck.title ? deck.title : "Нет трека в эфире";
  ui.next.textContent = `Далее: ${deck && deck.next ? deck.next.title : "—"}`;
  const main = timeline && (timeline.voices.find((voice) => timeline.grid && voice.id === timeline.grid.voice) || timeline.voices.find((voice) => voice.playing) || timeline.voices[0]);
  ui.position.textContent = main ? formatTime(frameAt(main, hostNow, timeline.rate) / SAMPLE_RATE) : "--:--";
}

function render() {
  renderStatus();
  if (clock.ready && timeline) {
    const hostNow = clock.hostUsAt(performance.now());
    renderBeat(hostNow);
    renderNow(hostNow);
  }
  requestAnimationFrame(render);
}

function bind() {
  ui.sound.addEventListener("click", startAudio);
  ui.backingLevel.addEventListener("input", () => changeLevels(() => (settings.backingDb = Number(ui.backingLevel.value))));
  ui.clickLevel.addEventListener("input", () => changeLevels(() => (settings.clickDb = Number(ui.clickLevel.value))));
  ui.backingToggle.addEventListener("click", (event) => {
    event.preventDefault();
    changeLevels(() => (settings.backingOn = !settings.backingOn));
  });
  ui.clickToggle.addEventListener("click", (event) => {
    event.preventDefault();
    changeLevels(() => (settings.clickOn = !settings.clickOn));
  });
  document.querySelectorAll("[data-trim]").forEach((button) =>
    button.addEventListener("click", () => {
      settings.trimMs = clamp(settings.trimMs + Number(button.dataset.trim), -100, 500, 0);
      saveSettings();
      applyLevels();
    }),
  );
  ui.testToggle.addEventListener("click", () => {
    test = !test;
    applyLevels();
  });
  ui.deviceName.value = settings.name;
  ui.deviceName.addEventListener("change", () => {
    settings.name = ui.deviceName.value.trim().slice(0, 64);
    saveSettings();
    send({ type: "hello", name: settings.name, levels: levels() });
  });
  ui.loginGo.addEventListener("click", async () => {
    const identifier = ui.loginId.value.trim();
    const password = ui.loginPassword.value;
    token = await authenticate(identifier, password);
    if (!token) {
      showLogin(true);
      return;
    }
    savePairing(identifier, password);
    kicked = false;
    ui.login.classList.add("hidden");
    connect();
  });
  ui.kickedRetry.addEventListener("click", async () => {
    kicked = false;
    ui.kicked.classList.add("hidden");
    if (await signIn()) connect();
  });
  document.addEventListener("pointerdown", () => {
    if (audio && audio.ctx.state !== "running") startAudio();
  });
  document.addEventListener("visibilitychange", () => {
    if (document.visibilityState === "visible" && audio) {
      audio.output.reset();
      if (timeline) audio.player.update({ ...timeline, voices: [] });
      if (timeline) {
        audio.player.update(timeline);
        audio.click.update({ ...timeline, grid: null });
        audio.click.update(timeline);
      }
    }
  });
  setInterval(sendStats, STATS_MS);
}

async function boot() {
  bind();
  applyLevels();
  requestAnimationFrame(render);
  const params = new URLSearchParams(location.search);
  const identifier = params.get("id");
  const password = params.get("key");
  if (identifier && password) {
    history.replaceState(null, "", location.pathname);
    token = await authenticate(identifier, password);
    if (token) savePairing(identifier, password);
    else showLogin(true);
  } else {
    await signIn();
  }
  if (token) connect();
}

boot();
