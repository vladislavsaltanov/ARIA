const WINDOW_SAMPLES = 64;
const WINDOW_MS = 30000;
const REGRESSION_SPAN_MS = 5000;
const GOOD_MARGIN_MS = 1;
const LOCK_SAMPLES = 8;
const LOCK_MAX_DELAY_MS = 15;

export class ClockSync {
  constructor() {
    this.samples = [];
    this.meanLocal = 0;
    this.meanOffset = 0;
    this.slope = 0;
    this.minDelay = Infinity;
    this.goodCount = 0;
  }

  onPong(t0, t1Us, t2Us, t3) {
    const t1 = t1Us / 1000;
    const t2 = t2Us / 1000;
    const delay = t3 - t0 - (t2 - t1);
    const offset = (t1 - t0 + (t2 - t3)) / 2;
    this.samples.push({ delay, offset, local: (t0 + t3) / 2 });
    if (this.samples.length > WINDOW_SAMPLES) this.samples.shift();
    this.fit(t3);
  }

  fit(now) {
    const recent = this.samples.filter((sample) => now - sample.local <= WINDOW_MS);
    if (recent.length === 0) return;
    const minDelay = Math.min(...recent.map((sample) => sample.delay));
    const good = recent.filter((sample) => sample.delay <= minDelay + GOOD_MARGIN_MS);
    const count = good.length;
    const meanLocal = good.reduce((sum, sample) => sum + sample.local, 0) / count;
    const meanOffset = good.reduce((sum, sample) => sum + sample.offset, 0) / count;
    let slope = 0;
    if (count >= 2 && good[count - 1].local - good[0].local >= REGRESSION_SPAN_MS) {
      let covariance = 0;
      let variance = 0;
      for (const sample of good) {
        const dx = sample.local - meanLocal;
        covariance += dx * (sample.offset - meanOffset);
        variance += dx * dx;
      }
      slope = variance > 0 ? covariance / variance : 0;
    }
    this.minDelay = minDelay;
    this.goodCount = count;
    this.meanLocal = meanLocal;
    this.meanOffset = meanOffset;
    this.slope = slope;
  }

  offsetAt(local) {
    return this.meanOffset + this.slope * (local - this.meanLocal);
  }

  get ready() {
    return this.goodCount > 0;
  }

  get locked() {
    return this.goodCount >= LOCK_SAMPLES && this.minDelay < LOCK_MAX_DELAY_MS;
  }

  get uncertaintyUs() {
    return Number.isFinite(this.minDelay) ? Math.round((this.minDelay / 2) * 1000) : Infinity;
  }

  hostUsAt(perfMs) {
    return (perfMs + this.offsetAt(perfMs)) * 1000;
  }

  perfMsAt(hostUs) {
    const host = hostUs / 1000;
    let local = host - this.offsetAt(host);
    local = host - this.offsetAt(local);
    return local;
  }
}
