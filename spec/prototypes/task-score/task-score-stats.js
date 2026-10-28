// Comparison statistics for the prototype (METRICS 6.11 values, DESIGN §12 quartile and bin rules).
window.TS_STATS = (function () {
  'use strict';

  // Deterministic unrounded scores spread inside each 10-point bin; the last bin is [90, 100].
  function scoresOf(rec) {
    const out = [];
    rec.bins.forEach((count, b) => {
      for (let i = 0; i < count; i++) {
        const span = b === 9 ? 10 : 9.98;
        out.push(b * 10 + ((i + 0.5) / count) * span);
      }
    });
    return out;
  }

  // Deterministic first-pass durations around the record's centre (golden-ratio spread).
  function passTimesOf(rec) {
    const out = [];
    for (let i = 0; i < rec.passes; i++) {
      const f = (i * 0.6180339887) % 1;
      out.push(Math.round(rec.passCenterMs * (0.35 + 1.3 * f)));
    }
    return out;
  }

  // Linear interpolation at h = (n − 1) p over sorted values; p = 0.5 equals the mean of the two middles.
  function quantile(sorted, p) {
    if (!sorted.length) return null;
    const h = (sorted.length - 1) * p;
    const lo = Math.floor(h);
    const hi = Math.ceil(h);
    return sorted[lo] + (sorted[hi] - sorted[lo]) * (h - lo);
  }

  const binOf = (s) => (s >= 90 ? 9 : Math.floor(s / 10));
  const ratio = (n, d) => (d ? { n, d, v: n / d } : null);

  function summarise(recs) {
    const scores = recs.flatMap(scoresOf).sort((a, b) => a - b);
    const passes = recs.flatMap(passTimesOf).sort((a, b) => a - b);
    const sum = (k) => recs.reduce((s, r) => s + r[k], 0);
    const bins = new Array(10).fill(0);
    scores.forEach((s) => { bins[binOf(s)] += 1; });
    return {
      tasks: sum('tasks'),
      scored: scores.length,
      median: quantile(scores, 0.5),
      q1: quantile(scores, 0.25),
      q3: quantile(scores, 0.75),
      bins,
      success: ratio(sum('success'), scores.length),
      stall: ratio(sum('stalled'), sum('tasks')),
      fixFail: ratio(sum('fixFailures'), sum('fixAttempts')),
      attempts: sum('recovered') ? sum('recoverAttempts') / sum('recovered') : null,
      firstPassMs: quantile(passes, 0.5),
      sources: [...new Set(recs.map((r) => r.source))],
    };
  }

  function build(D) {
    const bySource = {};
    D.SOURCE_ORDER.forEach((id) => {
      const recs = D.RECORDS.filter((r) => r.source === id);
      bySource[id] = recs.length ? summarise(recs) : { tasks: 0, scored: 0, bins: new Array(10).fill(0), sources: [id] };
    });
    const ids = [...new Set(D.RECORDS.map((r) => r.model))];
    const byModel = ids.map((model) => {
      const recs = D.RECORDS.filter((r) => r.model === model);
      const s = summarise(recs);
      s.model = model;
      s.sources = D.SOURCE_ORDER.filter((id) => s.sources.includes(id));
      return s;
    }).sort((a, b) => b.tasks - a.tasks || (a.model < b.model ? -1 : a.model > b.model ? 1 : 0));
    return { bySource, byModel };
  }

  return { build, quantile };
})();
