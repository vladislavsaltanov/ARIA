import { test } from "node:test";
import assert from "node:assert/strict";
import { frameAt, hostUsOf, fadeLevel, beatsBetween, structuralKey } from "../../src/ARIA.Remote/RemoteClient/monitor/timeline.mjs";

const voice = (over = {}) => ({ id: 1, epoch: 0, track: "t", anchorUs: 1_000_000, anchorFrame: 96000, playing: true, endFrame: null, fade: null, ...over });

test("frameAt and hostUsOf are inverse", () => {
  const v = voice();
  assert.equal(frameAt(v, 1_500_000, 48000), 96000 + 24000);
  assert.equal(hostUsOf(v, 120000, 48000), 1_500_000);
});

test("paused voice frozen", () => {
  assert.equal(frameAt(voice({ playing: false }), 9_000_000, 48000), 96000);
});

test("fade levels match FadeCurves", () => {
  const expected = {
    Linear: [0.25, 0.5, 0.75],
    Logarithmic: [0.5, Math.SQRT1_2, Math.sqrt(0.75)],
    Exponential: [0.0625, 0.25, 0.5625],
    SCurve: [0.15625, 0.5, 0.84375],
  };
  for (const [curve, levels] of Object.entries(expected)) {
    [0.25, 0.5, 0.75].forEach((t, i) => {
      const level = fadeLevel({ from: 0, to: 1, progress: 0, frames: 1000, curve, stopAtEnd: false }, t * 1000);
      assert.ok(Math.abs(level - levels[i]) < 1e-12, `${curve} at ${t}: ${level}`);
    });
  }
  assert.equal(fadeLevel(null, 10), 1);
  assert.equal(fadeLevel({ from: 1, to: 0, progress: 0.5, frames: 100, curve: "Linear" }, 1000), 0);
});

test("beats negative k before first beat counted", () => {
  const grid = { voice: 1, bpm: 120, beatsPerBar: 4, firstBeatFrame: 72000 };
  const v = voice({ anchorUs: 0, anchorFrame: 0 });
  const beats = beatsBetween(grid, v, 0, 1_600_000, 48000);
  assert.deepEqual(beats.map((b) => b.index), [-3, -2, -1, 0]);
  assert.deepEqual(beats.map((b) => b.accent), [false, false, false, true]);
  assert.equal(beats[3].hostUs, 1_500_000);
});

test("beats accent negative modulo", () => {
  const grid = { voice: 1, bpm: 60, beatsPerBar: 3, firstBeatFrame: 3 * 48000 };
  const v = voice({ anchorUs: 0, anchorFrame: 0 });
  const beats = beatsBetween(grid, v, 0, 3_500_000, 48000);
  assert.deepEqual(beats.map((b) => [b.index, b.accent]), [[-3, true], [-2, false], [-1, false], [0, true]]);
});

test("beats stop at endFrame and need playing", () => {
  const grid = { voice: 1, bpm: 120, beatsPerBar: 4, firstBeatFrame: 0 };
  const v = voice({ anchorUs: 0, anchorFrame: 0, endFrame: 48000 });
  assert.deepEqual(beatsBetween(grid, v, 0, 5_000_000, 48000).map((b) => b.index), [0, 1]);
  assert.deepEqual(beatsBetween(grid, voice({ playing: false }), 0, 5_000_000, 48000), []);
});

test("beats none without grid", () => {
  assert.deepEqual(beatsBetween(null, voice(), 0, 5_000_000, 48000), []);
});

test("beats bpm change uses new grid", () => {
  const v = voice({ anchorUs: 0, anchorFrame: 0 });
  assert.equal(beatsBetween({ bpm: 120, beatsPerBar: 4, firstBeatFrame: 0 }, v, 0, 2_000_000, 48000).length, 4);
  assert.equal(beatsBetween({ bpm: 90, beatsPerBar: 4, firstBeatFrame: 0 }, v, 0, 2_000_000, 48000).length, 3);
});

test("structural key ignores anchor shift but not epoch or fade target", () => {
  const base = voice({ fade: { from: 1, to: 0, progress: 0.1, frames: 480, curve: "Linear", stopAtEnd: true } });
  const shifted = { ...base, anchorUs: base.anchorUs + 10_000, anchorFrame: base.anchorFrame + 480, fade: { ...base.fade, progress: 0.6, from: 0.9 } };
  assert.equal(structuralKey(shifted), structuralKey(base));
  assert.notEqual(structuralKey({ ...base, epoch: 1 }), structuralKey(base));
  assert.notEqual(structuralKey({ ...base, fade: { ...base.fade, to: 0.5 } }), structuralKey(base));
  assert.notEqual(structuralKey({ ...base, endFrame: 1000 }), structuralKey(base));
});
