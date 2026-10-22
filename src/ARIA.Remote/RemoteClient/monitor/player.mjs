import { curve, dbToGain, fadeLevel, frameAt, hostUsOf, SAMPLE_RATE, structuralKey } from "./timeline.mjs";
import { CHUNK_FRAMES, EMPTY } from "./store.mjs";

const HORIZON_MS = 1000;
const REANCHOR_MS = 5;
const MAX_RATE_DEVIATION = 5e-4;
const FADE_IN_S = 0.01;
const FADE_OUT_S = 0.008;
const START_LEAD_S = 0.02;
const CURVE_POINTS = 32;

export function planChunks(voice, rate, fromUs, toUs, chunkFrames = CHUNK_FRAMES) {
  if (!voice.playing) return [];
  let start = Math.max(0, Math.ceil(frameAt(voice, fromUs, rate)));
  let end = Math.floor(frameAt(voice, toUs, rate));
  if (voice.endFrame != null) end = Math.min(end, voice.endFrame);
  const plans = [];
  while (start < end) {
    const chunk = Math.floor(start / chunkFrames);
    const offset = start - chunk * chunkFrames;
    const frames = Math.min(chunkFrames - offset, end - start);
    plans.push({ chunk, offset, frames, startUs: hostUsOf(voice, start, rate) });
    start += frames;
  }
  return plans;
}

export function driftDecision(errorMs, chunkSeconds) {
  if (Math.abs(errorMs) > REANCHOR_MS) return { reanchor: true };
  const correction = Math.max(-MAX_RATE_DEVIATION, Math.min(MAX_RATE_DEVIATION, errorMs / 1000 / (2 * chunkSeconds)));
  return { reanchor: false, rate: 1 + correction };
}

export function curveValues(curveName, from, to, tFrom, count) {
  const values = new Float32Array(count);
  for (let index = 0; index < count; index++) {
    const t = tFrom + (1 - tFrom) * (count === 1 ? 1 : index / (count - 1));
    values[index] = from + (to - from) * curve(curveName, t);
  }
  return values;
}

export class Player {
  constructor(ctx, store, output, clock, destination, ctxOfHost, hostOfCtx) {
    this.ctx = ctx;
    this.store = store;
    this.output = output;
    this.clock = clock;
    this.ctxOfHost = ctxOfHost;
    this.hostOfCtx = hostOfCtx;
    this.bus = ctx.createGain();
    this.bus.connect(destination);
    this.voices = new Map();
    this.rate = SAMPLE_RATE;
  }

  setLevel(gain) {
    this.bus.gain.setTargetAtTime(gain, this.ctx.currentTime, 0.02);
  }

  update(timeline) {
    this.rate = timeline.rate;
    const present = new Set();
    for (const voice of timeline.voices) {
      present.add(voice.id);
      const state = this.voices.get(voice.id);
      if (!state) {
        this.voices.set(voice.id, this.createState(voice));
        continue;
      }
      const key = structuralKey(voice);
      state.voice = voice;
      if (key !== state.key) {
        state.key = key;
        this.restructure(state, voice);
      }
    }
    for (const [id, state] of this.voices) {
      if (!present.has(id)) {
        this.release(state, this.ctx.currentTime);
        this.voices.delete(id);
      }
    }
  }

  createState(voice) {
    const gain = this.ctx.createGain();
    gain.connect(this.bus);
    const state = { voice, key: structuralKey(voice), gain, sources: [], nextFrame: null, nextCtx: null, fadeIn: true };
    this.applyEnvelope(state, voice);
    return state;
  }

  restructure(state, voice) {
    const change = Math.max(this.ctxOfHost(voice.anchorUs), this.ctx.currentTime);
    for (const source of state.sources) {
      if (source.startCtx >= change) {
        this.stopSource(source, 0);
      } else if (source.endCtx > change) {
        this.fadeOutSource(source, change);
      }
    }
    state.sources = state.sources.filter((source) => source.startCtx < change && source.endCtx > this.ctx.currentTime);
    state.nextFrame = null;
    state.nextCtx = null;
    state.fadeIn = true;
    this.applyEnvelope(state, voice);
  }

  release(state, at) {
    for (const source of state.sources) {
      if (source.startCtx >= at) this.stopSource(source, 0);
      else this.fadeOutSource(source, at);
    }
    state.sources = [];
    setTimeout(() => {
      try {
        state.gain.disconnect();
      } catch {}
    }, 200);
  }

  stopSource(source, when) {
    try {
      source.node.stop(when);
    } catch {}
  }

  fadeOutSource(source, at) {
    const start = Math.max(this.ctx.currentTime, at - FADE_OUT_S);
    try {
      source.gain.gain.cancelScheduledValues(start);
      source.gain.gain.setValueAtTime(source.gain.gain.value, start);
      source.gain.gain.linearRampToValueAtTime(0, at);
    } catch {}
    this.stopSource(source, at + 0.002);
  }

  applyEnvelope(state, voice) {
    const param = state.gain.gain;
    const base = dbToGain(voice.gainDb);
    const now = this.ctx.currentTime;
    const anchorCtx = this.ctxOfHost(voice.anchorUs);
    const elapsedFrames = Math.max(0, (now - anchorCtx) * this.rate);
    try {
      param.cancelScheduledValues(0);
      param.setValueAtTime(base * fadeLevel(voice.fade, elapsedFrames), now);
    } catch {}
    const fade = voice.fade;
    if (!fade || fade.progress >= 1) return;
    const remainingFrames = (1 - fade.progress) * fade.frames;
    const fadeEndCtx = anchorCtx + remainingFrames / this.rate;
    if (fadeEndCtx <= now) return;
    const startCtx = Math.max(now, anchorCtx);
    const tFrom = Math.min(1, fade.progress + Math.max(0, startCtx - anchorCtx) * this.rate / Math.max(1, fade.frames));
    const values = curveValues(fade.curve, fade.from * base, fade.to * base, tFrom, CURVE_POINTS);
    try {
      param.setValueCurveAtTime(values, startCtx + 0.001, Math.max(0.005, fadeEndCtx - startCtx - 0.001));
    } catch {}
  }

  tick() {
    if (!this.clock.ready) return;
    const now = this.ctx.currentTime;
    const horizonCtx = now + (HORIZON_MS + this.output.trimMs) / 1000;
    for (const state of this.voices.values()) {
      state.sources = state.sources.filter((source) => source.endCtx > now);
      this.schedule(state, now, horizonCtx);
    }
  }

  schedule(state, now, horizonCtx) {
    const voice = state.voice;
    if (!voice.playing) return;
    if (state.nextFrame == null || state.nextCtx == null || state.nextCtx < now + 0.002) {
      const startHost = Math.max(voice.anchorUs, this.hostOfCtx(now + START_LEAD_S));
      state.nextFrame = Math.max(0, Math.ceil(frameAt(voice, startHost, this.rate)));
      state.nextCtx = null;
      state.fadeIn = true;
    }
    const plans = planChunks(voice, this.rate, hostUsOf(voice, state.nextFrame, this.rate), this.hostOfCtx(horizonCtx));
    for (const plan of plans) {
      const buffer = this.store.get(voice.track, plan.chunk);
      if (!buffer) return;
      const firstFrame = plan.chunk * CHUNK_FRAMES + plan.offset;
      if (buffer === EMPTY || plan.offset >= buffer.length) {
        state.nextFrame = firstFrame + plan.frames;
        state.nextCtx = null;
        continue;
      }
      const frames = Math.min(plan.frames, buffer.length - plan.offset);
      const ideal = this.ctxOfHost(plan.startUs);
      let when = ideal;
      let playbackRate = 1;
      let fadeIn = state.fadeIn;
      if (state.nextCtx != null) {
        const decision = driftDecision((state.nextCtx - ideal) * 1000, frames / SAMPLE_RATE);
        if (decision.reanchor) {
          fadeIn = true;
        } else {
          when = state.nextCtx;
          playbackRate = decision.rate;
        }
      }
      let offset = plan.offset;
      let length = frames;
      if (when < now + 0.002) {
        const skip = Math.ceil((now + 0.005 - when) * SAMPLE_RATE * playbackRate);
        if (skip >= length) {
          state.nextFrame = firstFrame + frames;
          state.nextCtx = null;
          continue;
        }
        offset += skip;
        length -= skip;
        when = now + 0.005;
        fadeIn = true;
      }
      this.startSource(state, buffer, when, offset, length, playbackRate, fadeIn);
      state.fadeIn = false;
      state.nextFrame = firstFrame + frames;
      state.nextCtx = when + length / SAMPLE_RATE / playbackRate;
    }
  }

  startSource(state, buffer, when, offset, length, playbackRate, fadeIn) {
    const node = this.ctx.createBufferSource();
    node.buffer = buffer;
    node.playbackRate.value = playbackRate;
    const gain = this.ctx.createGain();
    node.connect(gain);
    gain.connect(state.gain);
    if (fadeIn) {
      gain.gain.setValueAtTime(0, when);
      gain.gain.linearRampToValueAtTime(1, when + FADE_IN_S);
    }
    node.start(when, offset / SAMPLE_RATE, length / SAMPLE_RATE);
    const source = { node, gain, startCtx: when, endCtx: when + length / SAMPLE_RATE / playbackRate };
    node.onended = () => {
      try {
        gain.disconnect();
      } catch {}
    };
    state.sources.push(source);
  }

  silence(seconds) {
    const now = this.ctx.currentTime;
    this.bus.gain.cancelScheduledValues(now);
    this.bus.gain.setValueAtTime(this.bus.gain.value, now);
    this.bus.gain.linearRampToValueAtTime(0, now + seconds);
  }
}
