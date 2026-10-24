import { mkdirSync, realpathSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { frameAt, hostUsOf, curve, fadeLevel, beatsBetween, dbToGain } from "../../src/ARIA.Remote/RemoteClient/monitor/timeline.mjs";
import { ClockSync } from "../../src/ARIA.Remote/RemoteClient/monitor/clock.mjs";

const parityDir = new URL("../fixtures/parity/", import.meta.url);

const voice = (over = {}) => ({
  id: 1,
  epoch: 0,
  track: "1db4df96-26fe-4f49-a954-fcacd871e9c0",
  kind: "entry",
  fallback: false,
  gainDb: 0,
  anchorUs: 1_000_000,
  anchorFrame: 96000,
  playing: true,
  endFrame: null,
  fade: null,
  ...over,
});

const fade = (over = {}) => ({
  from: 0,
  to: 1,
  progress: 0,
  frames: 48000,
  curve: "Linear",
  stopAtEnd: false,
  ...over,
});

const grid = (over = {}) => ({ voice: 1, bpm: 120, beatsPerBar: 4, firstBeatFrame: 0, ...over });

const normalise = (value) => {
  if (typeof value === "number") {
    if (!Number.isFinite(value)) throw new Error(`non-finite number in parity vector: ${value}`);
    return Object.is(value, -0) ? 0 : value;
  }
  if (Array.isArray(value)) return value.map(normalise);
  if (value !== null && typeof value === "object") {
    return Object.fromEntries(Object.entries(value).map(([key, inner]) => [key, normalise(inner)]));
  }
  return value;
};

const vector = (fn, impl, ...args) => normalise({ fn, args, expected: impl(...args) });

const RATE = 48000;
const BIG_US = 13_000_000_000_000;
const BIG_FRAME = 3_000_000_000;
const NEAR_INT32_FRAME = 2_147_483_000;

const frameAtVectors = () => {
  const v = voice();
  const fadingVoice = voice({ id: 2, epoch: 3, kind: "backing", gainDb: -3, endFrame: 480000, fade: fade({ from: 1, to: 0, progress: 0.25, frames: 38400, curve: "Logarithmic", stopAtEnd: true }) });
  const big = voice({ anchorUs: BIG_US, anchorFrame: BIG_FRAME });
  return [
    vector("frameAt", frameAt, v, 1_500_000, RATE),
    vector("frameAt", frameAt, v, 1_000_000, RATE),
    vector("frameAt", frameAt, v, 500_000, RATE),
    vector("frameAt", frameAt, v, 0, RATE),
    vector("frameAt", frameAt, v, 2_000_000, 44100),
    vector("frameAt", frameAt, v, 2_000_000, 48000.25),
    vector("frameAt", frameAt, v, 1_333_333.5, RATE),
    vector("frameAt", frameAt, voice({ playing: false }), 9_000_000, RATE),
    vector("frameAt", frameAt, voice({ playing: false, anchorFrame: BIG_FRAME }), 9_000_000, RATE),
    vector("frameAt", frameAt, fadingVoice, 3_000_000, RATE),
    vector("frameAt", frameAt, big, BIG_US, RATE),
    vector("frameAt", frameAt, big, BIG_US + 2_500_000, RATE),
    vector("frameAt", frameAt, big, BIG_US - 2_500_000, RATE),
    vector("frameAt", frameAt, big, BIG_US + 123_456.75, RATE),
    vector("frameAt", frameAt, big, BIG_US + 3_600_000_000, RATE),
    vector("frameAt", frameAt, voice({ anchorUs: BIG_US, anchorFrame: NEAR_INT32_FRAME }), BIG_US + 20_000_000, RATE),
  ];
};

const hostUsOfVectors = () => {
  const v = voice();
  const big = voice({ anchorUs: BIG_US, anchorFrame: BIG_FRAME });
  return [
    vector("hostUsOf", hostUsOf, v, 120000, RATE),
    vector("hostUsOf", hostUsOf, v, 96000, RATE),
    vector("hostUsOf", hostUsOf, v, 0, RATE),
    vector("hostUsOf", hostUsOf, v, 123456.789, RATE),
    vector("hostUsOf", hostUsOf, v, 120000, 44100),
    vector("hostUsOf", hostUsOf, v, 120000, 48000.25),
    vector("hostUsOf", hostUsOf, voice({ playing: false }), 120000, RATE),
    vector("hostUsOf", hostUsOf, big, BIG_FRAME, RATE),
    vector("hostUsOf", hostUsOf, big, BIG_FRAME + 480000, RATE),
    vector("hostUsOf", hostUsOf, big, BIG_FRAME - 480000, RATE),
    vector("hostUsOf", hostUsOf, big, BIG_FRAME + 12345.678, RATE),
    vector("hostUsOf", hostUsOf, big, BIG_FRAME + 1_728_000_000, RATE),
    vector("hostUsOf", hostUsOf, voice({ anchorUs: BIG_US, anchorFrame: NEAR_INT32_FRAME }), 2_147_483_648, RATE),
  ];
};

const curveVectors = () => [
  ...["Linear", "Logarithmic", "Exponential", "SCurve"].flatMap((name) =>
    [0, 0.25, 0.5, 1].map((t) => vector("curve", curve, name, t)),
  ),
  vector("curve", curve, "Linear", 0.75),
  vector("curve", curve, "Logarithmic", 0.75),
  vector("curve", curve, "Exponential", 0.75),
  vector("curve", curve, "SCurve", 0.75),
  vector("curve", curve, "Bogus", 0),
  vector("curve", curve, "Bogus", 0.25),
  vector("curve", curve, "Bogus", 0.5),
  vector("curve", curve, "Bogus", 1),
  vector("curve", curve, "linear", 0.25),
  vector("curve", curve, "", 0.5),
];

const fadeLevelVectors = () => {
  const curves = ["Linear", "Logarithmic", "Exponential", "SCurve"];
  const matrix = curves.flatMap((name) =>
    [0, 0.5, 1].flatMap((progress) =>
      [0, 12000].map((since) => vector("fadeLevel", fadeLevel, fade({ curve: name, progress }), since)),
    ),
  );
  return [
    vector("fadeLevel", fadeLevel, null, 10),
    vector("fadeLevel", fadeLevel, null, 0),
    ...matrix,
    vector("fadeLevel", fadeLevel, fade({ from: 1, to: 0, progress: 0.5, frames: 100, curve: "Linear" }), 1000),
    vector("fadeLevel", fadeLevel, fade({ from: 1, to: 0, progress: 0, frames: 38400, curve: "Logarithmic", stopAtEnd: true }), 9600),
    vector("fadeLevel", fadeLevel, fade({ from: 1, to: 0, progress: 0.25, frames: 38400, curve: "SCurve" }), 19200),
    vector("fadeLevel", fadeLevel, fade({ from: 0.3, to: 0.8, progress: 0.2, frames: 24000, curve: "Exponential" }), 1234.5),
    vector("fadeLevel", fadeLevel, fade({ progress: 0.5, frames: 0 }), 0.25),
    vector("fadeLevel", fadeLevel, fade({ progress: 0.5, frames: 0 }), 0),
    vector("fadeLevel", fadeLevel, fade({ progress: 0.1 }), -48000),
    vector("fadeLevel", fadeLevel, fade({ progress: 0.9, curve: "SCurve" }), 48000),
    vector("fadeLevel", fadeLevel, fade({ progress: 0.5, curve: "Bogus" }), 12000),
    vector("fadeLevel", fadeLevel, fade({ from: 1, to: 0, progress: 0.5, curve: "Logarithmic" }), 0),
  ];
};

const beatsVectors = () => {
  const origin = voice({ anchorUs: 0, anchorFrame: 0 });
  const bigVoice = voice({ anchorUs: BIG_US, anchorFrame: BIG_FRAME });
  return [
    vector("beatsBetween", beatsBetween, grid(), origin, 0, 2_100_000, RATE),
    vector("beatsBetween", beatsBetween, grid(), origin, 0, 2_000_000, RATE),
    vector("beatsBetween", beatsBetween, grid(), origin, 500_000, 2_000_000, RATE),
    vector("beatsBetween", beatsBetween, grid(), origin, 0, 10_000_000, RATE),
    vector("beatsBetween", beatsBetween, grid(), origin, 100_000, 200_000, RATE),
    vector("beatsBetween", beatsBetween, grid(), origin, 0, 1_600_000, 44100),
    vector("beatsBetween", beatsBetween, grid(), origin, 0, 1_600_000, 96000),
    vector("beatsBetween", beatsBetween, grid({ bpm: 90.5 }), origin, 0, 8_000_000, RATE),
    vector("beatsBetween", beatsBetween, grid({ bpm: 90.5 }), origin, 1_234_567.5, 4_000_000.25, RATE),
    vector("beatsBetween", beatsBetween, grid({ bpm: 300 }), origin, 0, 1_000_000, RATE),
    vector("beatsBetween", beatsBetween, grid({ beatsPerBar: 3 }), origin, 0, 3_200_000, RATE),
    vector("beatsBetween", beatsBetween, grid({ beatsPerBar: 7 }), origin, 0, 4_200_000, RATE),
    vector("beatsBetween", beatsBetween, grid({ beatsPerBar: 1 }), origin, 0, 2_100_000, RATE),
    vector("beatsBetween", beatsBetween, grid({ firstBeatFrame: 72000 }), origin, 0, 1_600_000, RATE),
    vector("beatsBetween", beatsBetween, grid({ firstBeatFrame: 96000 }), origin, 0, 1_600_000, RATE),
    vector("beatsBetween", beatsBetween, grid({ firstBeatFrame: 96000, beatsPerBar: 3 }), origin, 0, 2_000_000, RATE),
    vector("beatsBetween", beatsBetween, grid({ firstBeatFrame: -24000 }), origin, 0, 3_000_000, RATE),
    vector("beatsBetween", beatsBetween, grid({ firstBeatFrame: -72000 }), origin, 0, 3_000_000, RATE),
    vector("beatsBetween", beatsBetween, grid({ firstBeatFrame: -24000, bpm: 90.5, beatsPerBar: 7 }), origin, 0, 6_000_000, RATE),
    vector("beatsBetween", beatsBetween, grid(), voice({ anchorUs: 1_000_000, anchorFrame: 0 }), 0, 1_800_000, RATE),
    vector("beatsBetween", beatsBetween, grid(), voice({ anchorUs: 0, anchorFrame: 0, endFrame: 48000 }), 0, 3_000_000, RATE),
    vector("beatsBetween", beatsBetween, grid(), voice({ anchorUs: 0, anchorFrame: 0, endFrame: 24000 }), 0, 3_000_000, RATE),
    vector("beatsBetween", beatsBetween, grid(), voice({ anchorUs: 0, anchorFrame: 0, endFrame: 100_000 }), 0, 1_000_000, RATE),
    vector("beatsBetween", beatsBetween, grid(), voice({ anchorUs: 0, anchorFrame: 20000, endFrame: 10000 }), 0, 1_000_000, RATE),
    vector("beatsBetween", beatsBetween, grid(), voice({ anchorUs: 0, anchorFrame: 0, endFrame: 0 }), 0, 1_000_000, RATE),
    vector("beatsBetween", beatsBetween, grid(), voice({ anchorUs: 0, anchorFrame: 0, fade: fade({ from: 1, to: 0, progress: 0.5, curve: "SCurve", stopAtEnd: true }) }), 0, 1_100_000, RATE),
    vector("beatsBetween", beatsBetween, grid(), voice({ anchorUs: 0, anchorFrame: 0, playing: false }), 0, 2_000_000, RATE),
    vector("beatsBetween", beatsBetween, grid({ bpm: 0 }), origin, 0, 2_000_000, RATE),
    vector("beatsBetween", beatsBetween, grid({ bpm: -120 }), origin, 0, 2_000_000, RATE),
    vector("beatsBetween", beatsBetween, grid({ beatsPerBar: 0 }), origin, 0, 2_000_000, RATE),
    vector("beatsBetween", beatsBetween, grid({ beatsPerBar: -4 }), origin, 0, 2_000_000, RATE),
    vector("beatsBetween", beatsBetween, grid(), null, 0, 2_000_000, RATE),
    vector("beatsBetween", beatsBetween, null, origin, 0, 2_000_000, RATE),
    vector("beatsBetween", beatsBetween, null, null, 0, 2_000_000, RATE),
    vector("beatsBetween", beatsBetween, grid(), origin, 1_000_000, 1_000_000, RATE),
    vector("beatsBetween", beatsBetween, grid(), origin, 2_000_000, 1_000_000, RATE),
    vector("beatsBetween", beatsBetween, grid(), bigVoice, BIG_US, BIG_US + 2_100_000, RATE),
    vector("beatsBetween", beatsBetween, grid({ firstBeatFrame: 2_999_988_000 }), bigVoice, BIG_US - 500_000, BIG_US + 3_000_000, RATE),
    vector("beatsBetween", beatsBetween, grid({ bpm: 90.5, beatsPerBar: 7, firstBeatFrame: 2_999_952_000 }), bigVoice, BIG_US, BIG_US + 6_000_000, RATE),
    vector("beatsBetween", beatsBetween, grid({ bpm: 90.5 }), bigVoice, BIG_US + 1_800_000_000, BIG_US + 1_805_000_000, RATE),
    vector("beatsBetween", beatsBetween, grid(), voice({ anchorUs: BIG_US, anchorFrame: NEAR_INT32_FRAME }), BIG_US - 1_000_000, BIG_US + 1_000_000, RATE),
    vector("beatsBetween", beatsBetween, grid({ bpm: 90.5 }), voice({ anchorUs: BIG_US, anchorFrame: NEAR_INT32_FRAME }), BIG_US - 1_000_000, BIG_US + 1_000_000, RATE),
    vector("beatsBetween", beatsBetween, grid({ bpm: 90.5 }), voice({ anchorUs: BIG_US, anchorFrame: NEAR_INT32_FRAME }), BIG_US, BIG_US + 4_000_000, RATE),
  ];
};

const dbToGainVectors = () =>
  [-96, -60, -40, -20, -6, -3, -0.5, 0, 0.5, 3, 6, 12, 20].map((db) => vector("dbToGain", dbToGain, db));

const mulberry32 = (seed) => {
  let state = seed >>> 0;
  return () => {
    state = (state + 0x6d2b79f5) >>> 0;
    let mixed = state;
    mixed = Math.imul(mixed ^ (mixed >>> 15), mixed | 1);
    mixed ^= mixed + Math.imul(mixed ^ (mixed >>> 7), mixed | 61);
    return ((mixed ^ (mixed >>> 14)) >>> 0) / 4294967296;
  };
};

const HOST_BASE_MS = 5_400_000.375;
const DRIFT_PPM = 20;
const OUTLIER_EXTRA_MS = 50;

const round3 = (value) => Math.round(value * 1000) / 1000;
const hostMsAt = (localMs) => HOST_BASE_MS + localMs * (1 + DRIFT_PPM * 1e-6);
const linkNoiseMs = (rand) => (rand() < 0.65 ? rand() * 0.6 : 0.9 + rand() * 3);

const simulatePongs = (rand, { count, startMs, gapMs, oneWayMs, noiseMs = linkNoiseMs, extraBackMs = {} }) => {
  const pongs = [];
  let t0 = startMs;
  for (let index = 0; index < count; index += 1) {
    const noise = noiseMs(rand);
    const share = 0.3 + rand() * 0.4;
    const processUs = 80 + Math.floor(rand() * 220);
    const outMs = oneWayMs(index) + noise * share;
    const backMs = oneWayMs(index) + noise * (1 - share) + (extraBackMs[index] ?? 0);
    const t1Us = Math.round(hostMsAt(t0 + outMs) * 1000);
    const t2Us = t1Us + processUs;
    const t3 = round3(t0 + outMs + processUs / 1000 + backMs);
    pongs.push({ t0, t1Us, t2Us, t3 });
    t0 = round3(t3 + gapMs(index + 1, rand));
  }
  return pongs;
};

const exactPong = (t0, delayMs, offsetMs) => {
  const t3 = t0 + delayMs;
  const hostUs = Math.round(((t0 + t3) / 2 + offsetMs) * 1000);
  return { t0, t1Us: hostUs, t2Us: hostUs, t3 };
};

const clockSequence = (name, pongs) => {
  const clock = new ClockSync();
  const steps = pongs.map(({ t0, t1Us, t2Us, t3 }) => {
    clock.onPong(t0, t1Us, t2Us, t3);
    const localMs = round3(t3 + 10);
    const hostUs = clock.hostUsAt(localMs);
    return normalise({
      pong: { t0, t1Us, t2Us, t3 },
      query: { localMs, hostUs },
      after: {
        offsetAt: clock.offsetAt(localMs),
        ready: clock.ready,
        locked: clock.locked,
        uncertaintyUs: clock.uncertaintyUs,
        hostUsAt: hostUs,
        localMsAt: clock.perfMsAt(hostUs),
      },
    });
  });
  return { name, steps };
};

const driftSequence = () => {
  const rand = mulberry32(0x41524941);
  const pongs = simulatePongs(rand, {
    count: 120,
    startMs: 61_234.5,
    gapMs: (index, next) => (index < 60 ? 230 + next() * 20 : 660 + next() * 80),
    oneWayMs: () => 1.5,
    extraBackMs: { 12: OUTLIER_EXTRA_MS, 45: OUTLIER_EXTRA_MS, 100: OUTLIER_EXTRA_MS },
  });
  return clockSequence("drift120", pongs);
};

const sevenPongsSequence = () => {
  const rand = mulberry32(0x0000a7b1);
  const pongs = simulatePongs(rand, {
    count: 7,
    startMs: 9_000.25,
    gapMs: (_, next) => 480 + next() * 40,
    oneWayMs: () => 2,
  });
  return clockSequence("sevenPongs", pongs);
};

const slowLinkSequence = () => {
  const rand = mulberry32(0x00510177);
  const pongs = simulatePongs(rand, {
    count: 18,
    startMs: 120_000.125,
    gapMs: (_, next) => 900 + next() * 200,
    oneWayMs: (index) => (index < 12 ? 24 + (index % 3) : 18),
  });
  return clockSequence("slowLink", pongs);
};

const stalePongSequence = () => {
  const rand = mulberry32(0x0057a1e0);
  const pongs = simulatePongs(rand, {
    count: 24,
    startMs: 30_000.5,
    gapMs: (_, next) => 280 + next() * 40,
    oneWayMs: () => 3,
    extraBackMs: { 20: 70_000 },
  });
  return clockSequence("stalePong", pongs);
};

const marginTieSequence = () => {
  const offset = 5_000_000;
  const cases = [
    [4, 0],
    [5, 0.2],
    [5.001, 1],
    [4, 0.1],
    [4.5, 0.3],
    [5, 0.4],
    [3, 0.05],
    [4, 0.15],
    [4.0001, 2],
  ];
  return clockSequence(
    "marginTie",
    cases.map(([delayMs, shiftMs], index) => exactPong(1000 + 400 * index, delayMs, offset + shiftMs)),
  );
};

const clockVectors = () => [
  driftSequence(),
  sevenPongsSequence(),
  slowLinkSequence(),
  stalePongSequence(),
  marginTieSequence(),
];

export const buildAll = () => ({
  timeline: [
    ...frameAtVectors(),
    ...hostUsOfVectors(),
    ...curveVectors(),
    ...fadeLevelVectors(),
    ...beatsVectors(),
    ...dbToGainVectors(),
  ],
  clock: clockVectors(),
});

const writeAll = (all) => {
  mkdirSync(parityDir, { recursive: true });
  for (const [name, vectors] of Object.entries(all)) {
    writeFileSync(new URL(`${name}.json`, parityDir), `${JSON.stringify(vectors, null, 2)}\n`);
  }
};

const isEntryScript = process.argv[1] !== undefined && realpathSync(process.argv[1]) === realpathSync(fileURLToPath(import.meta.url));

if (isEntryScript) writeAll(buildAll());
