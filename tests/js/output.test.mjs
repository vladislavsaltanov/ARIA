import { test } from "node:test";
import assert from "node:assert/strict";
import { OutputMap } from "../../src/ARIA.Remote/RemoteClient/monitor/output.mjs";

test("output regression maps perf to ctx", () => {
  let perf = 1000;
  const ctx = {
    currentTime: 0,
    getOutputTimestamp: () => ({ contextTime: (perf - 1000) / 1000 + 0.5, performanceTime: perf }),
  };
  const map = new OutputMap(ctx, () => perf);
  for (; perf <= 9000; perf += 250) map.sample();
  assert.ok(Math.abs(map.ctxTimeAt(10000) - 9.5) < 1e-6);
});

test("output fallback uses latencies", () => {
  const ctx = { currentTime: 2.0, baseLatency: 0.005, outputLatency: 0.02 };
  const map = new OutputMap(ctx, () => 1000);
  map.sample();
  assert.ok(Math.abs(map.ctxTimeAt(1100) - 2.075) < 1e-9);
});

test("output ignores zero timestamps", () => {
  const ctx = { currentTime: 1.0, getOutputTimestamp: () => ({ contextTime: 0, performanceTime: 0 }) };
  const map = new OutputMap(ctx, () => 500);
  map.sample();
  map.sample();
  assert.ok(Math.abs(map.ctxTimeAt(600) - 1.1) < 1e-9);
});

test("output trim shifts earlier", () => {
  const ctx = { currentTime: 2.0 };
  const map = new OutputMap(ctx, () => 1000);
  map.trimMs = 40;
  assert.ok(Math.abs(map.ctxTimeAt(1000) - 1.96) < 1e-9);
});
