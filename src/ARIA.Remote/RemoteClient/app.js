(() => {
  

  var ws = null;
  var seq = 0;
  var clientId = "pult-" + Math.random().toString(36).slice(2, 8);
  var reconnectDelay = 500;
  var pendingAck = new Map();
  var sessionToken = null;
  var creds = null;

  var STORAGE_KEY = "aria-remote-pairing";

  var el = {
    conn: document.getElementById("conn"),
    status: document.getElementById("status"),
    title: document.getElementById("title"),
    next: document.getElementById("next"),
    remaining: document.getElementById("remaining"),
    queue: document.getElementById("queue"),
    panicConfirm: document.getElementById("panic-confirm"),
    panic: document.getElementById("btn-panic"),
    login: document.getElementById("login"),
    loginId: document.getElementById("login-id"),
    loginPassword: document.getElementById("login-password"),
    loginError: document.getElementById("login-error"),
    loginGo: document.getElementById("login-go"),
    forget: document.getElementById("btn-forget"),
    showClock: document.getElementById("show-clock"),
    trackElapsed: document.getElementById("track-elapsed"),
    master: document.getElementById("master"),
    mute: document.getElementById("btn-mute"),
    lufsValue: document.getElementById("lufs-value"),
    lufsFill: document.getElementById("lufs-fill"),
  };

  function savedPairing() {
    try {
      var raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return null;
      var value = JSON.parse(raw);
      if (value && value.host === location.origin && value.identifier && value.password) return value;
    } catch (e) { }
    return null;
  }

  function savePairing(identifier, password) {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify({ host: location.origin, identifier: identifier, password: password }));
    } catch (e) { }
  }

  function forgetPairing() {
    try {
      localStorage.removeItem(STORAGE_KEY);
    } catch (e) { }
    closeSocket();
    sessionToken = null;
    creds = null;
    showLogin("");
  }

  function showLogin(error) {
    el.login.classList.remove("hidden");
    document.getElementById("app").style.display = "none";
    el.loginError.classList.toggle("hidden", !error);
    el.loginError.textContent = error || "неверный идентификатор или пароль";
    el.loginId.value = creds ? creds.identifier : el.loginId.value;
  }

  function hideLogin() {
    el.login.classList.add("hidden");
    document.getElementById("app").style.display = "";
  }

  function authRequest(identifier, password) {
    return fetch("/auth", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ identifier: identifier, password: password }),
    }).then((response) => {
      if (!response.ok) return null;
      return response.json().then((body) => body.token);
    });
  }

  function ensureToken() {
    if (sessionToken) return Promise.resolve(sessionToken);
    if (!creds) return Promise.resolve(null);
    return authRequest(creds.identifier, creds.password).then((token) => {
      sessionToken = token;
      return token;
    });
  }

  function send(type, payload) {
    if (!ws || ws.readyState !== WebSocket.OPEN) return;
    var seqNo = ++seq;
    var frame = { client: clientId, seq: seqNo, command: Object.assign({ type: type }, payload || {}) };
    pendingAck.set(seqNo, Date.now());
    ws.send(JSON.stringify(frame));
  }

  function connect() {
    ensureToken().then((token) => {
      if (!token) {
        showLogin("");
        return;
      }
      var url = (location.protocol === "https:" ? "wss://" : "ws://") + location.host + "/ws?token=" + encodeURIComponent(token);
      ws = new WebSocket(url);
      ws.onopen = () => {
        reconnectDelay = 500;
        el.conn.textContent = "ONLINE";
        el.conn.className = "conn conn-on";
      };
      ws.onclose = () => {
        el.conn.textContent = "OFFLINE";
        el.conn.className = "conn conn-off";
        sessionToken = null;
        setTimeout(connect, reconnectDelay);
        reconnectDelay = Math.min(reconnectDelay * 2, 5000);
      };
      ws.onmessage = onMessage;
    });
  }

  function onMessage(event) {
    var frame;
    try {
      frame = JSON.parse(event.data);
    } catch (e) {
      return;
    }
    switch (frame.event) {
      case "snapshot": applySnapshot(frame); break;
      case "delta": applyDelta(frame); break;
      case "position": applyPosition(frame); break;
      case "lufs": applyLufs(frame); break;
      case "ack": pendingAck.delete(frame.seq); break;
      case "rejected": pendingAck.delete(frame.seq); break;
 default: break;
    }
  }

  var state = { transport: null, show: null, mixer: null, queue: [], lufs: null };

  var LUFS_FLOOR = -60;
  var LUFS_WARN = -18;

  function applySnapshot(frame) {
    state.show = frame.show ? frame.show.state : null;
    state.mixer = frame.mixer ? frame.mixer.state : null;
    state.transport = frame.transport.state;
    renderTransport();
    renderMixer();
    renderShowClock();
  }

  function applyDelta(frame) {
    if (frame.partition === "transport") {
      state.transport = frame.state;
      renderTransport();
    } else if (frame.partition === "queue") {
      state.queue = frame.state.items || [];
      renderQueue();
    } else if (frame.partition === "show") {
      state.show = frame.state;
      renderShowClock();
    } else if (frame.partition === "mixer") {
      state.mixer = frame.state;
      renderMixer();
    }
  }

  function applyPosition(frame) {
    var fileMs = frame.filePositionMs;
    el.trackElapsed.textContent = fileMs == null ? "0:00" : formatSeconds(Math.floor(fileMs / 1000));
    var ms = frame.remainingMs;
    if (ms == null) {
      el.remaining.textContent = "--:--";
      el.remaining.classList.remove("low");
      return;
    }
    var total = Math.ceil(ms / 1000);
    if (total < 0) total = 0;
    var h = Math.floor(total / 3600);
    var m = Math.floor((total % 3600) / 60);
    var s = total % 60;
    var text = h > 0
      ? h + ":" + pad(m) + ":" + pad(total % 60)
      : pad(Math.floor(total / 60)) + ":" + pad(total % 60);
    el.remaining.textContent = text;
    el.remaining.classList.toggle("low", total <= 10 && total > 0);
  }

  function pad(n) { return n < 10 ? "0" + n : "" + n; }

  function renderTransport() {
    var t = state.transport;
    if (!t) return;
    el.status.textContent = (t.status || "STOP").toUpperCase();
    el.title.textContent = t.current ? t.current.displayName : "—";
    el.next.textContent = "далее: " + (t.next ? t.next.displayName : "—");
    el.panic.disabled = panicked(t.status);
  }

  function panicked(status) {
    return status === "Panicked";
  }

  function parseIsoDuration(text) {
    if (typeof text !== "string") return null;
    var parts = text.split(":");
    if (parts.length < 3) return null;
    var hours = parseInt(parts[0], 10);
    var minutes = parseInt(parts[1], 10);
    var seconds = parseFloat(parts[2]);
    if (isNaN(hours) || isNaN(minutes) || isNaN(seconds)) return null;
    return hours * 3600 + minutes * 60 + Math.floor(seconds);
  }

  function formatSeconds(total) {
    if (total == null || isNaN(total)) return "--:--";
    if (total < 0) total = 0;
    var h = Math.floor(total / 3600);
    var m = Math.floor((total % 3600) / 60);
    var s = total % 60;
    return h > 0 ? h + ":" + pad(m) + ":" + pad(s) : m + ":" + pad(s);
  }

  function renderShowClock() {
    var clock = state.show && state.show.clock;
    var total = clock ? parseIsoDuration(clock.elapsed) : null;
    el.showClock.textContent = total == null ? "--:--" : formatSeconds(total);
  }

  function gainToPct(db) {
    var pct = (db + 80) / 92 * 100;
    return Math.max(0, Math.min(100, pct));
  }

  function pctToGain(pct) {
    return Math.round((-80 + pct * 0.92) * 10) / 10;
  }

  function renderMixer() {
    var m = state.mixer;
    if (!m) return;
    el.master.value = Math.round(gainToPct(m.masterGainDb));
    el.mute.classList.toggle("active", !!m.muted);
  }

  function applyLufs(frame) {
    state.lufs = typeof frame.momentaryLufs === "number" ? frame.momentaryLufs : null;
    renderLufs();
  }

  function renderLufs() {
    var v = state.lufs;
    if (v == null) {
      el.lufsValue.textContent = "—";
      el.lufsValue.classList.remove("hot");
      el.lufsFill.style.width = "0%";
      el.lufsFill.classList.remove("hot");
      return;
    }
    var clamped = Math.min(v, 0);
    var hot = clamped > LUFS_WARN;
    el.lufsValue.textContent = clamped.toFixed(1);
    el.lufsValue.classList.toggle("hot", hot);
    el.lufsFill.classList.toggle("hot", hot);
    var width = Math.max(0, Math.min(100, (clamped - LUFS_FLOOR) / -LUFS_FLOOR * 100));
    el.lufsFill.style.width = width + "%";
  }

  function renderQueue() {
    el.queue.textContent = "";
    if (!state.queue.length) {
      var li = document.createElement("li");
      li.className = "empty";
      li.textContent = "пусто";
      li.style.color = "var(--dim)";
      el.queue.appendChild(li);
      return;
    }
    state.queue.forEach((item) => {
      var li = document.createElement("li");
      if (item.color) li.style.borderLeft = "4px solid " + item.color;
      li.textContent = item.displayName;
      el.queue.appendChild(li);
    });
  }

  function showConfirm() { el.panicConfirm.classList.remove("hidden"); }
  function hideConfirm() { el.panicConfirm.classList.add("hidden"); }

  el.panic.addEventListener("click", showConfirm);
  document.getElementById("panic-yes").addEventListener("click", () => {
    hideConfirm();
    send("panic");
    vibrate();
  });
  document.getElementById("panic-no").addEventListener("click", hideConfirm);

  function vibrate() {
    if (navigator.vibrate) navigator.vibrate(80);
  }

  el.master.addEventListener("input", () => {
    if (state.mixer && state.mixer.muted) send("set_muted", { muted: false });
    send("set_master_gain", { gain_db: pctToGain(Number(el.master.value)) });
  });

  el.mute.addEventListener("click", () => {
    if (!state.mixer) return;
    send("set_muted", { muted: !state.mixer.muted });
    vibrate();
  });

  var actions = {
    "btn-play": "play",
    "btn-pause": "pause",
    "btn-stop": "stop",
    "btn-next": "next",
    "btn-replay": "replay",
  };

  Object.keys(actions).forEach((id) => {
    var button = document.getElementById(id);
    if (!button) return;
    button.addEventListener("click", () => {
      send(actions[id]);
      vibrate();
    });
  });

  var wakeLock = null;
  async function requestWakeLock() {
    try {
      if ("wakeLock" in navigator) {
        wakeLock = await navigator.wakeLock.request("screen");
        wakeLock.addEventListener("release", () => {
          setTimeout(requestWakeLock, 1000);
        });
      }
    } catch (e) { /* denied - fine */ }
  }

  document.addEventListener("visibilitychange", () => {
    if (document.visibilityState === "visible") requestWakeLock();
  });

  function closeSocket() {
    if (!ws) return;
    try {
      ws.onclose = null;
      ws.close();
    } catch (e) { }
    ws = null;
  }

  function startWithSaved() {
    creds = savedPairing();
    if (creds) {
      hideLogin();
      connect();
      return;
    }
    showLogin("");
  }

  el.loginGo.addEventListener("click", () => {
    var identifier = el.loginId.value.trim();
    var password = el.loginPassword.value;
    if (!identifier || !password) {
      showLogin("заполните оба поля");
      return;
    }
    authRequest(identifier, password).then((token) => {
      if (!token) {
        showLogin("неверный идентификатор или пароль");
        return;
      }
      sessionToken = token;
      creds = { identifier: identifier, password: password };
      savePairing(identifier, password);
      hideLogin();
      closeSocket();
      connect();
    });
  });

  el.forget.addEventListener("click", forgetPairing);

  (function boot() {
    var params = new URLSearchParams(location.search);
    var urlId = params.get("id");
    var urlKey = params.get("key");
    if (urlId && urlKey) {
      history.replaceState(null, "", location.pathname);
      authRequest(urlId, urlKey).then((token) => {
        if (token) {
          sessionToken = token;
          creds = { identifier: urlId, password: urlKey };
          savePairing(urlId, urlKey);
          hideLogin();
        }
        connect();
      });
      return;
    }
    startWithSaved();
  })();

  requestWakeLock();
})();
