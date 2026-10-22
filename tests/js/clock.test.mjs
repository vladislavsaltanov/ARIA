import { test } from "node:test";
import assert from "node:assert/strict";
import { ClockSync } from "../../src/ARIA.Remote/RemoteClient/monitor/clock.mjs";

function exchange(clock, local, hostOf, outbound, inbound, processing = 0.1) {
  const t0 = local;
  const t1 = hostOf(local + outbound) * 1000;
  const t2 = t1 + processing * 1000;
  const t3 = local + outbound + processing + inbound;
  clock.onPong(t0, t1, t2, t3);
  return t3;
}

test("offset symmetric exact", () => {
  const clock = new ClockSync();
  const hostOf = (local) => local + 5000;
  for (let i = 0; i < 10; i++) exchange(clock, 1000 + i * 250, hostOf, 2, 2);
  assert.ok(Math.abs(clock.hostUsAt(4000) - 9000 * 1000) < 1);
});

test("asymmetric spikes filtered by min delay", () => {
  const clock = new ClockSync();
  const hostOf = (local) => local + 5000;
  let seed = 7;
  const random = () => ((seed = (seed * 16807) % 2147483647) / 2147483647);
  for (let i = 0; i < 60; i++) {
    const clean = i % 10 === 0;
    exchange(clock, 1000 + i * 250, hostOf, 2, clean ? 2 : 2 + 5 + random() * 35);
  }
  assert.ok(Math.abs(clock.hostUsAt(20000) - 25000 * 1000) <= 500);
});

test("drift 100ppm tracked", () => {
  const clock = new ClockSync();
  const hostOf = (local) => local * (1 + 100e-6) + 5000;
  let last = 0;
  for (let i = 0; i <= 60; i++) last = exchange(clock, 1000 + i * 1000, hostOf, 3, 3);
  assert.ok(Math.abs(clock.hostUsAt(last) - hostOf(last) * 1000) <= 300);
});

test("locked after 8 good samples", () => {
  const clock = new ClockSync();
  const hostOf = (local) => local + 100;
  for (let i = 0; i < 7; i++) exchange(clock, 1000 + i * 250, hostOf, 2, 2);
  assert.equal(clock.locked, false);
  exchange(clock, 3000, hostOf, 2, 2);
  assert.equal(clock.locked, true);
  assert.ok(clock.uncertaintyUs > 0 && clock.uncertaintyUs < 3000);
});

test("not locked when min delay too high", () => {
  const clock = new ClockSync();
  for (let i = 0; i < 12; i++) exchange(clock, 1000 + i * 250, (local) => local, 25, 25);
  assert.equal(clock.locked, false);
});

test("perfMsAt inverts hostUsAt", () => {
  const clock = new ClockSync();
  const hostOf = (local) => local * (1 + 50e-6) + 1234;
  for (let i = 0; i <= 20; i++) exchange(clock, 1000 + i * 500, hostOf, 2, 2);
  const perf = 8000.25;
  assert.ok(Math.abs(clock.perfMsAt(clock.hostUsAt(perf)) - perf) < 0.001);
});

function wifi(clock, count, intervalMs) {
  let seed = 7;
  const random = () => {
    seed = (seed * 1664525 + 1013904223) % 4294967296;
    return seed / 4294967296;
  };
  const hostOf = (local) => local + 5000;
  let last = 0;
  for (let i = 0; i < count; i++) {
    last = exchange(clock, 1000 + i * intervalMs, hostOf, 3 + random() * 12, 3 + random() * 12);
  }
  return last;
}

test("locked on a jittery wifi link at fast ping rate", () => {
  const clock = new ClockSync();
  wifi(clock, 20, 250);
  assert.equal(clock.locked, true);
});

test("stays locked on a jittery wifi link at slow ping rate", () => {
  const clock = new ClockSync();
  wifi(clock, 40, 1000);
  assert.equal(clock.locked, true);
});

test("jittery wifi offset stays within the reported uncertainty", () => {
  const clock = new ClockSync();
  const last = wifi(clock, 40, 1000);
  const error = Math.abs(clock.hostUsAt(last) - (last + 5000) * 1000);
  assert.ok(error <= clock.uncertaintyUs + 500, `error ${error} us, uncertainty ${clock.uncertaintyUs} us`);
});
