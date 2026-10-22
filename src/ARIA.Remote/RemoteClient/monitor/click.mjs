import { beatsBetween } from "./timeline.mjs";

const BURST_SECONDS = 0.04;
const HORIZON_MS = 300;

export function clickSamples(sampleRate, frequency, amplitude) {
  const length = Math.round(BURST_SECONDS * sampleRate);
  const decay = length / 4;
  const samples = new Float32Array(length);
  for (let index = 0; index < length; index++) {
    samples[index] = amplitude * Math.sin((2 * Math.PI * frequency * index) / sampleRate) * Math.exp(-index / decay);
  }
  return samples;
}

function toBuffer(ctx, samples) {
  const buffer = ctx.createBuffer(1, samples.length, ctx.sampleRate);
  buffer.copyToChannel(samples, 0);
  return buffer;
}

export class ClickScheduler {
  constructor(ctx, output, clock, destination, ctxOfHost) {
    this.ctx = ctx;
    this.output = output;
    this.clock = clock;
    this.ctxOfHost = ctxOfHost;
    this.bus = ctx.createGain();
    this.bus.connect(destination);
    this.accent = toBuffer(ctx, clickSamples(ctx.sampleRate, 2000, 1.0));
    this.beat = toBuffer(ctx, clickSamples(ctx.sampleRate, 1000, 0.5));
    this.timeline = null;
    this.signature = "";
    this.scheduled = new Map();
    this.test = false;
  }

  setLevel(gain) {
    this.bus.gain.setTargetAtTime(gain, this.ctx.currentTime, 0.02);
  }

  setTest(enabled) {
    if (this.test !== enabled) {
      this.test = enabled;
      this.cancel();
    }
  }

  update(timeline) {
    this.timeline = timeline;
    const signature = this.signatureOf(timeline);
    if (signature !== this.signature) {
      this.signature = signature;
      this.cancel();
    }
  }

  signatureOf(timeline) {
    if (this.test) return timeline.test ? `test|${timeline.test.anchorUs}|${timeline.test.intervalUs}` : "test";
    const grid = timeline.grid;
    if (!grid) return "none";
    const voice = timeline.voices.find((candidate) => candidate.id === grid.voice);
    return [grid.voice, grid.bpm, grid.beatsPerBar, grid.firstBeatFrame, voice ? voice.epoch : "", voice ? voice.playing : "", voice ? voice.endFrame : ""].join("|");
  }

  cancel() {
    for (const node of this.scheduled.values()) {
      try {
        node.stop();
      } catch {}
    }
    this.scheduled.clear();
  }

  tick(perfNow) {
    const timeline = this.timeline;
    if (!timeline || !this.clock.ready) return;
    const hostNow = this.clock.hostUsAt(perfNow);
    const hostEnd = hostNow + (HORIZON_MS + this.output.trimMs + this.output.latencyMs) * 1000;
    const due = this.test ? this.testClicks(timeline, hostNow, hostEnd) : this.beats(timeline, hostNow, hostEnd);
    for (const click of due) {
      const key = `${this.signature}#${click.index}`;
      if (this.scheduled.has(key)) continue;
      const when = this.ctxOfHost(click.hostUs);
      if (when < this.ctx.currentTime) continue;
      const node = this.ctx.createBufferSource();
      node.buffer = click.accent ? this.accent : this.beat;
      node.connect(this.bus);
      node.start(when);
      node.onended = () => this.scheduled.delete(key);
      this.scheduled.set(key, node);
    }
  }

  beats(timeline, hostNow, hostEnd) {
    const grid = timeline.grid;
    if (!grid) return [];
    const voice = timeline.voices.find((candidate) => candidate.id === grid.voice);
    return beatsBetween(grid, voice, hostNow, hostEnd, timeline.rate);
  }

  testClicks(timeline, hostNow, hostEnd) {
    const test = timeline.test;
    if (!test || !(test.intervalUs > 0)) return [];
    const clicks = [];
    const first = Math.ceil((hostNow - test.anchorUs) / test.intervalUs);
    for (let index = first; test.anchorUs + index * test.intervalUs < hostEnd; index++) {
      clicks.push({ hostUs: test.anchorUs + index * test.intervalUs, index, accent: true });
    }
    return clicks;
  }
}
