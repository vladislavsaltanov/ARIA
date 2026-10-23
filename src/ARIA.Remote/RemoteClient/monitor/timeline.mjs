export const SAMPLE_RATE = 48000;

export function frameAt(voice, hostUs, rate) {
  return voice.playing ? voice.anchorFrame + ((hostUs - voice.anchorUs) * rate) / 1e6 : voice.anchorFrame;
}

export function hostUsOf(voice, frame, rate) {
  return voice.anchorUs + ((frame - voice.anchorFrame) * 1e6) / rate;
}

export function curve(name, t) {
  switch (name) {
    case "Logarithmic":
      return Math.sqrt(t);
    case "Exponential":
      return t * t;
    case "SCurve":
      return t * t * (3 - 2 * t);
    default:
      return t;
  }
}

export function fadeLevel(fade, framesSinceAnchor) {
  if (!fade) return 1;
  const t = Math.min(1, Math.max(0, fade.progress + framesSinceAnchor / Math.max(1, fade.frames)));
  return fade.from + (fade.to - fade.from) * curve(fade.curve, t);
}

export function beatsBetween(grid, voice, fromUs, toUs, rate) {
  if (!grid || !voice || !voice.playing || !(grid.bpm > 0) || !(grid.beatsPerBar > 0)) return [];
  const framesPerBeat = (60 / grid.bpm) * SAMPLE_RATE;
  const from = frameAt(voice, fromUs, rate);
  let to = frameAt(voice, toUs, rate);
  if (voice.endFrame != null) to = Math.min(to, voice.endFrame);
  if (to <= from) return [];
  const first = Math.ceil((from - grid.firstBeatFrame) / framesPerBeat);
  const last = Math.floor((to - grid.firstBeatFrame - 1e-9) / framesPerBeat);
  const beats = [];
  for (let index = first; index <= last; index++) {
    const frame = grid.firstBeatFrame + index * framesPerBeat;
    if (frame < 0) continue;
    const bar = grid.beatsPerBar;
    beats.push({ hostUs: hostUsOf(voice, frame, rate), index, accent: ((index % bar) + bar) % bar === 0 });
  }
  return beats;
}

export function structuralKey(voice) {
  const fade = voice.fade ? [voice.fade.to, voice.fade.frames, voice.fade.curve, voice.fade.stopAtEnd].join(",") : "";
  return [voice.epoch, voice.playing, voice.endFrame, voice.track, fade].join("|");
}

export function dbToGain(db) {
  return Math.pow(10, db / 20);
}
