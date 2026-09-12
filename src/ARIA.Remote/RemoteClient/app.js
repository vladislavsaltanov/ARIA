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
    trackElapsed: document.getElementById("track-elapsed"),
    master: document.getElementById("master"),
    mute: document.getElementById("btn-mute"),
    lufsValue: document.getElementById("lufs-value"),
    lufsFill: document.getElementById("lufs-fill"),
    playlists: document.getElementById("playlists"),
    scriptSection: document.getElementById("script-section"),
    scriptTabs: document.getElementById("script-tabs"),
    scriptLines: document.getElementById("script-lines"),
    mentionMenu: document.getElementById("mention-menu"),
    mentionOptions: document.getElementById("mention-options"),
    lock: document.getElementById("lock"),
    toast: document.getElementById("toast"),
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

  function applySnapshot(frame) {
    state.show = frame.show ? frame.show.state : null;
    state.mixer = frame.mixer ? frame.mixer.state : null;
    state.transport = frame.transport.state;
    renderTransport();
    renderMixer();
    renderShowClock();
    renderPlaylists();
    renderScript();
    renderLock();
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
      renderPlaylists();
      renderLock();
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

  function applyPosition(frame) {
    var fileMs = frame.filePositionMs;
    el.trackElapsed.textContent =
      fileMs == null ? "0:00" : formatSeconds(Math.floor(fileMs / 1000));
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
    var text =
      h > 0
        ? h + ":" + pad(m) + ":" + pad(total % 60)
        : pad(Math.floor(total / 60)) + ":" + pad(total % 60);
    el.remaining.textContent = text;
    el.remaining.classList.toggle("low", total <= 10 && total > 0);
  }

  function pad(n) {
    return n < 10 ? "0" + n : "" + n;
  }

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
    return h > 0 ? h + ":" + pad(m) + ":" + pad(s) : m + ":" + pad(s);
  }

  function renderShowClock() {
    var clock = state.show && state.show.clock;
    var total = clock ? parseIsoDuration(clock.elapsed) : null;
    el.showClock.textContent = total == null ? "--:--" : formatSeconds(total);
  }

  function scriptChanged(a, b) {
    return JSON.stringify(a) !== JSON.stringify(b);
  }

  function scriptContentChanged(prev, next) {
    return (
      scriptChanged(prev && prev.scripts, next && next.scripts) ||
      scriptChanged(prev && prev.trackDigest, next && next.trackDigest)
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

  function renderScript() {
    var scripts = (state.show && state.show.scripts) || [];
    el.scriptTabs.textContent = "";
    if (!scripts.length) {
      el.scriptLines.textContent = "";
      scriptLineEls = [];
      updateScriptFollow();
      return;
    }
    var script = activeScript();
    scripts.forEach((s) => {
      var tab = document.createElement("button");
      tab.className = "script-tab" + (s.id === activeScriptId ? " active" : "");
      tab.textContent = s.name || "сценарий";
      tab.addEventListener("click", () => {
        if (activeScriptId === s.id) return;
        activeScriptId = s.id;
        lastFollowIndex = null;
        renderScript();
      });
      el.scriptTabs.appendChild(tab);
    });
    renderScriptLines(script);
    updateScriptFollow();
  }

  function renderScriptLines(script) {
    el.scriptLines.textContent = "";
    scriptLineEls = [];
    lastFollowIndex = null;
    if (!script) return;
    var names = trackNameMap();
    (script.lines || []).forEach((line) => {
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
      el.scriptLines.appendChild(li);
      scriptLineEls.push(li);
    });
  }

  function smartClick(mentions, names) {
    if (!mentions.length) return;
    if (mentions.length === 1) {
      send("enqueue_track", { track: mentions[0].track });
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
        send("enqueue_track", { track: mention.track });
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
    var pct = ((db + 80) / 92) * 100;
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
    if (item.entryId && current.entryId) return item.entryId === current.entryId;
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
      name.textContent = item.displayName;
      li.appendChild(name);
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

  var openPlaylistId = null;
  var overridesEntryId = null;
  var creatingPlaylist = false;

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
    var input = textInput("имя плейлиста", pl.name);
    var ok = document.createElement("button");
    ok.className = "mini ok";
    ok.textContent = "ОК";
    ok.addEventListener("click", () => {
      var name = input.value.trim();
      if (name && name !== pl.name)
        send("rename_playlist", { id: pl.id, name: name });
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
      send("delete_playlist", { id: pl.id });
      if (openPlaylistId === pl.id) openPlaylistId = null;
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
    label.textContent = name == null ? "(повисшее упоминание)" : name;
    if (entry.overrides && entry.overrides.name) {
      label.textContent += " → " + entry.overrides.name;
    }
    line.appendChild(label);
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
        send("add_entry", { playlist: pl.id, track: select.value });
      refresh();
    });
    wrap.appendChild(select);
    wrap.appendChild(add);
    return wrap;
  }

  function createPlaylistRow(refresh) {
    var wrap = document.createElement("div");
    wrap.className = "inline-row";
    var input = textInput("новый плейлист");
    var ok = document.createElement("button");
    ok.className = "mini ok";
    ok.textContent = "Создать";
    ok.addEventListener("click", () => {
      var name = input.value.trim();
      if (name) send("create_playlist", { name: name });
      creatingPlaylist = false;
      refresh();
    });
    var cancel = document.createElement("button");
    cancel.className = "mini";
    cancel.textContent = "Отмена";
    cancel.addEventListener("click", () => {
      creatingPlaylist = false;
      refresh();
    });
    wrap.appendChild(input);
    wrap.appendChild(ok);
    wrap.appendChild(cancel);
    return wrap;
  }

  function playlistBody(pl, refresh) {
    var body = document.createElement("div");
    body.className = "playlist-body";
    if (!pl.entries.length) {
      var empty = document.createElement("p");
      empty.className = "empty-note";
      empty.textContent = "пустой плейлист";
      body.appendChild(empty);
    }
    pl.entries.forEach((entry, index) => {
      body.appendChild(entryRow(entry, index, pl.entries.length, refresh));
    });
    body.appendChild(addEntryRow(pl, refresh));
    return body;
  }

  function renderPlaylists() {
    var host = el.playlists;
    host.textContent = "";
    if (!state.show) return;
    var playlists = state.show.playlists || [];
    if (openPlaylistId && !playlists.some((pl) => pl.id === openPlaylistId)) {
      openPlaylistId = null;
      overridesEntryId = null;
    }
    var refresh = () => renderPlaylists();
    playlists.forEach((pl) => {
      var row = document.createElement("div");
      row.className = "playlist";
      var active = state.show.activeId === pl.id;
      if (active) row.classList.add("active");
      var head = document.createElement("div");
      head.className = "playlist-head";
      var name = document.createElement("span");
      name.className = "playlist-name";
      name.textContent = pl.name;
      head.appendChild(name);
      if (active) {
        var badge = document.createElement("span");
        badge.className = "playlist-badge";
        badge.textContent = "АКТИВНЫЙ";
        head.appendChild(badge);
      }
      if (state.show.activeId !== pl.id) {
        head.appendChild(
          smallButton("активировать", () => {
            send("set_active_playlist", { id: pl.id });
          }),
        );
      }
      head.appendChild(
        smallButton("✎", () => {
          row.textContent = "";
          row.appendChild(renameRow(pl, refresh));
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
      var isOpen = openPlaylistId === pl.id;
      if (isOpen) {
        row.classList.add("open");
        row.appendChild(playlistBody(pl, refresh));
      }
      name.addEventListener("click", () => {
        openPlaylistId = isOpen ? null : pl.id;
        overridesEntryId = null;
        refresh();
      });
      host.appendChild(row);
    });
    var createToggle = document.createElement("button");
    createToggle.className = "playlist-create";
    createToggle.textContent = creatingPlaylist ? "Отмена" : "+ новый плейлист";
    createToggle.addEventListener("click", () => {
      creatingPlaylist = !creatingPlaylist;
      refresh();
    });
    if (creatingPlaylist) host.appendChild(createPlaylistRow(refresh));
    host.appendChild(createToggle);
  }

  function showConfirm() {
    el.panicConfirm.classList.remove("hidden");
  }
  function hideConfirm() {
    el.panicConfirm.classList.add("hidden");
  }

  el.panic.addEventListener("click", showConfirm);

  el.queueClear.addEventListener("click", () => {
    el.clearConfirm.classList.remove("hidden");
  });
  document.getElementById("clear-yes").addEventListener("click", () => {
    el.clearConfirm.classList.add("hidden");
    send("clear_queue");
    vibrate();
  });
  document.getElementById("clear-no").addEventListener("click", () => {
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

  document
    .getElementById("mention-cancel")
    .addEventListener("click", hideMentionMenu);

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
