import { frameAt, SAMPLE_RATE } from "./timeline.mjs";

export const CHUNK_FRAMES = 96000;

const AHEAD_FRAMES = 20 * SAMPLE_RATE;
const BEHIND_FRAMES = 0.5 * SAMPLE_RATE;
const STANDBY_FRAMES = 6 * SAMPLE_RATE;
const RETRY_MS = 200;
const FAILURE_COOLDOWN_MS = 1000;

export function windowsFor(timeline, nowUs) {
  const windows = [];
  for (const voice of timeline.voices) {
    if (voice.playing) {
      const frame = frameAt(voice, nowUs, timeline.rate);
      windows.push({ track: voice.track, from: Math.max(0, Math.floor(frame - BEHIND_FRAMES)), to: Math.floor(frame + AHEAD_FRAMES) });
    } else {
      windows.push({ track: voice.track, from: Math.max(0, voice.anchorFrame), to: voice.anchorFrame + STANDBY_FRAMES });
    }
  }
  const next = timeline.deck && timeline.deck.next;
  if (next) windows.push({ track: next.track, from: Math.max(0, next.startFrame), to: next.startFrame + STANDBY_FRAMES });
  return windows;
}

export function chunksFor(window, chunkFrames = CHUNK_FRAMES) {
  const chunks = [];
  if (window.to <= window.from) return chunks;
  const last = Math.floor((window.to - 1) / chunkFrames);
  for (let chunk = Math.floor(window.from / chunkFrames); chunk <= last; chunk++) chunks.push({ track: window.track, chunk });
  return chunks;
}

export const EMPTY = Symbol("empty");

export class PcmStore {
  constructor(fetchChunk, ctx, { maxChunks = 30, concurrency = 2 } = {}) {
    this.fetchChunk = fetchChunk;
    this.ctx = ctx;
    this.maxChunks = maxChunks;
    this.concurrency = concurrency;
    this.buffers = new Map();
    this.pending = new Set();
    this.wanted = [];
    this.inflight = 0;
    this.cooldown = new Map();
  }

  key(track, chunk) {
    return `${track}:${chunk}`;
  }

  get(track, chunk) {
    return this.buffers.get(this.key(track, chunk)) || null;
  }

  want(windows) {
    const seen = new Set();
    this.wanted = [];
    for (const window of windows) {
      for (const item of chunksFor(window)) {
        const key = this.key(item.track, item.chunk);
        if (!seen.has(key)) {
          seen.add(key);
          this.wanted.push(item);
        }
      }
    }
    for (const key of [...this.buffers.keys()]) {
      if (this.buffers.size <= this.maxChunks) break;
      if (!seen.has(key)) this.buffers.delete(key);
    }
    this.pump();
  }

  pump() {
    while (this.inflight < this.concurrency) {
      const now = Date.now();
      const next = this.wanted.find((item) => {
        const key = this.key(item.track, item.chunk);
        return !this.buffers.has(key) && !this.pending.has(key) && !((this.cooldown.get(key) || 0) > now);
      });
      if (!next) return;
      this.load(next.track, next.chunk);
    }
  }

  async load(track, chunk) {
    const key = this.key(track, chunk);
    this.pending.add(key);
    this.inflight++;
    let delay = 0;
    try {
      const result = await this.fetchChunk(track, chunk * CHUNK_FRAMES, CHUNK_FRAMES);
      if (result.samples) {
        this.buffers.set(key, this.toBuffer(result.samples, result.frames));
        this.cooldown.delete(key);
      } else {
        delay = result.status === 503 ? RETRY_MS : FAILURE_COOLDOWN_MS;
      }
    } catch {
      delay = FAILURE_COOLDOWN_MS;
    } finally {
      this.pending.delete(key);
      this.inflight--;
    }
    if (delay > 0) {
      this.cooldown.set(key, Date.now() + delay);
      setTimeout(() => this.pump(), delay);
    }
    this.pump();
  }

  toBuffer(samples, frames) {
    if (frames <= 0) return EMPTY;
    const buffer = this.ctx.createBuffer(2, frames, SAMPLE_RATE);
    const left = new Float32Array(frames);
    const right = new Float32Array(frames);
    for (let frame = 0; frame < frames; frame++) {
      left[frame] = samples[frame * 2] / 32768;
      right[frame] = samples[frame * 2 + 1] / 32768;
    }
    buffer.copyToChannel(left, 0);
    buffer.copyToChannel(right, 1);
    return buffer;
  }
}
