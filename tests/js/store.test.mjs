import { test } from "node:test";
import assert from "node:assert/strict";
import { windowsFor, chunksFor } from "../../src/ARIA.Remote/RemoteClient/monitor/store.mjs";

test("windows for playing, paused and next", () => {
  const timeline = {
    rate: 48000,
    voices: [
      { track: "a", anchorUs: 0, anchorFrame: 96000, playing: true },
      { track: "b", anchorUs: 0, anchorFrame: 10000, playing: false },
    ],
    deck: { next: { track: "c", startFrame: 24000 } },
  };
  assert.deepEqual(windowsFor(timeline, 1_000_000), [
    { track: "a", from: 120000, to: 1104000 },
    { track: "b", from: 10000, to: 298000 },
    { track: "c", from: 24000, to: 312000 },
  ]);
});

test("windows clamp at zero", () => {
  const timeline = { rate: 48000, voices: [{ track: "a", anchorUs: 0, anchorFrame: 0, playing: true }], deck: {} };
  assert.equal(windowsFor(timeline, 0)[0].from, 0);
});

test("chunks cover window", () => {
  assert.deepEqual(chunksFor({ track: "a", from: 120000, to: 300000 }, 96000), [
    { track: "a", chunk: 1 },
    { track: "a", chunk: 2 },
    { track: "a", chunk: 3 },
  ]);
  assert.deepEqual(chunksFor({ track: "a", from: 0, to: 96000 }, 96000), [{ track: "a", chunk: 0 }]);
});
