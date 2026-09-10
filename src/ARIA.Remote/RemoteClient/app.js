(function () {
  "use strict";

  var ws = null;
  var seq = 0;
  var clientId = "pult-" + Math.random().toString(36).slice(2, 8);
  var reconnectDelay = 500;
  var pendingAck = new Map();

  var el = {
    conn: document.getElementById("conn"),
    status: document.getElementById("status"),
    title: document.getElementById("title"),
    next: document.getElementById("next"),
    remaining: document.getElementById("remaining"),
    queue: document.getElementById("queue"),
    panicConfirm: document.getElementById("panic-confirm"),
    panic: document.getElementById("btn-panic"),
  };

  function send(type, payload) {
    if (!ws || ws.readyState !== WebSocket.OPEN) return;
    var seqNo = ++seq;
    var frame = { client: clientId, seq: seqNo, command: Object.assign({ type: type }, payload || {}) };
    pendingAck.set(seqNo, Date.now());
    ws.send(JSON.stringify(frame));
  }

  function connect() {
    var url = (location.protocol === "https:" ? "wss://" : "ws://") + location.host + "/ws" + location.search;
    ws = new WebSocket(url);

    ws.onopen = function () {
      reconnectDelay = 500;
      el.conn.textContent = "ONLINE";
      el.conn.className = "conn conn-on";
    };
    ws.onclose = function () {
      el.conn.textContent = "OFFLINE";
      el.conn.className = "conn conn-off";
      setTimeout(connect, reconnectDelay);
      reconnectDelay = Math.min(reconnectDelay * 2, 5000);
    };
    ws.onmessage = onMessage;
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

  requestWakeLock();
  connect();
})();
