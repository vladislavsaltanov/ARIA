const WINDOW_SAMPLES = 32;

export class OutputMap {
  constructor(ctx, now = () => performance.now()) {
    this.ctx = ctx;
    this.now = now;
    this.samples = [];
    this.trimMs = 0;
  }

  sample() {
    const stamp = typeof this.ctx.getOutputTimestamp === "function" ? this.ctx.getOutputTimestamp() : null;
    if (stamp && stamp.contextTime > 0 && stamp.performanceTime > 0) {
      this.samples.push({ ctx: stamp.contextTime, perf: stamp.performanceTime });
      if (this.samples.length > WINDOW_SAMPLES) this.samples.shift();
    }
  }

  reset() {
    this.samples = [];
  }

  get latencyMs() {
    return ((this.ctx.baseLatency || 0) + (this.ctx.outputLatency || 0)) * 1000;
  }

  ctxTimeAt(perfMs) {
    let time;
    if (this.samples.length >= 2) {
      const count = this.samples.length;
      const meanPerf = this.samples.reduce((sum, sample) => sum + sample.perf, 0) / count;
      const meanCtx = this.samples.reduce((sum, sample) => sum + sample.ctx, 0) / count;
      let covariance = 0;
      let variance = 0;
      for (const sample of this.samples) {
        const dx = sample.perf - meanPerf;
        covariance += dx * (sample.ctx - meanCtx);
        variance += dx * dx;
      }
      const slope = variance > 0 ? covariance / variance : 0.001;
      time = meanCtx + slope * (perfMs - meanPerf);
    } else {
      time = this.ctx.currentTime + (perfMs - this.now()) / 1000 - this.latencyMs / 1000;
    }
    return time - this.trimMs / 1000;
  }
}
