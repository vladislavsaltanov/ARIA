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
    app: document.getElementById("app"),
    conn: document.getElementById("conn"),
    status: document.getElementById("status"),
    title: document.getElementById("title"),
    next: document.getElementById("next"),
    elapsed: document.getElementById("elapsed"),
    trackRemain: document.getElementById("track-remain"),
    seek: document.getElementById("seek"),
    seekFill: document.getElementById("seek-fill"),
    queue: document.getElementById("queue"),
    panicConfirm: document.getElementById("panic-confirm"),
    queueClear: document.getElementById("btn-queue-clear"),
    clearConfirm: document.getElementById("clear-confirm"),
    panic: document.getElementById("btn-panic"),
    login: document.getElementById("login"),
    loginId: document.getElementById("login-id"),
    loginPassword: document.getElementById("login-password"),
    loginError: document.getElementById("login-error"),
    loginGo: document.getElementById("login-go"),
    forget: document.getElementById("btn-forget"),
    showClock: document.getElementById("show-clock"),
    master: document.getElementById("master"),
    mute: document.getElementById("btn-mute"),
    lufsValue: document.getElementById("lufs-value"),
    lufsFill: document.getElementById("lufs-fill"),
    previewPlay: document.getElementById("btn-preview-play"),
    previewStop: document.getElementById("btn-preview-stop"),
    previewGain: document.getElementById("preview-gain"),
    previewMute: document.getElementById("btn-preview-mute"),
    previewAudio: document.getElementById("preview-audio"),
    projects: document.getElementById("projects"),
    scriptSection: document.getElementById("script-section"),
    scriptTabs: document.getElementById("script-tabs"),
    scriptLines: document.getElementById("script-lines"),
    mentionMenu: document.getElementById("mention-menu"),
    mentionOptions: document.getElementById("mention-options"),
    lock: document.getElementById("lock"),
    toast: document.getElementById("toast"),
    clearYes: document.getElementById("clear-yes"),
    clearNo: document.getElementById("clear-no"),
    panicYes: document.getElementById("panic-yes"),
    panicNo: document.getElementById("panic-no"),
    mentionCancel: document.getElementById("mention-cancel"),
    defaultAction: document.getElementById("default-action"),
  };

  function savedPairing() {
    try {
      var raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return null;
      var value = JSON.parse(raw);
      if (
        value &&
        value.host === location.origin &&
        value.identifier &&
        value.password
      )
        return value;
    } catch {}
    return null;
  }

  function savePairing(identifier, password) {
    try {
      localStorage.setItem(
        STORAGE_KEY,
        JSON.stringify({
          host: location.origin,
          identifier: identifier,
          password: password,
        }),
      );
    } catch {}
  }

  function forgetPairing() {
    try {
      localStorage.removeItem(STORAGE_KEY);
    } catch {}
    closeSocket();
    sessionToken = null;
    creds = null;
    showLogin("");
  }

  function showLogin(error) {
    el.login.classList.remove("hidden");
    el.app.style.display = "none";
    el.loginError.classList.toggle("hidden", !error);
    el.loginError.textContent = error || "неверный идентификатор или пароль";
    el.loginId.value = creds ? creds.identifier : el.loginId.value;
  }

  function hideLogin() {
    el.login.classList.add("hidden");
    el.app.style.display = "";
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
    var frame = {
      client: clientId,
      seq: seqNo,
      command: Object.assign({ type: type }, payload || {}),
    };
    pendingAck.set(seqNo, Date.now());
    ws.send(JSON.stringify(frame));
  }

  function connect() {
    ensureToken().then((token) => {
      if (!token) {
        showLogin("");
        return;
      }
      var url =
        (location.protocol === "https:" ? "wss://" : "ws://") +
        location.host +
        "/ws?token=" +
        encodeURIComponent(token);
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
    } catch {
      return;
    }
    switch (frame.event) {
      case "snapshot":
        applySnapshot(frame);
        break;
      case "delta":
        applyDelta(frame);
        break;
      case "position":
        applyPosition(frame);
        break;
      case "lufs":
        applyLufs(frame);
        break;
      case "ack":
        pendingAck.delete(frame.seq);
        break;
      case "rejected":
        pendingAck.delete(frame.seq);
        rejectFeedback();
        break;
      default:
        break;
    }
  }

  var state = {
    transport: null,
    show: null,
    mixer: null,
    queue: [],
    lufs: null,
    lastFileMs: null,
  };

  var LUFS_FLOOR = -60;
  var LUFS_WARN = -18;

  var toastTimer = null;

  function rejectFeedback() {
    if (navigator.vibrate) navigator.vibrate(40);
    el.toast.textContent = "команда отклонена";
    el.toast.classList.remove("hidden");
    if (toastTimer) clearTimeout(toastTimer);
    toastTimer = setTimeout(() => {
      el.toast.classList.add("hidden");
      toastTimer = null;
    }, 2000);
  }

  function renderLock() {
    el.lock.classList.toggle("hidden", !(state.show && state.show.locked));
  }

  function renderDefaultAction() {
    if (!el.defaultAction) return;
    var value = state.show ? state.show.defaultEndAction : null;
    if (value) el.defaultAction.value = value;
  }

  function applySnapshot(frame) {
    state.show = frame.show ? frame.show.state : null;
    state.mixer = frame.mixer ? frame.mixer.state : null;
    state.transport = frame.transport.state;
    state.queue =
      frame.queue && frame.queue.state ? frame.queue.state.items || [] : [];
    renderTransport();
    renderMixer();
    renderQueue();
    renderShowClock();
    renderProjects();
    renderScript();
    renderLock();
    renderDefaultAction();
  }

  function applyDelta(frame) {
    if (frame.partition === "transport") {
      state.transport = frame.state;
      renderTransport();
    } else if (frame.partition === "queue") {
      state.queue = frame.state.items || [];
      renderQueue();
    } else if (frame.partition === "show") {
      var prev = state.show;
      state.show = frame.state;
      renderShowClock();
      renderProjects();
      renderLock();
      renderDefaultAction();
      if (scriptContentChanged(prev, frame.state)) {
        renderScript();
      } else {
        updateScriptFollow();
      }
    } else if (frame.partition === "mixer") {
      state.mixer = frame.state;
      renderMixer();
    }
  }

  var trackWindowMs = null;

  function marquee(node, text) {
    node.textContent = text;
    node.classList.remove("scroll");
    requestAnimationFrame(() => {
      if (node.scrollWidth > node.clientWidth + 4) {
        var first = document.createElement("span");
        first.className = "mq";
        first.textContent = text;
        var second = document.createElement("span");
        second.className = "mq";
        second.textContent = text;
        node.textContent = "";
        node.appendChild(first);
        node.appendChild(second);
        node.classList.add("scroll");
      }
    });
  }

  function applyPosition(frame) {
    state.lastFileMs = frame.filePositionMs;
    var fileMs = frame.filePositionMs;
    el.elapsed.textContent =
      fileMs == null ? "0:00" : formatSeconds(Math.floor(fileMs / 1000));
    var ms = frame.remainingMs;
    if (ms == null) {
      el.trackRemain.textContent = "--:--";
      el.elapsed.classList.remove("low");
      renderSeek(fileMs);
      return;
    }
    var total = Math.ceil(ms / 1000);
    if (total < 0) total = 0;
    el.trackRemain.textContent = "-" + formatSeconds(total);
    el.elapsed.classList.toggle("low", total <= 10 && total > 0);
    renderSeek(fileMs);
  }

  function renderSeek(fileMs) {
    if (fileMs == null || !trackWindowMs || trackWindowMs <= 0) {
      el.seekFill.style.width = "0%";
      return;
    }
    var ratio = Math.min(1, Math.max(0, fileMs / trackWindowMs));
    el.seekFill.style.width = (ratio * 100).toFixed(1) + "%";
  }

  function pad2(n) {
    return n < 10 ? "0" + n : "" + n;
  }

  function renderTransport() {
    var t = state.transport;
    if (!t) return;
    el.status.textContent = (t.status || "STOP").toUpperCase();
    marquee(el.title, t.current ? t.current.displayName : "—");
    marquee(el.next, "далее: " + (t.next ? t.next.displayName : "—"));
    el.panic.disabled = t.status === "Panicked";
    trackWindowMs = trackWindow(t.current);
    renderSeek(state.lastFileMs);
  }

  function trackWindow(current) {
    if (!current) return null;
    var total = parseIsoDuration(current.duration);
    if (total == null) return null;
    var cueIn = parseIsoDuration(current.cueIn) || 0;
    var cueOut = parseIsoDuration(current.cueOut);
    var end = cueOut == null ? total : cueOut;
    var window = (end - cueIn) * 1000;
    return window > 0 ? window : null;
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

  function parseIsoTicks(text) {
    if (typeof text !== "string") return 0;
    var parts = text.split(":");
    if (parts.length < 3) return 0;
    var hours = parseFloat(parts[0]);
    var minutes = parseFloat(parts[1]);
    var seconds = parseFloat(parts[2]);
    if (isNaN(hours) || isNaN(minutes) || isNaN(seconds)) return 0;
    return Math.round((hours * 3600 + minutes * 60 + seconds) * 1000) * 10000;
  }

  function formatSeconds(total) {
    if (total == null || isNaN(total)) return "--:--";
    if (total < 0) total = 0;
    var h = Math.floor(total / 3600);
    var m = Math.floor((total % 3600) / 60);
    var s = total % 60;
    return h > 0 ? h + ":" + pad2(m) + ":" + pad2(s) : m + ":" + pad2(s);
  }

  function renderShowClock() {
    var clock = state.show && state.show.clock;
    var total = clock ? parseIsoDuration(clock.elapsed) : null;
    el.showClock.textContent = total == null ? "--:--" : formatSeconds(total);
  }

  function scriptContentChanged(prev, next) {
    var prevScripts = prev && prev.scripts;
    var nextScripts = next && next.scripts;
    var prevDigest = prev && prev.trackDigest;
    var nextDigest = next && next.trackDigest;
    return (
      JSON.stringify(prevScripts) !== JSON.stringify(nextScripts) ||
      JSON.stringify(prevDigest) !== JSON.stringify(nextDigest)
    );
  }

  var activeScriptId = null;
  var scriptLineEls = [];
  var lastFollowIndex = null;

  function activeScript() {
    var scripts = (state.show && state.show.scripts) || [];
    if (!scripts.length) return null;
    if (!scripts.some((s) => s.id === activeScriptId))
      activeScriptId = scripts[0].id;
    return scripts.find((s) => s.id === activeScriptId) || null;
  }

  function clockSeconds() {
    var clock = state.show && state.show.clock;
    return clock ? parseIsoDuration(clock.elapsed) : null;
  }

  function trackNameMap() {
    var map = {};
    var digest = state.show && state.show.trackDigest;
    ((digest && digest.entries) || []).forEach((entry) => {
      map[entry.track] = entry.displayName;
    });
    return map;
  }

  var editingScriptLineId = null;
  var addingScriptLine = false;
  var scriptManage = null;
  var pendingNewScriptIds = null;

  function editingInside(host) {
    var active = document.activeElement;
    return (
      !!active &&
      host.contains(active) &&
      (active.tagName === "INPUT" ||
        active.tagName === "SELECT" ||
        active.tagName === "TEXTAREA")
    );
  }

  function renderScript() {
    if (editingInside(el.scriptTabs) || editingInside(el.scriptLines)) return;
    var scripts = (state.show && state.show.scripts) || [];
    el.scriptTabs.textContent = "";
    if (pendingNewScriptIds) {
      var known = pendingNewScriptIds;
      pendingNewScriptIds = null;
      var fresh = scripts.filter((s) => known.indexOf(s.id) < 0);
      if (fresh.length) activeScriptId = fresh[0].id;
    }
    if (!scripts.length) {
      el.scriptLines.textContent = "";
      scriptLineEls = [];
      el.scriptTabs.appendChild(
        smallButton("+ сценарий", () => {
          scriptManage = scriptManage === "create" ? null : "create";
          renderScript();
        }),
      );
      if (scriptManage === "create")
        el.scriptTabs.appendChild(scriptCreateForm());
      updateScriptFollow();
      return;
    }
    var script = activeScript();
    if (
      editingScriptLineId &&
      script.lines.every((line) => line.id !== editingScriptLineId)
    )
      editingScriptLineId = null;
    scripts.forEach((s) => {
      var tab = document.createElement("button");
      tab.className = "script-tab" + (s.id === activeScriptId ? " active" : "");
      tab.textContent = s.name || "сценарий";
      tab.addEventListener("click", () => {
        if (activeScriptId === s.id) return;
        activeScriptId = s.id;
        lastFollowIndex = null;
        editingScriptLineId = null;
        addingScriptLine = false;
        scriptManage = null;
        renderScript();
      });
      el.scriptTabs.appendChild(tab);
    });
    el.scriptTabs.appendChild(scriptManageRow());
    if (scriptManage === "create")
      el.scriptTabs.appendChild(scriptCreateForm());
    if (scriptManage === "rename")
      el.scriptTabs.appendChild(scriptRenameForm(script));
    if (scriptManage === "delete")
      el.scriptTabs.appendChild(scriptDeleteForm(script));
    renderScriptLines(script);
    updateScriptFollow();
  }

  function scriptManageRow() {
    var wrap = document.createElement("div");
    wrap.className = "inline-row";
    wrap.appendChild(
      smallButton("+ строка", () => {
        addingScriptLine = true;
        editingScriptLineId = null;
        renderScript();
      }),
    );
    wrap.appendChild(
      smallButton("✎", () => {
        scriptManage = scriptManage === "rename" ? null : "rename";
        renderScript();
      }),
    );
    wrap.appendChild(
      smallButton(
        "✕",
        () => {
          scriptManage = scriptManage === "delete" ? null : "delete";
          renderScript();
        },
        "danger",
      ),
    );
    return wrap;
  }

  function scriptCreateForm() {
    var wrap = document.createElement("div");
    wrap.className = "inline-row";
    var input = textInput("имя сценария", "");
    var ok = document.createElement("button");
    ok.className = "mini ok";
    ok.textContent = "ОК";
    ok.addEventListener("click", () => {
      var name = input.value.trim();
      if (!name) return;
      pendingNewScriptIds = (state.show.scripts || []).map((s) => s.id);
      send("create_script", { name: name });
      scriptManage = null;
      renderScript();
    });
    var cancel = document.createElement("button");
    cancel.className = "mini";
    cancel.textContent = "Отмена";
    cancel.addEventListener("click", () => {
      scriptManage = null;
      renderScript();
    });
    wrap.appendChild(input);
    wrap.appendChild(ok);
    wrap.appendChild(cancel);
    return wrap;
  }

  function scriptRenameForm(script) {
    var wrap = document.createElement("div");
    wrap.className = "inline-row";
    var input = textInput("имя сценария", script.name);
    var ok = document.createElement("button");
    ok.className = "mini ok";
    ok.textContent = "ОК";
    ok.addEventListener("click", () => {
      var name = input.value.trim();
      if (name && name !== script.name)
        send("rename_script", { id: script.id, name: name });
      scriptManage = null;
      renderScript();
    });
    var cancel = document.createElement("button");
    cancel.className = "mini";
    cancel.textContent = "Отмена";
    cancel.addEventListener("click", () => {
      scriptManage = null;
      renderScript();
    });
    wrap.appendChild(input);
    wrap.appendChild(ok);
    wrap.appendChild(cancel);
    return wrap;
  }

  function scriptDeleteForm(script) {
    var wrap = document.createElement("div");
    wrap.className = "inline-row";
    var label = document.createElement("span");
    label.textContent = "Удалить «" + script.name + "»?";
    var yes = document.createElement("button");
    yes.className = "mini danger";
    yes.textContent = "Удалить";
    yes.addEventListener("click", () => {
      send("delete_script", { id: script.id });
      activeScriptId = null;
      editingScriptLineId = null;
      addingScriptLine = false;
      scriptManage = null;
      renderScript();
    });
    var no = document.createElement("button");
    no.className = "mini";
    no.textContent = "Отмена";
    no.addEventListener("click", () => {
      scriptManage = null;
      renderScript();
    });
    wrap.appendChild(label);
    wrap.appendChild(yes);
    wrap.appendChild(no);
    return wrap;
  }

  function parseClockInput(text) {
    var parts = (text || "").trim().split(":");
    if (!parts.length || parts.length > 3) return null;
    var nums = parts.map((p) => parseInt(p, 10));
    if (nums.some((n) => isNaN(n) || n < 0)) return null;
    if (
      parts.length > 1 &&
      (nums[parts.length - 1] > 59 || nums[parts.length - 2] > 59)
    )
      return null;
    var total = 0;
    for (var i = 0; i < nums.length; i++) total = total * 60 + nums[i];
    return total;
  }

  function scriptTrackSelect(selected) {
    var select = document.createElement("select");
    select.className = "inline-input";
    var none = document.createElement("option");
    none.value = "";
    none.textContent = "— без трека —";
    select.appendChild(none);
    var entries =
      (state.show &&
        state.show.trackDigest &&
        state.show.trackDigest.entries) ||
      [];
    entries.forEach((entry) => {
      var option = document.createElement("option");
      option.value = entry.track;
      option.textContent = entry.displayName;
      if (entry.track === selected) option.selected = true;
      select.appendChild(option);
    });
    return select;
  }

  function scriptLineEditor(script, line) {
    var li = document.createElement("li");
    li.className = "script-line";
    var wrap = document.createElement("div");
    wrap.className = "inline-row";
    var atSec = line == null ? null : parseIsoDuration(line.atElapsed);
    var time = textInput(
      "м:сс",
      line == null || atSec == null ? "" : formatSeconds(atSec),
    );
    time.style.maxWidth = "90px";
    var text = textInput("текст строки", line == null ? "" : line.text);
    var mentions = (line && line.mentions) || [];
    var select = scriptTrackSelect(mentions.length ? mentions[0].track : "");
    var ok = document.createElement("button");
    ok.className = "mini ok";
    ok.textContent = "ОК";
    ok.addEventListener("click", () => {
      var at = parseClockInput(time.value);
      if (at == null) return;
      var track = select.value;
      if (line == null) {
        send("add_script_line", {
          script: script.id,
          at_ms: at * 1000,
          text: text.value,
          mentions: track ? [track] : [],
        });
        addingScriptLine = false;
      } else {
        send("update_script_line", {
          script: script.id,
          line: line.id,
          at_ms: at * 1000,
          text: text.value,
          mentions: track ? [track] : [],
        });
        editingScriptLineId = null;
      }
      renderScript();
    });
    wrap.appendChild(time);
    wrap.appendChild(text);
    wrap.appendChild(select);
    wrap.appendChild(ok);
    if (line != null) {
      var del = document.createElement("button");
      del.className = "mini danger";
      del.textContent = "✕";
      del.addEventListener("click", () => {
        if (del.textContent === "✕") {
          del.textContent = "?";
          return;
        }
        send("remove_script_line", { script: script.id, line: line.id });
        editingScriptLineId = null;
        renderScript();
      });
      wrap.appendChild(del);
    }
    var cancel = document.createElement("button");
    cancel.className = "mini";
    cancel.textContent = "Отмена";
    cancel.addEventListener("click", () => {
      editingScriptLineId = null;
      addingScriptLine = false;
      renderScript();
    });
    wrap.appendChild(cancel);
    li.appendChild(wrap);
    return li;
  }

  function renderScriptLines(script) {
    el.scriptLines.textContent = "";
    scriptLineEls = [];
    lastFollowIndex = null;
    if (!script) return;
    var names = trackNameMap();
    (script.lines || []).forEach((line) => {
      if (editingScriptLineId === line.id) {
        var editor = scriptLineEditor(script, line);
        el.scriptLines.appendChild(editor);
        scriptLineEls.push(editor);
        return;
      }
      var li = document.createElement("li");
      li.className = "script-line";
      var at = document.createElement("span");
      at.className = "script-at";
      var atSec = parseIsoDuration(line.atElapsed);
      at.textContent = atSec == null ? "--:--" : formatSeconds(atSec);
      var text = document.createElement("span");
      text.className = "script-text";
      text.textContent = line.text;
      li.appendChild(at);
      li.appendChild(text);
      var mentions = line.mentions || [];
      if (mentions.length) {
        var chips = document.createElement("span");
        chips.className = "script-chips";
        mentions.forEach((mention) => {
          var name = names[mention.track];
          var chip = document.createElement("span");
          chip.className = "chip" + (name ? "" : " dangling");
          chip.textContent = name || "повисшее";
          chips.appendChild(chip);
        });
        li.appendChild(chips);
      }
      li.addEventListener("click", () => smartClick(mentions, names));
      li.appendChild(
        smallButton("✎", () => {
          editingScriptLineId = line.id;
          addingScriptLine = false;
          renderScript();
        }),
      );
      el.scriptLines.appendChild(li);
      scriptLineEls.push(li);
    });
    if (addingScriptLine) {
      var adder = scriptLineEditor(script, null);
      el.scriptLines.appendChild(adder);
      scriptLineEls.push(adder);
    }
  }

  function smartClick(mentions, names) {
    if (!mentions.length) return;
    if (mentions.length === 1) {
      send("play_track", { track: mentions[0].track });
      vibrate();
      return;
    }
    openMentionMenu(mentions, names);
  }

  function openMentionMenu(mentions, names) {
    el.mentionOptions.textContent = "";
    mentions.forEach((mention) => {
      var name = names[mention.track];
      var button = document.createElement("button");
      button.className = "pad mention-option";
      button.textContent = name || "повисшее";
      button.disabled = !name;
      button.addEventListener("click", () => {
        hideMentionMenu();
        send("play_track", { track: mention.track });
        vibrate();
      });
      el.mentionOptions.appendChild(button);
    });
    el.mentionMenu.classList.remove("hidden");
  }

  function hideMentionMenu() {
    el.mentionMenu.classList.add("hidden");
  }

  function updateScriptFollow() {
    var script = activeScript();
    var elapsed = clockSeconds();
    var index = -1;
    if (script && elapsed != null) {
      (script.lines || []).forEach((line, i) => {
        var at = parseIsoDuration(line.atElapsed);
        if (at != null && at <= elapsed) index = i;
      });
    }
    if (index === lastFollowIndex) return;
    lastFollowIndex = index;
    scriptLineEls.forEach((li, i) =>
      li.classList.toggle("follow", i === index),
    );
    if (index >= 0 && scriptSectionVisible()) {
      scriptLineEls[index].scrollIntoView({ block: "nearest" });
    }
  }

  function scriptSectionVisible() {
    var rect = el.scriptSection.getBoundingClientRect();
    return rect.top < window.innerHeight && rect.bottom > 0;
  }

  function gainToPct(db) {
    if (!(db > -80)) return 0;
    return Math.max(0, Math.min(125, 10 ** (db / 40) * 100));
  }

  function pctToGain(pct) {
    if (!(pct > 0)) return -80;
    return (
      Math.round(Math.max(-80, Math.min(12, 40 * Math.log10(pct / 100))) * 10) /
      10
    );
  }

  function renderMixer() {
    var m = state.mixer;
    if (!m) return;
    el.master.value = Math.round(gainToPct(m.masterGainDb));
    el.mute.classList.toggle("active", !!m.muted);
  }

  function applyLufs(frame) {
    state.lufs =
      typeof frame.momentaryLufs === "number" ? frame.momentaryLufs : null;
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
    var width = Math.max(
      0,
      Math.min(100, ((clamped - LUFS_FLOOR) / -LUFS_FLOOR) * 100),
    );
    el.lufsFill.style.width = width + "%";
  }

  function transportPlaying(item) {
    var current = state.transport && state.transport.current;
    if (!current) return false;
    if (item.entryId && current.entryId)
      return item.entryId === current.entryId;
    return item.trackId === current.trackId;
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
    state.queue.forEach((item, index) => {
      var li = document.createElement("li");
      if (item.color) li.style.borderLeft = "4px solid " + item.color;
      if (transportPlaying(item)) li.classList.add("playing");
      var name = document.createElement("span");
      name.className = "queue-name";
      li.appendChild(name);
      marquee(name, item.displayName);
      var actions = document.createElement("span");
      actions.className = "queue-actions";
      if (index > 0) {
        var up = document.createElement("button");
        up.className = "qbtn";
        up.textContent = "↑";
        up.setAttribute("data-action", "up");
        up.setAttribute("data-index", index);
        actions.appendChild(up);
      }
      if (index < state.queue.length - 1) {
        var down = document.createElement("button");
        down.className = "qbtn";
        down.textContent = "↓";
        down.setAttribute("data-action", "down");
        down.setAttribute("data-index", index);
        actions.appendChild(down);
      }
      var remove = document.createElement("button");
      remove.className = "qbtn qbtn-remove";
      remove.textContent = "×";
      remove.setAttribute("data-action", "remove");
      remove.setAttribute("data-index", index);
      actions.appendChild(remove);
      li.appendChild(actions);
      el.queue.appendChild(li);
    });
  }

  var openProjectId = null;
  var overridesEntryId = null;
  var creatingProject = false;

  function digestName(trackId) {
    var entries =
      (state.show &&
        state.show.trackDigest &&
        state.show.trackDigest.entries) ||
      [];
    for (var i = 0; i < entries.length; i++) {
      if (entries[i].track === trackId) return entries[i].displayName;
    }
    return null;
  }

  function smallButton(label, onClick, extraClass) {
    var button = document.createElement("button");
    button.className = "mini" + (extraClass ? " " + extraClass : "");
    button.textContent = label;
    button.addEventListener("click", (event) => {
      event.stopPropagation();
      onClick();
      vibrate();
    });
    return button;
  }

  function textInput(placeholder, value) {
    var input = document.createElement("input");
    input.className = "inline-input";
    input.type = "text";
    input.placeholder = placeholder;
    if (value != null) input.value = value;
    return input;
  }

  function renameRow(pl, refresh) {
    var wrap = document.createElement("div");
    wrap.className = "inline-row";
    var input = textInput("имя проекта", pl.name);
    var ok = document.createElement("button");
    ok.className = "mini ok";
    ok.textContent = "ОК";
    ok.addEventListener("click", () => {
      var name = input.value.trim();
      if (name && name !== pl.name)
        send("rename_project", { id: pl.id, name: name });
      refresh();
    });
    var cancel = document.createElement("button");
    cancel.className = "mini";
    cancel.textContent = "Отмена";
    cancel.addEventListener("click", () => refresh());
    wrap.appendChild(input);
    wrap.appendChild(ok);
    wrap.appendChild(cancel);
    return wrap;
  }

  function deleteRow(pl, refresh) {
    var wrap = document.createElement("div");
    wrap.className = "inline-row";
    var label = document.createElement("span");
    label.textContent = "Удалить «" + pl.name + "»?";
    var yes = document.createElement("button");
    yes.className = "mini danger";
    yes.textContent = "Удалить";
    yes.addEventListener("click", () => {
      send("delete_project", { id: pl.id });
      if (openProjectId === pl.id) openProjectId = null;
      refresh();
    });
    var no = document.createElement("button");
    no.className = "mini";
    no.textContent = "Отмена";
    no.addEventListener("click", () => refresh());
    wrap.appendChild(label);
    wrap.appendChild(yes);
    wrap.appendChild(no);
    return wrap;
  }

  function applyOverrides(entry, refresh) {
    var form = document.querySelector('[data-ov-form="' + entry.id + '"]');
    if (!form) return;
    var overrides = {};
    var name = form.querySelector("[data-ov=name]").value.trim();
    var gain = form.querySelector("[data-ov=gain]").value.trim();
    var cueIn = form.querySelector("[data-ov=cueIn]").value.trim();
    var cueOut = form.querySelector("[data-ov=cueOut]").value.trim();
    var endAction = form.querySelector("[data-ov=endAction]").value;
    if (name) overrides.name = name;
    if (gain !== "") overrides.gain_db = Number(gain);
    if (cueIn !== "")
      overrides.cue_in_ticks = Math.round(Number(cueIn) * 10000);
    if (cueOut !== "")
      overrides.cue_out_ticks = Math.round(Number(cueOut) * 10000);
    if (endAction) overrides.end_action = endAction;
    if (Object.keys(overrides).length === 0) {
      send("set_entry_overrides", { entry: entry.id });
    } else {
      send("set_entry_overrides", { entry: entry.id, overrides: overrides });
    }
    overridesEntryId = null;
    refresh();
  }

  function overridesForm(entry, refresh) {
    var form = document.createElement("div");
    form.className = "ov-form";
    form.setAttribute("data-ov-form", entry.id);
    var o = entry.overrides || {};
    var fields = document.createElement("div");
    fields.className = "ov-fields";
    var nameInput = textInput("имя (override)", o.name == null ? null : o.name);
    nameInput.setAttribute("data-ov", "name");
    var gainInput = document.createElement("input");
    gainInput.className = "inline-input";
    gainInput.type = "number";
    gainInput.step = "0.1";
    gainInput.placeholder = "gain, дБ";
    if (o.gainDb != null) gainInput.value = o.gainDb;
    gainInput.setAttribute("data-ov", "gain");
    var cueInInput = document.createElement("input");
    cueInInput.className = "inline-input";
    cueInInput.type = "number";
    cueInInput.step = "10";
    cueInInput.placeholder = "cue in, мс";
    if (o.cueIn != null)
      cueInInput.value = Math.round(parseIsoTicks(o.cueIn) / 10000);
    cueInInput.setAttribute("data-ov", "cueIn");
    var cueOutInput = document.createElement("input");
    cueOutInput.className = "inline-input";
    cueOutInput.type = "number";
    cueOutInput.step = "10";
    cueOutInput.placeholder = "cue out, мс";
    if (o.cueOut != null)
      cueOutInput.value = Math.round(parseIsoTicks(o.cueOut) / 10000);
    cueOutInput.setAttribute("data-ov", "cueOut");
    var endAction = document.createElement("select");
    endAction.className = "inline-input";
    endAction.setAttribute("data-ov", "endAction");
    var emptyOption = document.createElement("option");
    emptyOption.value = "";
    emptyOption.textContent = "конец трека: как есть";
    endAction.appendChild(emptyOption);
    ["Pause", "Stop", "Replay", "Advance"].forEach((value) => {
      var option = document.createElement("option");
      option.value = value;
      option.textContent = value;
      if (o.endAction === value) option.selected = true;
      endAction.appendChild(option);
    });
    [nameInput, gainInput, cueInInput, cueOutInput, endAction].forEach(
      (field) => fields.appendChild(field),
    );
    form.appendChild(fields);
    var buttons = document.createElement("div");
    buttons.className = "inline-row";
    var apply = document.createElement("button");
    apply.className = "mini ok";
    apply.textContent = "Применить";
    apply.addEventListener("click", () => applyOverrides(entry, refresh));
    var reset = document.createElement("button");
    reset.className = "mini danger";
    reset.textContent = "Сброс";
    reset.addEventListener("click", () => {
      send("set_entry_overrides", { entry: entry.id });
      overridesEntryId = null;
      refresh();
    });
    var cancel = document.createElement("button");
    cancel.className = "mini";
    cancel.textContent = "Отмена";
    cancel.addEventListener("click", () => {
      overridesEntryId = null;
      refresh();
    });
    buttons.appendChild(apply);
    buttons.appendChild(reset);
    buttons.appendChild(cancel);
    form.appendChild(buttons);
    return form;
  }

  function entryRow(entry, index, count, refresh) {
    var wrap = document.createElement("div");
    wrap.className = "entry";
    var line = document.createElement("div");
    line.className = "entry-line";
    var name = digestName(entry.trackId);
    var label = document.createElement("span");
    label.className = "entry-name" + (name == null ? " dangling" : "");
    var labelText = name == null ? "(повисшее упоминание)" : name;
    if (entry.overrides && entry.overrides.name) {
      labelText += " → " + entry.overrides.name;
    }
    line.appendChild(label);
    marquee(label, labelText);
    line.appendChild(
      smallButton("▶", () => {
        send("jump_to", { entry: entry.id });
      }),
    );
    if (index > 0) {
      line.appendChild(
        smallButton("↑", () => {
          send("move_entry", { entry: entry.id, new_index: index - 1 });
          refresh();
        }),
      );
    }
    if (index < count - 1) {
      line.appendChild(
        smallButton("↓", () => {
          send("move_entry", { entry: entry.id, new_index: index + 1 });
          refresh();
        }),
      );
    }
    line.appendChild(
      smallButton("⚙", () => {
        overridesEntryId = overridesEntryId === entry.id ? null : entry.id;
        refresh();
      }),
    );
    line.appendChild(
      smallButton(
        "✕",
        () => {
          send("remove_entry", { entry: entry.id });
          refresh();
        },
        "danger",
      ),
    );
    wrap.appendChild(line);
    if (overridesEntryId === entry.id)
      wrap.appendChild(overridesForm(entry, refresh));
    return wrap;
  }

  function addEntryRow(pl, refresh) {
    var wrap = document.createElement("div");
    wrap.className = "inline-row";
    var select = document.createElement("select");
    select.className = "inline-input";
    var entries =
      (state.show &&
        state.show.trackDigest &&
        state.show.trackDigest.entries) ||
      [];
    entries.forEach((digestEntry) => {
      var option = document.createElement("option");
      option.value = digestEntry.track;
      option.textContent = digestEntry.displayName;
      select.appendChild(option);
    });
    var add = document.createElement("button");
    add.className = "mini ok";
    add.textContent = "+ добавить";
    add.addEventListener("click", () => {
      if (select.value)
        send("add_entry", { project: pl.id, track: select.value });
      refresh();
    });
    wrap.appendChild(select);
    wrap.appendChild(add);
    return wrap;
  }

  function createProjectRow(refresh) {
    var wrap = document.createElement("div");
    wrap.className = "inline-row";
    var input = textInput("новый проект");
    var ok = document.createElement("button");
    ok.className = "mini ok";
    ok.textContent = "Создать";
    ok.addEventListener("click", () => {
      var name = input.value.trim();
      if (name) send("create_project", { name: name });
      creatingProject = false;
      refresh();
    });
    var cancel = document.createElement("button");
    cancel.className = "mini";
    cancel.textContent = "Отмена";
    cancel.addEventListener("click", () => {
      creatingProject = false;
      refresh();
    });
    wrap.appendChild(input);
    wrap.appendChild(ok);
    wrap.appendChild(cancel);
    return wrap;
  }

  function projectBody(pl, refresh) {
    var body = document.createElement("div");
    body.className = "project-body";
    if (!pl.entries.length) {
      var empty = document.createElement("p");
      empty.className = "empty-note";
      empty.textContent = "пустой проект";
      body.appendChild(empty);
    }
    pl.entries.forEach((entry, index) => {
      body.appendChild(entryRow(entry, index, pl.entries.length, refresh));
    });
    body.appendChild(addEntryRow(pl, refresh));
    return body;
  }

  function renderProjects() {
    var host = el.projects;
    if (editingInside(host)) return;
    host.textContent = "";
    if (!state.show) return;
    var projects = state.show.projects || [];
    if (openProjectId && !projects.some((pl) => pl.id === openProjectId)) {
      openProjectId = null;
      overridesEntryId = null;
    }
    var refresh = () => renderProjects();
    projects.forEach((pl) => {
      var row = document.createElement("div");
      row.className = "playlist";
      var active = state.show.activeId === pl.id;
      if (active) row.classList.add("active");
      var head = document.createElement("div");
      head.className = "project-head";
      var name = document.createElement("span");
      name.className = "project-name";
      head.appendChild(name);
      marquee(name, pl.name);
      if (active) {
        var badge = document.createElement("span");
        badge.className = "project-badge";
        badge.textContent = "АКТИВНЫЙ";
        head.appendChild(badge);
      }
      if (state.show.activeId !== pl.id) {
        head.appendChild(
          smallButton("активировать", () => {
            send("set_active_project", { id: pl.id });
          }),
        );
      }
      head.appendChild(
        smallButton("✎", () => {
          row.textContent = "";
          var form = renameRow(pl, refresh);
          row.appendChild(form);
          var field = form.querySelector("input");
          if (field) field.focus();
        }),
      );
      head.appendChild(
        smallButton(
          "✕",
          () => {
            row.textContent = "";
            row.appendChild(deleteRow(pl, refresh));
          },
          "danger",
        ),
      );
      row.appendChild(head);
      var isOpen = openProjectId === pl.id;
      if (isOpen) {
        row.classList.add("open");
        row.appendChild(projectBody(pl, refresh));
      }
      name.addEventListener("click", () => {
        openProjectId = isOpen ? null : pl.id;
        overridesEntryId = null;
        refresh();
      });
      host.appendChild(row);
    });
    var createToggle = document.createElement("button");
    createToggle.className = "project-create";
    createToggle.textContent = creatingProject ? "Отмена" : "+ новый проект";
    createToggle.addEventListener("click", () => {
      creatingProject = !creatingProject;
      refresh();
    });
    if (creatingProject) host.appendChild(createProjectRow(refresh));
    host.appendChild(createToggle);
  }

  el.panic.addEventListener("click", () => {
    el.panicConfirm.classList.remove("hidden");
  });

  el.queueClear.addEventListener("click", () => {
    el.clearConfirm.classList.remove("hidden");
  });
  el.clearYes.addEventListener("click", () => {
    el.clearConfirm.classList.add("hidden");
    send("clear_queue");
    vibrate();
  });
  el.clearNo.addEventListener("click", () => {
    el.clearConfirm.classList.add("hidden");
  });

  el.queue.addEventListener("click", (event) => {
    var button = event.target.closest("button[data-action]");
    if (!button) return;
    var index = Number(button.getAttribute("data-index"));
    var action = button.getAttribute("data-action");
    if (action === "remove") {
      send("remove_from_queue", { index: index });
    } else if (action === "up") {
      send("move_queue_item", { from: index, to: index - 1 });
    } else if (action === "down") {
      send("move_queue_item", { from: index, to: index + 1 });
    }
    vibrate();
  });
  el.panicYes.addEventListener("click", () => {
    el.panicConfirm.classList.add("hidden");
    send("panic");
    vibrate();
  });
  el.panicNo.addEventListener("click", () => {
    el.panicConfirm.classList.add("hidden");
  });

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

  var previewMuted = false;

  el.previewPlay.addEventListener("click", () => {
    var current = state.transport && state.transport.current;
    if (!current || !current.trackId) return;
    send("start_preview_track", { track: current.trackId });
    ensureToken().then((token) => {
      if (!token) return;
      el.previewAudio.src = "/preview?token=" + encodeURIComponent(token);
      el.previewAudio.play().catch(() => {});
    });
    vibrate();
  });

  el.previewStop.addEventListener("click", () => {
    send("stop_preview", {});
    try {
      el.previewAudio.pause();
      el.previewAudio.removeAttribute("src");
      el.previewAudio.load();
    } catch {}
    vibrate();
  });

  el.previewGain.addEventListener("input", () => {
    send("set_preview_gain", {
      gain_db: pctToGain(Number(el.previewGain.value)),
    });
  });

  el.previewMute.addEventListener("click", () => {
    previewMuted = !previewMuted;
    send("set_preview_muted", { muted: previewMuted });
    el.previewMute.classList.toggle("active", previewMuted);
    vibrate();
  });

  if (el.seek) {
    el.seek.addEventListener("click", (event) => {
      var rect = el.seek.getBoundingClientRect();
      if (rect.width <= 0 || !trackWindowMs || trackWindowMs <= 0) return;
      var current = state.transport && state.transport.current;
      if (!current) return;
      var cueIn = (parseIsoDuration(current.cueIn) || 0) * 1000;
      var ratio = Math.min(
        1,
        Math.max(0, (event.clientX - rect.left) / rect.width),
      );
      send("seek_to", {
        position_ms: Math.round(cueIn + ratio * trackWindowMs),
      });
      vibrate();
    });
  }

  el.mentionCancel.addEventListener("click", hideMentionMenu);

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

  if (el.defaultAction) {
    el.defaultAction.addEventListener("change", () => {
      send("set_default_end_action", { end_action: el.defaultAction.value });
      vibrate();
    });
  }

  async function requestWakeLock() {
    try {
      if ("wakeLock" in navigator) {
        var lock = await navigator.wakeLock.request("screen");
        lock.addEventListener("release", () => {
          setTimeout(requestWakeLock, 1000);
        });
      }
    } catch {}
  }

  document.addEventListener("visibilitychange", () => {
    if (document.visibilityState === "visible") requestWakeLock();
  });

  function closeSocket() {
    if (!ws) return;
    try {
      ws.onclose = null;
      ws.close();
    } catch {}
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
