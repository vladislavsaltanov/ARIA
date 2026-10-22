import { test } from "node:test";
import assert from "node:assert/strict";
import { planChunks, driftDecision, curveValues } from "../../src/ARIA.Remote/RemoteClient/monitor/player.mjs";

const voice = (over = {}) => ({ id: 1, epoch: 0, track: "t", anchorUs: 1_000_000_000, anchorFrame: 150000, playing: true, endFrame: null, fade: null, ...over });

test("planChunks mid-chunk join starts at offset", () => {
  const plans = planChunks(voice(), 48000, 1_000_000_000, 1_002_000_000);
  assert.deepEqual(plans[0], { chunk: 1, offset: 54000, frames: 42000, startUs: 1_000_000_000 });
  assert.deepEqual(plans[1], { chunk: 2, offset: 0, frames: 54000, startUs: 1_000_875_000 });
  assert.equal(plans.length, 2);
});

test("planChunks respects endFrame", () => {
  const plans = planChunks(voice({ endFrame: 160000 }), 48000, 1_000_000_000, 1_002_000_000);
  assert.deepEqual(plans, [{ chunk: 1, offset: 54000, frames: 10000, startUs: 1_000_000_000 }]);
});

test("planChunks nothing for paused voice", () => {
  assert.deepEqual(planChunks(voice({ playing: false }), 48000, 1_000_000_000, 1_002_000_000), []);
});

test("drift small rate capped at 500ppm", () => {
  assert.deepEqual(driftDecision(4, 2), { reanchor: false, rate: 1.0005 });
  assert.deepEqual(driftDecision(0.5, 2), { reanchor: false, rate: 1.000125 });
  assert.deepEqual(driftDecision(-0.4, 2), { reanchor: false, rate: 0.9999 });
});

test("drift over 5ms reanchors", () => {
  assert.deepEqual(driftDecision(5.5, 2), { reanchor: true });
  assert.deepEqual(driftDecision(-6, 2), { reanchor: true });
});

test("curveValues spans from progress to end", () => {
  const values = curveValues("Exponential", 1, 0, 0.5, 3);
  assert.ok(Math.abs(values[0] - 0.75) < 1e-6);
  assert.ok(Math.abs(values[1] - (1 - 0.5625)) < 1e-6);
  assert.ok(Math.abs(values[2] - 0) < 1e-6);
});
