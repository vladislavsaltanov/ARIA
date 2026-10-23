const VIDEO = "data:video/mp4;base64,AAAAHGZ0eXBtcDQyAAAAAWlzb21tcDQxbXA0MgAAAAFtZGF0AAAAAAAAAL4AAAA2BgUtR1ZK3FxMQz+U78URPNFDqAEAAAMAAQMAAAMAAQIAAAfQCwAAAwAAAwAAAwDSDAORKwGAAAAAJiW4IACD/x67v/70wYBE1CeG1bepiPbqe3D8suYhjOIAAPYEWnhwAAAAGgYFFUdWStxcTEM/lO/FETzRQ6gDAAADAAGAAAAAKCW4EAB7/88zvv/hsrnALw67RBGMPFGPOiq+QB0mfudBqAAEMBFpMyAAAAK0bW9vdgAAAGxtdmhkAAAAAOb/4xzm/+McAAACWAAABLAAAQAAAQAAAAAAAAAAAAAAAAEAAAAAAAAAAAAAAAAAAAABAAAAAAAAAAAAAAAAAABAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAgAAAkB0cmFrAAAAXHRraGQAAAAB5v/jHOb/4xwAAAABAAAAAAAABLAAAAAAAAAAAAAAAAAAAAAAAAEAAAAAAAAAAAAAAAAAAAABAAAAAAAAAAAAAAAAAABAAAAAABAAAAAQAAAAAAAkZWR0cwAAABxlbHN0AAAAAAAAAAEAAASwAAAAAAABAAAAAAG4bWRpYQAAACBtZGhkAAAAAOb/4xzm/+McAAACWAAABLBVxAAAAAAAMWhkbHIAAAAAAAAAAHZpZGUAAAAAAAAAAAAAAABDb3JlIE1lZGlhIFZpZGVvAAAAAV9taW5mAAAAFHZtaGQAAAABAAAAAAAAAAAAAAAkZGluZgAAABxkcmVmAAAAAAAAAAEAAAAMdXJsIAAAAAEAAAEfc3RibAAAAKFzdHNkAAAAAAAAAAEAAACRYXZjMQAAAAAAAAABAAAAAAAAAAAAAAAAAAAAAAAQABAASAAAAEgAAAAAAAAAAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABj//wAAACdhdmNDAWQAC//hAAwnZAALrFcMN4FmClABAAQo7jyw/fj4AAAAAApmaWVsAQAAAAAKY2hybQAAAAAAGHN0dHMAAAAAAAAAAQAAAAIAAAJYAAAADnNkdHAAAAAAICAAAAAcc3RzYwAAAAAAAAABAAAAAQAAAAEAAAABAAAAHHN0c3oAAAAAAAAAAAAAAAIAAABkAAAASgAAABhzdGNvAAAAAAAAAAIAAAAsAAAAkA==";

let video = null;
let lock = null;
let silentAudio = null;

async function requestLock() {
  try {
    lock = await navigator.wakeLock.request("screen");
    lock.addEventListener("release", () => {
      lock = null;
    });
    return true;
  } catch {
    return false;
  }
}

export async function startKeepAwake() {
  if (window.isSecureContext && navigator.wakeLock && (await requestLock())) {
    document.addEventListener("visibilitychange", () => {
      if (document.visibilityState === "visible" && !lock) requestLock();
    });
    return "wakelock";
  }
  if (!video) {
    video = document.createElement("video");
    video.setAttribute("playsinline", "");
    video.setAttribute("muted", "");
    video.muted = true;
    video.loop = true;
    video.style.cssText = "position:fixed;left:0;bottom:0;width:1px;height:1px;opacity:0.01;pointer-events:none";
    video.src = VIDEO;
    document.body.appendChild(video);
  }
  try {
    await video.play();
    return "video";
  } catch {
    return "none";
  }
}

function silentWavUrl() {
  const frames = 4000;
  const bytes = new Uint8Array(44 + frames);
  const view = new DataView(bytes.buffer);
  const text = (offset, value) => [...value].forEach((char, index) => view.setUint8(offset + index, char.charCodeAt(0)));
  text(0, "RIFF");
  view.setUint32(4, 36 + frames, true);
  text(8, "WAVE");
  text(12, "fmt ");
  view.setUint32(16, 16, true);
  view.setUint16(20, 1, true);
  view.setUint16(22, 1, true);
  view.setUint32(24, 8000, true);
  view.setUint32(28, 8000, true);
  view.setUint16(32, 1, true);
  view.setUint16(34, 8, true);
  text(36, "data");
  view.setUint32(40, frames, true);
  bytes.fill(128, 44);
  return URL.createObjectURL(new Blob([bytes], { type: "audio/wav" }));
}

export function unlockAudioSession() {
  try {
    if (navigator.audioSession) {
      navigator.audioSession.type = "playback";
      return "session";
    }
  } catch {}
  if (!silentAudio) {
    silentAudio = new Audio(silentWavUrl());
    silentAudio.loop = true;
  }
  silentAudio.play().catch(() => {});
  return "audio";
}
