import { test } from "node:test";
import assert from "node:assert/strict";
import { clickSamples } from "../../src/ARIA.Remote/RemoteClient/monitor/click.mjs";

test("click samples length and decay", () => {
  const accent = clickSamples(48000, 2000, 1.0);
  assert.equal(accent.length, 1920);
  assert.equal(clickSamples(44100, 1000, 0.5).length, 1764);
  const expected = Math.sin((2 * Math.PI * 2000 * 7) / 48000) * Math.exp(-7 / 480);
  assert.ok(Math.abs(accent[7] - expected) < 1e-6);
  const head = Math.max(...accent.slice(0, 100).map(Math.abs));
  const tail = Math.max(...accent.slice(1800).map(Math.abs));
  assert.ok(tail < head / 20);
});
