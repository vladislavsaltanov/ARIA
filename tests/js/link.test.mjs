import { test } from "node:test";
import assert from "node:assert/strict";
import { attemptSignIn, isStale, nextDelay, STALE_MS } from "../../src/ARIA.Remote/RemoteClient/monitor/link.mjs";

const pairing = { identifier: "drums", password: "secret" };

test("network failure retries and never shows the login screen", async () => {
  const outcome = await attemptSignIn(pairing, async () => ({ offline: true }));
  assert.deepEqual(outcome, { state: "retry" });
});

test("thrown authenticate counts as a network failure", async () => {
  const outcome = await attemptSignIn(pairing, async () => {
    throw new Error("Failed to fetch");
  });
  assert.deepEqual(outcome, { state: "retry" });
});

test("rejected credentials show the login screen with an error", async () => {
  const outcome = await attemptSignIn(pairing, async () => ({ denied: true }));
  assert.deepEqual(outcome, { state: "login", error: true });
});

test("no stored pairing shows the login screen without an error", async () => {
  const outcome = await attemptSignIn(null, async () => assert.fail("must not be called"));
  assert.deepEqual(outcome, { state: "login", error: false });
});

test("accepted credentials return the token", async () => {
  const outcome = await attemptSignIn(pairing, async (id, password) => {
    assert.equal(id, "drums");
    assert.equal(password, "secret");
    return { token: "abc" };
  });
  assert.deepEqual(outcome, { state: "ok", token: "abc" });
});

test("backoff doubles from 500 ms up to 5 s", () => {
  let delay = 500;
  const seen = [];
  for (let i = 0; i < 6; i++) {
    delay = nextDelay(delay);
    seen.push(delay);
  }
  assert.deepEqual(seen, [1000, 2000, 4000, 5000, 5000, 5000]);
});

test("a connected socket that stops hearing the host goes stale", () => {
  assert.equal(isStale(true, 10_000 + STALE_MS - 1, 10_000), false);
  assert.equal(isStale(true, 10_000 + STALE_MS + 1, 10_000), true);
  assert.equal(isStale(false, 99_999, 10_000), false);
});
