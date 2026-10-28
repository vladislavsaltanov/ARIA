import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import { buildAll } from "./gen-parity-vectors.mjs";

const parityDir = new URL("../fixtures/parity/", import.meta.url);
const all = buildAll();

for (const [name, vectors] of Object.entries(all)) {
  test(`committed ${name} parity vectors match the JS implementation`, () => {
    const committed = JSON.parse(readFileSync(new URL(`${name}.json`, parityDir), "utf8"));
    assert.deepEqual(vectors, committed);
  });
}

test("every committed parity file has a generator entry", () => {
  const committed = readdirSync(parityDir)
    .filter((file) => file.endsWith(".json"))
    .map((file) => file.slice(0, -".json".length))
    .sort();
  assert.deepEqual(committed, Object.keys(all).sort());
});

test("parity vectors are deterministic", () => {
  assert.deepEqual(buildAll(), all);
});
