// Fake but internally consistent data for the task-score prototype (METRICS 6.9 / 6.11 shapes).
// Dimension maxima: V 40, C 25, R 15, E 10, K 10. A score is the sum of its dims.
window.TS_DATA = (function () {
  const SOURCES = {
    codex: { key: 'source.codex', color: '#34D399' },
    claudeCode: { key: 'source.claudeCode', color: '#FF8A65' },
    claudeDesktop: { key: 'source.claudeDesktop', color: '#B39DFF' },
    omp: { key: 'source.omp', color: '#84CC16' },
    pi: { key: 'source.pi', color: '#A855F7' },
    openclaw: { key: 'source.openclaw', color: '#DE2E1F' },
    deepseek: { key: 'source.deepseek', color: '#4D6BFE' },
  };
  const SOURCE_ORDER = ['codex', 'claudeCode', 'claudeDesktop', 'omp', 'pi', 'openclaw'];

  const DIMS = [
    { id: 'V', max: 40, key: 'task.dim.verify' },
    { id: 'C', max: 25, key: 'task.dim.converge' },
    { id: 'R', max: 15, key: 'task.dim.recover' },
    { id: 'E', max: 10, key: 'task.dim.efficiency' },
    { id: 'K', max: 10, key: 'task.dim.complete' },
  ];

  const CALIBRATION_N = 40;
  const NOW = 1761637320000;
  const cal = (p, n) => ({ p, n, cal: true });
  const prior = (p, n) => ({ p, n, cal: false });

  // Dashboard order (METRICS 6.7). nova is the top score while rocket is the fastest agent.
  // Predictions: success and stall whenever the task has a score; fix fails only while failing.
  const AGENTS = [
    {
      id: 'rocket', project: 'rocket', source: 'codex', model: 'gpt-6.1-sol', effort: 'xhigh', level: 4, speed: 'fast',
      tps: 204, phase: 'generating', depth: 0, lastLineMs: NOW - 400,
      form: 'live', state: 'failing', score: 41, dims: { V: 0, C: 17, R: 9, E: 7, K: 8 },
      check: { suite: 'cargo test', kind: 'test', fLast: 3, fPeak: 9 },
      predict: { success: cal(0.38, 54), stall: cal(0.41, 54), fixFails: prior(0.52, 23) },
      signals: { looping: 4, oscillations: 2, novelty: 0.46, failed: 5, recovered: 1, actions: 23 },
    },
    {
      id: 'nova', project: 'nova', source: 'claudeCode', model: 'claude-opus-5-5', effort: 'xhigh', level: 4,
      tps: 162, phase: 'generating', depth: 0, subCount: 1, lastLineMs: NOW - 900,
      form: 'live', state: 'passing', score: 86, dims: { V: 40, C: 20, R: 11, E: 7, K: 8 },
      check: { suite: 'swift test', kind: 'test' },
      predict: { success: cal(0.91, 212), stall: prior(0.04, 212) },
      signals: { novelty: 0.78, failed: 2, recovered: 2, actions: 31 },
    },
    {
      id: 'nova-sub', project: 'nova', source: 'claudeCode', model: 'claude-sonnet-5-5', effort: 'medium', level: 2,
      tps: 30, phase: 'generating', depth: 1, parent: 'nova', lastLineMs: NOW - 1200,
      form: 'live', state: 'unverified', score: 9, dims: { E: 9 },
      predict: { success: cal(0.62, 212), stall: prior(0.14, 212) },
      signals: { actions: 6 },
    },
    {
      id: 'quasar', project: 'quasar', source: 'codex', model: 'gpt-6.1-terra', effort: 'high', level: 3,
      tps: 76, phase: 'generating', depth: 0, lastLineMs: NOW - 2100,
      form: 'live', state: 'pending', score: 34, dims: { V: 0, C: 18, R: 10, E: 6, K: 0 }, vHeld: 40,
      check: { suite: 'pytest', kind: 'test' },
      predict: { success: cal(0.71, 54), stall: cal(0.12, 54) },
      signals: { repeating: 3, novelty: 0.61, failed: 3, recovered: 3, actions: 18 },
    },
    {
      id: 'tihu', project: 'tihu', source: 'deepseek', model: 'deepseek-v4-pro', lastLineMs: NOW - 300,
      tps: 60, phase: 'generating', depth: 0, form: 'none', state: 'none', score: null, unsupported: true,
    },
    {
      id: 'lumen', project: 'lumen', source: 'omp', model: 'claude-opus-5-5', effort: 'medium', level: 2,
      tps: 0, phase: 'tools', depth: 0, lastLineMs: NOW - 5000, form: 'none', state: 'none', score: null, dims: {},
      signals: { actions: 5 },
    },
    {
      id: 'orbit', project: 'orbit', source: 'claudeDesktop', model: 'claude-fable-5-1',
      tps: 0, phase: 'idle', depth: 0, lastLineMs: NOW - 240000,
      form: 'resting', state: 'passing', score: 72, dims: { V: 40, C: 14, R: 8, E: 6, K: 4 },
      check: { suite: 'npm test', kind: 'test' },
      predict: { success: prior(0.88, 31), stall: prior(0.07, 31) },
      signals: { novelty: 0.69, failed: 2, recovered: 2, actions: 27 },
    },
    {
      // Card gallery only: failed, then edited (state failing, V 0, K 0), every model still on its prior.
      id: 'vega', project: 'vega', source: 'pi', model: 'gpt-6.1-terra', effort: 'high', level: 3, galleryOnly: true,
      tps: 48, phase: 'generating', depth: 0, lastLineMs: NOW - 700,
      form: 'live', state: 'failing', score: 25, dims: { V: 0, C: 14, R: 5, E: 6, K: 0 },
      check: { suite: 'go test', kind: 'test', fLast: 2, fPeak: 5, edited: true },
      predict: { success: prior(0.44, 12), stall: prior(0.18, 12), fixFails: prior(0.35, 9) },
      signals: { oscillations: 1, novelty: 0.71, failed: 3, recovered: 1, actions: 14 },
    },
  ];

  // Climb starts from "before" (nova still unverified); Drop and Loop leave nova lower, each for its own reason.
  const SCENARIOS = {
    final: {},
    before: {
      nova: { state: 'unverified', score: 7, dims: { E: 7 }, check: null,
        predict: { success: cal(0.58, 212), stall: prior(0.15, 212) } },
    },
    drop: {
      nova: { state: 'pending', score: 38, dims: { V: 0, C: 20, R: 11, E: 7, K: 0 }, vHeld: 40,
        predict: { success: cal(0.84, 212), stall: prior(0.06, 212) } },
    },
    loop: {
      nova: { score: 81, dims: { V: 40, C: 20, R: 11, E: 2, K: 8 },
        predict: { success: cal(0.86, 212), stall: prior(0.23, 212) },
        signals: { looping: 3, novelty: 0.31, failed: 3, recovered: 2, actions: 38 } },
    },
  };

  // Gallery orbs and menu bar states (each an independent set).
  const READOUTS = [
    { id: 'passing', label: 'task.state.passing', score: 86, state: 'passing', form: 'live', think: 128, n: 4 },
    { id: 'unverified', label: 'task.state.unverified', score: 9, state: 'unverified', form: 'live', think: 64, n: 2 },
    { id: 'pending', label: 'task.state.pending', score: 34, state: 'pending', form: 'live', think: 41, n: 3 },
    { id: 'resting', label: 'phase.idle', score: 72, state: 'passing', form: 'resting', think: 0, n: 2,
      note: '淡色保留到 agent 離開清單' },
    { id: 'none', label: 'task.state.none', score: null, state: 'none', form: 'none', think: 22, n: 0 },
  ];

  // Per (source, model) task records, summarised. Scores are generated inside their 10-point bins; the stats module
  // derives every shown number (median, quartiles, rates, means) from these, so the views always agree.
  const RECORDS = [
    { source: 'codex', model: 'gpt-6.1-sol', tasks: 74, bins: [1, 1, 2, 3, 4, 5, 8, 12, 12, 10], success: 47, stalled: 7,
      fixFailures: 11, fixAttempts: 52, recoverAttempts: 34, recovered: 19, passes: 50, passCenterMs: 280000 },
    { source: 'codex', model: 'gpt-6.1-terra', tasks: 54, bins: [1, 2, 2, 3, 4, 5, 6, 7, 5, 3], success: 28, stalled: 6,
      fixFailures: 12, fixAttempts: 41, recoverAttempts: 27, recovered: 12, passes: 30, passCenterMs: 430000 },
    { source: 'claudeCode', model: 'claude-opus-5-5', tasks: 132, bins: [2, 3, 5, 7, 9, 11, 17, 20, 19, 16], success: 78,
      stalled: 18, fixFailures: 30, fixAttempts: 101, recoverAttempts: 42, recovered: 33, passes: 84, passCenterMs: 170000 },
    { source: 'claudeCode', model: 'claude-sonnet-5-5', tasks: 82, bins: [3, 4, 5, 6, 7, 8, 9, 9, 6, 5], success: 40,
      stalled: 16, fixFailures: 24, fixAttempts: 66, recoverAttempts: 30, recovered: 19, passes: 46, passCenterMs: 240000 },
    { source: 'claudeDesktop', model: 'claude-fable-5-1', tasks: 9, bins: [0, 0, 0, 1, 1, 0, 2, 1, 1, 0], success: 4,
      stalled: 1, fixFailures: 2, fixAttempts: 6, recoverAttempts: 3, recovered: 2, passes: 4, passCenterMs: 350000 },
    { source: 'omp', model: 'claude-opus-5-5', tasks: 5, bins: [0, 0, 0, 1, 0, 1, 1, 1, 0, 0], success: 2, stalled: 1,
      fixFailures: 1, fixAttempts: 4, recoverAttempts: 2, recovered: 1, passes: 3, passCenterMs: 500000 },
  ];

  return {
    SOURCES, SOURCE_ORDER, DIMS, AGENTS, SCENARIOS, READOUTS, RECORDS, CALIBRATION_N,
    HISTORY: { range: '2026/9/29 – 10/28 · 30', lowSample: 10 },
    THINK_TOTAL: 128, HERO_TPS: 532, HEAT: 0.45,
  };
})();
