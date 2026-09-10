(function () {
  "use strict";

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
    }).then(function (response) {
      if (!response.ok) return null;
      return response.json().then(function (body) { return body.token; });
    });
  }

  function ensureToken() {
    if (sessionToken) return Promise.resolve(sessionToken);
    if (!creds) return Promise.resolve(null);
    return authRequest(creds.identifier, creds.password).then(function (token) {
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
    ensureToken().then(function (token) {
      if (!token) {
        showLogin("");
        return;
      }
      var url = (location.protocol === "https:" ? "wss://" : "ws://") + location.host + "/ws?token=" + encodeURIComponent(token);
      ws = new WebSocket(url);
      ws.onopen = function () {
        reconnectDelay = 500;
        el.conn.textContent = "ONLINE";
        el.conn.className = "conn conn-on";
      };
      ws.onclose = function () {
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
      case "ack": pendingAck.delete(frame.seq); break;
 default: break;
    }
  }

  var state = { transport: null, queue: [] };

  function applySnapshot(frame) {
    state.transport = frame.transport.state;
    renderTransport();
  }

  function applyDelta(frame) {
    if (frame.partition === "transport") {
      state.transport = frame.state;
      renderTransport();
    } else if (frame.partition === "queue") {
      state.queue = frame.state.items || [];
      renderQueue();
    }
  }

  function applyPosition(frame) {
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
    state.queue.forEach(function (item) {
      var li = document.createElement("li");
      if (item.color) li.style.borderLeft = "4px solid " + item.color;
      li.textContent = item.displayName;
      el.queue.appendChild(li);
    });
  }

  function showConfirm() { el.panicConfirm.classList.remove("hidden"); }
  function hideConfirm() { el.panicConfirm.classList.add("hidden"); }

  el.panic.addEventListener("click", showConfirm);
  document.getElementById("panic-yes").addEventListener("click", function () {
    hideConfirm();
    send("panic");
    vibrate();
  });
  document.getElementById("panic-no").addEventListener("click", hideConfirm);

  function vibrate() {
    if (navigator.vibrate) navigator.vibrate(80);
  }

  var actions = {
    "btn-play": "play",
    "btn-pause": "pause",
    "btn-stop": "stop",
    "btn-next": "next",
    "btn-replay": "replay",
  };

  Object.keys(actions).forEach(function (id) {
    var button = document.getElementById(id);
    if (!button) return;
    button.addEventListener("click", function () {
      send(actions[id]);
      vibrate();
    });
  });

  var wakeLock = null;
  async function requestWakeLock() {
    try {
      if ("wakeLock" in navigator) {
        wakeLock = await navigator.wakeLock.request("screen");
        wakeLock.addEventListener("release", function () {
          setTimeout(requestWakeLock, 1000);
        });
      }
    } catch (e) { /* denied - fine */ }
  }

  document.addEventListener("visibilitychange", function () {
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

  el.loginGo.addEventListener("click", function () {
    var identifier = el.loginId.value.trim();
    var password = el.loginPassword.value;
    if (!identifier || !password) {
      showLogin("заполните оба поля");
      return;
    }
    authRequest(identifier, password).then(function (token) {
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
      authRequest(urlId, urlKey).then(function (token) {
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
