// Task score (智力 v2) prototype: renders every surface in both themes from TS_DATA and TS_STRINGS.
(function () {
  'use strict';

  const S = window.TS_STRINGS;
  const D = window.TS_DATA;
  const params = new URLSearchParams(location.search);
  const ui = {
    lang: params.get('lang') || 'zh-Hant',
    view: params.get('theme') || 'both',
    reduce: params.get('reduce') === '1' || matchMedia('(prefers-reduced-motion: reduce)').matches,
    isStatic: params.get('static') === '1',
    only: params.get('section'),
    open: params.get('open'),
    cardFilter: params.get('card'),
    variant: params.get('variant'),
    scenario: D.SCENARIOS[params.get('scenario')] ? params.get('scenario') : 'final',
    openCard: null,
  };

  // ── i18n and formatting ──────────────────────────────────────────────────────────────
  const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

  function raw(key) {
    const e = S[key];
    if (!e) return `⟨${key}⟩`;
    return e[ui.lang] ?? e.en;
  }

  function t(key, ...args) {
    let i = 0;
    return raw(key).replace(/%(%|@|d)/g, (m, c) => (c === '%' ? '%' : String(args[i++])));
  }

  // Like t(), but escapes the text and lets an argument be {html} (inserted as is).
  function th(key, ...args) {
    let i = 0;
    return raw(key).split(/(%%|%@|%d)/).map((part) => {
      if (part === '%%') return '%';
      if (part === '%@' || part === '%d') {
        const a = args[i++];
        return a && typeof a === 'object' ? a.html : esc(a);
      }
      return esc(part);
    }).join('');
  }

  function fmtSpeed(v) {
    if (v === 0) return '0';
    if (v < 10) return v.toFixed(1);
    if (v < 1000) return String(Math.round(v));
    if (v < 10000) return (v / 1000).toFixed(2) + 'k';
    if (v < 100000) return (v / 1000).toFixed(1) + 'k';
    return Math.round(v / 1000) + 'k';
  }
  function fmtProb(p) {
    const v = p * 100;
    if (v < 1) return '<1%';
    if (v > 99) return '>99%';
    return Math.round(v) + '%';
  }
  function probWords(p) {
    const v = p * 100;
    if (v < 1) return t('a11y.task.below', 1);
    if (v > 99) return t('a11y.task.above', 99);
    return Math.round(v) + '%';
  }
  function fmtDur(ms) {
    const s = Math.round(ms / 1000);
    if (s < 60) return t('time.seconds', s);
    const m = Math.round(s / 60);
    if (m < 60) return t('time.minutes', m);
    return t('time.hoursMinutes', Math.floor(m / 60), m % 60);
  }
  const glue = (str) => String(str).replace(/(\d) /g, '$1\u00a0').replace(/ (\d)/g, '\u00a0$1');
  const srcName = (id) => t(D.SOURCES[id].key);
  const srcColor = (id) => D.SOURCES[id].color;

  // ── Heat palette (DESIGN §1) ─────────────────────────────────────────────────────────
  const HEAT = [[0, '#7C8798'], [0.1, '#2DD4BF'], [0.3, '#38BDF8'], [0.48, '#818CF8'], [0.64, '#E879F9'],
    [0.78, '#FB7185'], [0.9, '#FB923C'], [1, '#FACC15']];
  const hex = (c) => [1, 3, 5].map((i) => parseInt(c.slice(i, i + 2), 16));
  function heatColor(h, darken = 0) {
    h = Math.max(0, Math.min(1, h));
    let k = 1;
    while (k < HEAT.length - 1 && HEAT[k][0] < h) k++;
    const [h0, c0] = HEAT[k - 1];
    const [h1, c1] = HEAT[k];
    const f = (h - h0) / (h1 - h0 || 1);
    const a = hex(c0);
    const b = hex(c1);
    const m = a.map((v, i) => Math.round((v + (b[i] - v) * f) * (1 - darken)));
    return `rgb(${m.join(',')})`;
  }

  // ── Model helpers ────────────────────────────────────────────────────────────────────
  const unproven = (s) => s === 'unverified' || s === 'pending';

  function agents() {
    const over = D.SCENARIOS[ui.scenario] || {};
    return D.AGENTS.map((a) => (over[a.id] ? { ...a, ...over[a.id] } : a));
  }

  const inSets = (list) => list.filter((a) => !a.galleryOnly);

  // The highest live score, else the highest resting one (pale), else none.
  // Ties (METRICS 6.9): the latest lastLineMs, then the smallest id.
  function topOf(list) {
    const scored = inSets(list).filter((a) => a.score != null);
    const live = scored.filter((a) => a.form === 'live');
    const pool = live.length ? live : scored.filter((a) => a.form === 'resting');
    if (!pool.length) return null;
    const ranked = [...pool].sort((a, b) => b.score - a.score || b.lastLineMs - a.lastLineMs || (a.id < b.id ? -1 : 1));
    const a = ranked[0];
    return { agent: a, value: a.score, state: a.state, form: live.length ? 'live' : 'resting', n: pool.length };
  }

  function scoreWords(value, state, form, n) {
    if (value == null) return null;
    let s = String(value);
    if (state === 'unverified') s = t('a11y.task.unverified', s);
    else if (state === 'pending') s = t('a11y.task.pending', s);
    if (form === 'resting') s = t('a11y.task.idle', s);
    if (n >= 2) s = t('a11y.task.top', s, n);
    return s;
  }

  // A score number: `–` for none, the proof mark for unverified / pending. data-k drives the motion rules.
  function numHTML(value, state, { key = '', anim = '', cls = '', pale = false } = {}) {
    if (value == null) return `<span class="${cls} is-quiet" data-k="${key}">–</span>`;
    const pm = unproven(state);
    return `<span class="${cls}${pm ? ' pm' : ''}${pale ? ' is-pale' : ''}" data-k="${key}" data-v="${value}" ` +
      `data-anim="${anim}" data-pm="${pm ? 1 : 0}">${value}</span>`;
  }

  // ── Icons ────────────────────────────────────────────────────────────────────────────
  const I = {
    check: '<svg width="10" height="10" viewBox="0 0 10 10" aria-hidden="true"><circle cx="5" cy="5" r="5" fill="currentColor" opacity=".22"/><path d="M2.8 5.2l1.5 1.5 3-3.3" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round"/></svg>',
    alert: '<svg width="10" height="10" viewBox="0 0 10 10" aria-hidden="true"><circle cx="5" cy="5" r="4.4" fill="none" stroke="currentColor" stroke-width="1.2"/><path d="M5 2.7v2.8" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/><circle cx="5" cy="7.3" r=".75" fill="currentColor"/></svg>',
    ring: '<svg width="10" height="10" viewBox="0 0 10 10" aria-hidden="true"><circle cx="5" cy="5" r="4" fill="none" stroke="currentColor" stroke-width="1.3" stroke-dasharray="0 2.5" stroke-linecap="round"/></svg>',
    clock: '<svg width="10" height="10" viewBox="0 0 10 10" aria-hidden="true"><circle cx="5" cy="5" r="4" fill="none" stroke="currentColor" stroke-width="1.3" stroke-dasharray="0 2.5" stroke-linecap="round"/><path d="M5 3v2.2l1.4.9" fill="none" stroke="currentColor" stroke-width="1.1" stroke-linecap="round"/></svg>',
    loop: '<svg width="12" height="12" viewBox="0 0 12 12" aria-hidden="true"><path d="M2.2 5.2A3.9 3.9 0 0 1 9.3 3.4M9.8 6.8a3.9 3.9 0 0 1-7.1 1.8" fill="none" stroke="currentColor" stroke-width="1.35" stroke-linecap="round"/><path d="M9.8 1.4v2.4H7.4M2.2 10.6V8.2h2.4" fill="none" stroke="currentColor" stroke-width="1.35" stroke-linecap="round" stroke-linejoin="round"/></svg>',
    repeat: '<svg width="12" height="12" viewBox="0 0 12 12" aria-hidden="true"><path d="M2 5.5V5a2 2 0 0 1 2-2h6M8 1l2 2-2 2M10 6.5V7a2 2 0 0 1-2 2H2M4 11L2 9l2-2" fill="none" stroke="currentColor" stroke-width="1.2" stroke-linecap="round" stroke-linejoin="round"/></svg>',
    swap: '<svg width="12" height="12" viewBox="0 0 12 12" aria-hidden="true"><path d="M1.5 4h9M8 1.5L10.5 4 8 6.5M10.5 8h-9M4 5.5L1.5 8 4 10.5" fill="none" stroke="currentColor" stroke-width="1.2" stroke-linecap="round" stroke-linejoin="round"/></svg>',
    list: '<svg width="12" height="12" viewBox="0 0 12 12" aria-hidden="true"><path d="M4.5 3h6M4.5 6h6M4.5 9h6" stroke="currentColor" stroke-width="1.2" stroke-linecap="round"/><circle cx="2" cy="3" r=".8" fill="currentColor"/><circle cx="2" cy="6" r=".8" fill="currentColor"/><circle cx="2" cy="9" r=".8" fill="currentColor"/></svg>',
    hourglass: '<svg width="11" height="11" viewBox="0 0 11 11" aria-hidden="true"><path d="M2.5 1h6M2.5 10h6M3 1c0 3 5 3 5 4.5S3 7 3 10M8 1c0 3-5 3-5 4.5S8 7 8 10" fill="none" stroke="currentColor" stroke-width="1.1" stroke-linecap="round"/></svg>',
    bolt: '<svg width="8" height="10" viewBox="0 0 8 10" aria-hidden="true"><path d="M4.8 0L.6 5.6h2.8L2.6 10l4.8-6H4.5z" fill="currentColor"/></svg>',
    chev: '<svg width="8" height="10" viewBox="0 0 8 10" aria-hidden="true"><path d="M1.5 3.5L4 1l2.5 2.5M1.5 6.5L4 9l2.5-2.5" fill="none" stroke="currentColor" stroke-width="1.2" stroke-linecap="round" stroke-linejoin="round"/></svg>',
    lock: '<svg width="10" height="11" viewBox="0 0 10 11" aria-hidden="true"><rect x="1.5" y="4.6" width="7" height="5.6" rx="1.3" fill="none" stroke="currentColor" stroke-width="1.1"/><path d="M3 4.6V3.3a2 2 0 0 1 4 0v1.3" fill="none" stroke="currentColor" stroke-width="1.1"/></svg>',
    wifi: '<svg width="16" height="12" viewBox="0 0 16 12" aria-hidden="true"><path d="M1 4.2a10.5 10.5 0 0 1 14 0M3.3 6.6a7.2 7.2 0 0 1 9.4 0M5.6 9a3.8 3.8 0 0 1 4.8 0" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/><circle cx="8" cy="10.8" r=".9" fill="currentColor"/></svg>',
    battery: '<svg width="24" height="12" viewBox="0 0 24 12" aria-hidden="true"><rect x=".7" y=".7" width="20" height="10.6" rx="3" fill="none" stroke="currentColor" stroke-opacity=".5"/><rect x="2.4" y="2.4" width="13" height="7.2" rx="1.6" fill="currentColor"/><path d="M22.4 4v4" stroke="currentColor" stroke-opacity=".5" stroke-width="1.4" stroke-linecap="round"/></svg>',
    speaker: '<svg width="14" height="12" viewBox="0 0 14 12" aria-hidden="true"><path d="M1 4.2h2.4L6.6 1.4v9.2L3.4 7.8H1z" fill="currentColor"/><path d="M8.6 3.6a3.4 3.4 0 0 1 0 4.8M10.4 1.8a6 6 0 0 1 0 8.4" fill="none" stroke="currentColor" stroke-width="1.1" stroke-linecap="round"/></svg>',
    caret: '<svg width="10" height="6" viewBox="0 0 10 6" aria-hidden="true"><path d="M1 5l4-4 4 4" fill="none" stroke="currentColor" stroke-width="1.3" stroke-linecap="round" stroke-linejoin="round"/></svg>',
    gaugeMuted: '<svg width="34" height="34" viewBox="0 0 34 34" aria-hidden="true"><path d="M7.2 26.8A13 13 0 1 1 26.8 26.8" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" opacity=".55"/><path d="M17 17l5-6" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"/><circle cx="17" cy="17" r="2.2" fill="currentColor"/></svg>',
  };

  function arcD(cx, cy, r, a0, a1) {
    const rad = (a) => (a * Math.PI) / 180;
    const x0 = cx + r * Math.cos(rad(a0));
    const y0 = cy + r * Math.sin(rad(a0));
    const x1 = cx + r * Math.cos(rad(a1));
    const y1 = cy + r * Math.sin(rad(a1));
    return `M${x0.toFixed(2)} ${y0.toFixed(2)}A${r} ${r} 0 ${a1 - a0 > 180 ? 1 : 0} 1 ${x1.toFixed(2)} ${y1.toFixed(2)}`;
  }

  function gaugeSVG(d, h, theme) {
    const sw = 1.75;
    const r = (d - sw) / 2;
    const c = d / 2;
    const col = heatColor(h, theme === 'light' ? 0.18 : 0);
    return `<svg class="gauge" width="${d}" height="${d}" viewBox="0 0 ${d} ${d}" aria-hidden="true">` +
      `<path d="${arcD(c, c, r, 135, 405)}" fill="none" stroke="currentColor" stroke-opacity=".25" stroke-width="${sw}" stroke-linecap="round"/>` +
      `<path d="${arcD(c, c, r, 135, 135 + 270 * h)}" fill="none" stroke="${col}" stroke-width="${sw}" stroke-linecap="round"/></svg>`;
  }

  // ── Score card ───────────────────────────────────────────────────────────────────────
  function chipHTML(a) {
    if (a.unsupported) return '';
    const map = {
      passing: ['chip-passing', I.check], failing: ['chip-failing', I.alert], unverified: ['chip-neutral', I.ring],
      pending: ['chip-neutral', I.clock], none: ['chip-neutral', I.ring],
    };
    const [cls, icon] = map[a.state];
    return `<span class="chip ${cls}">${icon}${esc(t('task.state.' + a.state))}</span>`;
  }

  function checkName(a) {
    if (!a.check) return null;
    return a.check.suite ? { html: `<span class="suite">${esc(a.check.suite)}</span>` } : t('task.check.' + a.check.kind);
  }

  function statusHTML(a) {
    const ck = checkName(a);
    switch (a.state) {
      case 'none': return th('task.status.none');
      case 'unverified': return th('task.status.unverified');
      case 'failing':
        if (a.check.edited) return th('task.status.failing.edited', ck);
        return a.check.fLast != null ? th('task.status.failing', ck, a.check.fLast, a.check.fPeak) : th('task.status.failing.noCount', ck);
      case 'passing': return a.check.kind === 'test' ? th('task.status.passing', ck) : th('task.status.passing.buildOnly', ck);
      case 'pending': return th('task.status.pending', ck);
      default: return '';
    }
  }

  function compHTML(a) {
    const dims = a.dims || {};
    let left = 0;
    const parts = [];
    D.DIMS.forEach((d) => {
      const ghost = d.id === 'V' && a.state === 'pending' ? a.vHeld || 0 : 0;
      const v = ghost || dims[d.id] || 0;
      if (!v) return;
      parts.push(`<i class="${ghost ? 'ghost' : ''}" style="position:absolute;left:${left}%;width:calc(${v}% - 2px)"></i>`);
      left += v;
    });
    return `<div class="comp" aria-hidden="true">${parts.join('')}</div>`;
  }

  function dimHTML(d, a) {
    const v = (a.dims || {})[d.id];
    const noProof = a.state === 'none' || a.state === 'unverified';
    const label = t(d.key);
    let value;
    let bar;
    let words;
    if (noProof && (d.id !== 'E' || a.state === 'none')) {
      const txt = d.id === 'E' ? '–' : t('task.state.unverified');
      value = `<span class="v is-quiet">${esc(txt)}</span>`;
      bar = '<div class="bar dotted"></div>';
      words = d.id === 'E' ? `${label}: –` : t('a11y.task.unverified', label);
    } else if (d.id === 'V' && a.state === 'pending') {
      value = `<span class="v is-pending">${esc(t('task.state.pending'))}</span>`;
      bar = `<div class="bar"><i class="ghost" style="width:${((a.vHeld || 0) / d.max) * 100}%"></i></div>`;
      words = t('a11y.task.pending', t('a11y.task.dim', label, 0, d.max));
    } else {
      value = `<span class="v">${v}<small> / ${d.max}</small></span>`;
      bar = `<div class="bar"><i style="width:${(v / d.max) * 100}%"></i></div>`;
      words = t('a11y.task.dim', label, v, d.max);
    }
    return `<div class="dim" role="group" aria-label="${esc(words)}"><div class="dim-top"><b>${esc(label)}</b>${value}</div>` +
      `${bar}<p>${esc(t(d.key + '.desc'))}</p></div>`;
  }

  // Each model carries its own flag: calibrated on N tasks, prior while N < 40, or prior although N ≥ 40.
  function flagText(m) {
    if (m.cal) return t('task.predict.cell.calibrated', m.n);
    return m.n < D.CALIBRATION_N ? t('task.predict.cell.prior', m.n, D.CALIBRATION_N) : t('task.predict.cell.priorOnly');
  }

  function predictHTML(a) {
    const p = a.predict;
    if (!p || a.form === 'resting' || a.score == null) return '';
    const keys = ['success', 'stall'].concat(a.state === 'failing' && p.fixFails ? ['fixFails'] : []);
    const cells = keys.map((k) => {
      const m = p[k];
      const base = t('a11y.task.predict.' + k, probWords(m.p));
      const words = m.cal ? t('a11y.task.predict.calibrated', base, m.n) : t('a11y.task.predict.prior', base);
      return `<div role="group" aria-label="${esc(words)}"><div class="p">${fmtProb(m.p)}</div>` +
        `<div class="l">${esc(t('task.predict.' + k))}</div><div class="f${m.cal ? '' : ' is-prior'}">${esc(flagText(m))}</div></div>`;
    }).join('');
    return `<div class="c-sec"><div class="c-sec-t">${esc(t('task.predict.title'))}</div>` +
      `<div class="pred" style="--n:${keys.length}">${cells}</div>` +
      `<div class="c-note">${esc(t('task.predict.note', srcName(a.source), D.CALIBRATION_N))}</div></div>`;
  }

  function signalsHTML(a) {
    const s = a.signals || {};
    const rows = [];
    if (s.looping) rows.push(`<div class="sig warn">${I.loop}<span>${esc(t('work.looping', s.looping))}</span></div>`);
    if (s.repeating) rows.push(`<div class="sig">${I.repeat}<span>${esc(t('work.repeating', s.repeating))}</span></div>`);
    if (s.oscillations) rows.push(`<div class="sig">${I.swap}<span>${esc(t('task.signal.oscillation', s.oscillations))}</span></div>`);
    if (s.actions >= 8) {
      rows.push(`<div class="sig">${I.list}<span>${esc(t('work.summary', Math.round(s.novelty * 100) + '%', s.failed, s.recovered))}</span></div>`);
    }
    if (!rows.length) return '';
    return `<div class="c-sec"><div class="c-sec-t">${esc(t('task.signals.title'))}</div>${rows.join('')}</div>`;
  }

  function factsHTML(a) {
    const parts = [];
    if (a.effort) parts.push(t('effort.tooltip', a.effort));
    if (a.speed) parts.push(t('speed.tooltip', t('speed.value.' + a.speed)));
    if (!parts.length) return '';
    return `<div class="c-facts">${parts.map(esc).join(' · ')}</div>`;
  }

  function cardHTML(a, o = {}) {
    const meta = [a.model, srcName(a.source)].join(' · ');
    const head = `<div class="c-head"><span class="dot" style="background:${srcColor(a.source)}"></span>` +
      `<b>${esc(a.project)}</b>${chipHTML(a)}</div><div class="c-meta">${esc(meta)}</div>`;
    const label = `${a.project}，${t('a11y.task.card')}`;
    if (a.unsupported) {
      return `<div class="card" role="group" aria-label="${esc(label)}">${head}<div class="c-empty">${esc(t('task.unsupported'))}</div>` +
        `${factsHTML(a)}</div>`;
    }
    const eyebrow = o.topN ? t('task.card.top', o.topN) : a.form === 'resting' ? t('phase.idle') : '';
    const pale = a.form === 'resting';
    const big = a.score == null ? '<span class="n is-none">–</span>'
      : numHTML(a.score, a.state, { key: `card-${o.key || a.id}`, anim: 'count', cls: 'n', pale });
    const scoreA11y = a.score == null ? t('intel.none')
      : t('a11y.task.score', scoreWords(a.score, a.state, a.form, o.topN || 0), t('task.state.' + a.state));
    return `<div class="card" role="group" aria-label="${esc(label)}">${head}` +
      `<div class="c-score" role="group" aria-label="${esc(scoreA11y)}">` +
      (eyebrow ? `<div class="c-eyebrow">${esc(eyebrow)}</div>` : '') +
      `<div class="c-big">${big}<span class="of">/100</span></div>` +
      `<div class="c-status">${statusHTML(a)}</div>${compHTML(a)}</div>` +
      `<div class="c-sec"><div class="c-sec-t">${esc(t('task.dims.title'))}</div>${D.DIMS.map((d) => dimHTML(d, a)).join('')}</div>` +
      predictHTML(a) + signalsHTML(a) + factsHTML(a) +
      `<div class="c-foot">${esc(t('task.card.footnote'))}</div></div>`;
  }

  // ── Orb ──────────────────────────────────────────────────────────────────────────────
  function orbHTML(o, theme, uid) {
    const S0 = o.size;
    const tt = Math.max(7, 0.05 * S0);
    const p = 1.25 * tt;
    const c = S0 / 2;
    const single = !(o.think > 0);
    const nearest = c - 0.245 * S0 + 0.095 * S0;
    const intelBase = single ? c - 0.245 * S0 + 0.075 * S0 : nearest - 1.32 * tt;
    const top = (base, size) => (base - 0.855 * size).toFixed(2);
    const r1 = 0.405 * S0;
    const r2 = 0.445 * S0;
    const lit = Math.round(o.heat * 36);
    let segs = '';
    let glow = '';
    for (let i = 0; i < 36; i++) {
      const a = ((135 + (i + 0.5) * 7.5) * Math.PI) / 180;
      const x1 = c + r1 * Math.cos(a);
      const y1 = c + r1 * Math.sin(a);
      const x2 = c + r2 * Math.cos(a);
      const y2 = c + r2 * Math.sin(a);
      const on = i < lit;
      const col = on ? heatColor(i / 36, theme === 'light' ? 0.08 : 0) : 'var(--led-off)';
      const ln = `<line x1="${x1.toFixed(2)}" y1="${y1.toFixed(2)}" x2="${x2.toFixed(2)}" y2="${y2.toFixed(2)}" stroke="${col}" stroke-width="${(0.026 * S0).toFixed(2)}" stroke-linecap="round"/>`;
      segs += ln;
      if (on && theme === 'dark') glow += ln;
    }
    const dots = (o.dots || []).map((col, i, arr) => {
      const w = arr.length * 7;
      return `<circle cx="${(c - w / 2 + 3.5 + i * 7).toFixed(1)}" cy="${(0.875 * S0).toFixed(1)}" r="2" fill="${col}"/>`;
    }).join('');
    const title = heatColor(o.heat, theme === 'light' ? 0.25 : 0);
    const pale = o.form === 'resting';
    const n = o.score == null ? '<span class="n is-quiet">–</span>'
      : numHTML(o.score, o.state, { key: o.key, anim: 'count', cls: 'n' });
    const intel = `<div class="ln ln-intel${pale ? ' is-pale' : ''}" style="top:${top(intelBase, p)}px">` +
      `<span class="glyph">${esc(t('intel.glyph'))}</span>${n}</div>`;
    const think = single ? '' : `<div class="ln ln-think" style="top:${top(nearest, tt)}px"><span class="glyph">` +
      `${esc(t('intel.thinking.glyph'))}</span><span class="n">${fmtSpeed(o.think)}</span><span class="u">tok/s</span></div>`;
    const words = o.score == null ? t('intel.none')
      : single ? t('a11y.orb.intel.only', scoreWords(o.score, o.state, o.form, o.n))
        : t('a11y.orb.intel', scoreWords(o.score, o.state, o.form, o.n), fmtSpeed(o.think));
    return `<div class="orb" role="img" aria-label="${esc(words)}" style="--S:${S0}px;--t:${tt}px;--p:${p}px">` +
      `<svg viewBox="0 0 ${S0} ${S0}" aria-hidden="true"><defs>` +
      `<radialGradient id="g-${uid}" cx="50%" cy="38%" r="62%"><stop offset="0" style="stop-color:var(--orb-g1)"/><stop offset="1" style="stop-color:var(--orb-g2)"/></radialGradient>` +
      `<filter id="f-${uid}" x="-20%" y="-20%" width="140%" height="140%"><feGaussianBlur stdDeviation="${(0.018 * S0).toFixed(2)}"/></filter></defs>` +
      `<circle cx="${c}" cy="${c}" r="${(0.485 * S0).toFixed(2)}" fill="url(#g-${uid})" stroke="var(--orb-rim)"/>` +
      `<g filter="url(#f-${uid})" opacity=".85">${glow}</g>${segs}${dots}</svg>` +
      intel + think +
      `<div class="ln val" style="top:${top(0.62 * S0, 0.25 * S0)}px;font-size:${(0.25 * S0).toFixed(1)}px">${o.tps}</div>` +
      `<div class="ln unit" style="top:${top(0.705 * S0, 0.068 * S0)}px;font-size:${(0.068 * S0).toFixed(1)}px">tok/s</div>` +
      `<div class="ln ttl" style="top:${top(0.79 * S0, 0.074 * S0)}px;font-size:${(0.074 * S0).toFixed(1)}px;color:${title}">${esc(t(o.title))}</div>` +
      '</div>';
  }

  function orbSection(theme) {
    const list = agents();
    const top = topOf(list);
    const live = inSets(list).filter((a) => a.tps > 0);
    const hero = orbHTML({
      size: 148, tps: D.HERO_TPS, heat: D.HEAT, title: 'title.maglev', think: D.THINK_TOTAL, key: 'orb-hero',
      score: top ? top.value : null, state: top ? top.state : 'none', form: top ? top.form : 'none', n: top ? top.n : 0,
      dots: live.map((a) => srcColor(a.source)),
    }, theme, `hero-${theme}`);
    const tip = top ? `<div class="hover-tip">${esc(t('task.tooltip.top', top.value, top.agent.project))}` +
      `<div class="sub">${esc(t('intel.disclaimer'))}</div></div>` : '';
    const gallery = D.READOUTS.map((r) => `<div class="orb-tile">${orbHTML({
      size: 116, tps: r.form === 'resting' ? 0 : 236, heat: r.form === 'resting' ? 0 : 0.28, title: r.form === 'resting' ? 'title.walk' : 'title.sportsCar',
      think: r.think, score: r.score, state: r.state, form: r.form, n: r.n, key: '',
      dots: r.form === 'resting' ? ['#7C8798'] : ['#34D399', '#FF8A65'],
    }, theme, `${r.id}-${theme}`)}<div class="caption"><b>${esc(t(r.label))}</b>${r.note ? `<span class="note">${esc(r.note)}</span>` : ''}</div></div>`).join('');
    return `<div class="stack" style="align-items:center"><div class="orb-tile">${hero}</div>` +
      `<div style="max-width:300px"><div class="caption" style="text-align:left;margin:0">` +
      `${top ? `<b>${esc(top.agent.project)}</b> · ${esc(top.agent.model)} · ${esc(srcName(top.agent.source))}` : ''}</div>${tip}` +
      (top ? `<div class="caption" style="text-align:left">${esc(t('a11y.orb.intel', scoreWords(top.value, top.state, top.form, top.n), fmtSpeed(D.THINK_TOTAL)))}</div>` : '') + '</div></div>' +
      `<div class="stack" style="margin-top:20px;justify-content:space-between">${gallery}</div>`;
  }

  // ── Menu bar and taskbar ────────────────────────────────────────────────────────────
  function rblockHTML(r, key) {
    const single = r.score != null && !(r.think > 0);
    const n = r.score == null ? '<span class="nb is-quiet">–</span>'
      : `<span class="nb">${numHTML(r.score, r.state, { key })}</span>`;
    const think = r.score == null ? '–' : fmtSpeed(r.think);
    return `<span class="rblock${r.form === 'resting' ? ' is-pale' : ''}${single ? ' single' : ''}" aria-hidden="true">` +
      `<span class="row r1"><span class="lb">${esc(t('intel.glyph'))}</span>${n}<span class="ub"></span></span>` +
      `<span class="row r2"><span class="lb">${esc(t('intel.thinking.glyph'))}</span><span class="nb${r.score == null ? ' is-quiet' : ''}">${think}</span><span class="ub">tok/s</span></span>` +
      '<span class="row"><span class="lb ghost"></span><span class="nb ghost">8.88k</span><span class="ub ghost">tok/s</span></span></span>';
  }

  function barWords(r) {
    if (r.score == null) return t('intel.none');
    const w = scoreWords(r.score, r.state, r.form, r.n);
    return r.think > 0 ? t('a11y.orb.intel', w, fmtSpeed(r.think)) : t('a11y.orb.intel.only', w);
  }

  function itemHTML(r, theme, kind, key) {
    const d = kind === 'mac' ? 13 : 14;
    const tps = r.form === 'resting' || r.score == null && r.think === 0 ? 0 : D.HERO_TPS;
    return `<span class="item" role="button" tabindex="0" aria-label="${esc(barWords(r))}">${gaugeSVG(d, tps ? D.HEAT : 0, theme)}` +
      `<span class="v${tps ? '' : ' is-idle'}">${fmtSpeed(tps)}</span><span class="unit">tok/s</span>${rblockHTML(r, key)}</span>`;
  }

  function menubarSection(theme) {
    const list = agents();
    const top = topOf(list);
    const live = top ? { score: top.value, state: top.state, form: top.form, n: top.n, think: D.THINK_TOTAL } : { score: null, think: 0 };
    const states = [
      { id: 'live', label: 'task.state.passing', r: live },
      { id: 'unverified', label: 'task.state.unverified', r: { score: 9, state: 'unverified', form: 'live', n: 2, think: 64 } },
      { id: 'resting', label: 'phase.idle', r: { score: 72, state: 'passing', form: 'resting', n: 2, think: 0 } },
      { id: 'none', label: 'task.state.none', r: { score: null, think: 0 } },
    ];
    const mac = `<div class="mbar">${itemHTML(live, theme, 'mac', 'bar-mac')}<span class="sys">${I.wifi}</span><span class="sys">${I.battery}</span>` +
      '<span class="sys tnum">15:42</span></div>';
    const win = `<div class="tbar">${itemHTML(live, theme, 'win', 'bar-win')}<span class="tray">${I.caret}${I.wifi}${I.speaker}</span>` +
      '<span class="clock tnum">15:42<br>2026/10/28</span></div>';
    const tip = top ? `<div class="hover-tip">${esc(t('task.tooltip.top', top.value, top.agent.project))}</div>` : '';
    const macStates = states.map((s) => `<div class="bar-state"><div class="mbar">${itemHTML(s.r, theme, 'mac', '')}</div>` +
      `<div class="bar-label">${esc(t(s.label))}</div><div class="vo">${esc(barWords(s.r))}</div></div>`).join('');
    const winStates = states.map((s) => `<div class="bar-state"><div class="tbar">${itemHTML(s.r, theme, 'win', '')}</div>` +
      `<div class="bar-label">${esc(t(s.label))}</div></div>`).join('');
    return `${mac}<div class="caption" style="text-align:left">${esc(barWords(live))}</div>` +
      `<div class="bar-states">${macStates}</div>` +
      `<div style="height:22px"></div>${win}${tip}<div class="bar-states">${winStates}</div>`;
  }

  // ── Board (glass, M) ─────────────────────────────────────────────────────────────────
  function capsuleHTML(a, small) {
    if (!a.effort && !a.speed) return '';
    const hs = small ? [3.2, 4.4, 5.6, 6.8, 8] : [4, 5.5, 7, 8.5, 10];
    const meter = a.level ? `<span class="meter">${hs.map((h, i) => `<i class="${i < a.level ? 'on' : ''}" style="height:${h}px"></i>`).join('')}</span>` : '';
    const bolt = a.speed ? `<span class="bolt">${I.bolt}</span>` : '';
    return `<span class="capsule" aria-hidden="true">${meter}<span class="w">${esc(a.effort || '')}</span>${bolt}</span>`;
  }

  function sgArc(f, unproven, pale, uid) {
    const cx = 33;
    const cy = 33;
    const r = 29;
    const track = `<path d="${arcD(cx, cy, r, 150, 390)}" fill="none" stroke="var(--line-2)" stroke-width="3" stroke-linecap="round"/>`;
    if (f == null) return track;
    const end = 150 + 240 * f;
    const ang = (end * Math.PI) / 180;
    const tx = cx + r * Math.cos(ang);
    const ty = cy + r * Math.sin(ang);
    const d = arcD(cx, cy, r, 150, end);
    const dash = unproven ? ' stroke-dasharray="0 6"' : '';
    const glow = pale ? '' : `<path class="glow" d="${d}" fill="none" stroke="var(--pearl-b)" stroke-opacity=".6" stroke-width="3" stroke-linecap="round"${dash} filter="url(#sgf-${uid})"/>`;
    const tip = unproven ? `<circle cx="${tx.toFixed(2)}" cy="${ty.toFixed(2)}" r="2.3" fill="var(--board-bg)" stroke="var(--pearl-b)" stroke-width="1.2"/>`
      : `<circle cx="${tx.toFixed(2)}" cy="${ty.toFixed(2)}" r="2.3" fill="#fff" stroke="var(--pearl-b)" stroke-width=".6"/>`;
    return `${track}<g class="val" opacity="${pale ? 0.35 : 1}">${glow}<path d="${d}" fill="none" stroke="url(#sgg-${uid})" stroke-width="3" stroke-linecap="round"${dash}/>${tip}</g>`;
  }

  function sparkPath(w, h) {
    const pts = [];
    for (let i = 0; i < 60; i++) {
      const v = 0.52 + 0.22 * Math.sin(i / 6.5) + 0.12 * Math.sin(i / 2.1 + 1) + 0.06 * Math.sin(i * 1.7);
      pts.push([(i / 59) * w, h - 1.5 - Math.max(0, Math.min(1, v)) * (h - 3)]);
    }
    return pts.map((p, i) => `${i ? 'L' : 'M'}${p[0].toFixed(1)} ${p[1].toFixed(1)}`).join('');
  }

  function boardRows(all) {
    const list = inSets(all);
    const roots = list.filter((a) => a.depth === 0);
    const groups = roots.map((r) => {
      const members = list.filter((a) => a.id === r.id || a.parent === r.id);
      return { root: r, members, tps: members.reduce((s, a) => s + a.tps, 0), top: topOf(members) };
    }).sort((a, b) => b.tps - a.tps);
    return groups.filter((g) => g.root.id !== 'lumen');
  }

  function boardSection(theme) {
    const list = agents();
    const top = topOf(list);
    const uid = `b-${theme}`;
    const total = D.HERO_TPS;
    const heat = heatColor(D.HEAT, theme === 'light' ? 0.22 : 0);
    const gen = inSets(list).filter((a) => a.phase === 'generating').length;
    const tools = inSets(list).filter((a) => a.phase === 'tools').length;
    const rows = boardRows(list).map((g, i) => {
      const r = g.root;
      const tp = g.top;
      const iq = tp ? numHTML(tp.value, tp.state, { key: `brow-${r.id}`, cls: '', pale: tp.form === 'resting' }) : '<span class="is-quiet">–</span>';
      const share = g.tps ? `<span class="share" style="color:${srcColor(r.source)};width:${((g.tps / total) * 250).toFixed(0)}px"></span>` : '';
      const detail = [r.model, r.subCount ? t('boards.subCount', r.subCount) : ''].filter(Boolean).join(' · ');
      const words = [t('a11y.board.row', i + 1, r.project, fmtSpeed(g.tps))];
      if (tp) words.push(t('a11y.agent.intel', scoreWords(tp.value, tp.state, tp.form, tp.n)));
      let html = `<div class="brow" tabindex="0" data-card="${r.id}" aria-label="${esc(words.join('，'))}">` +
        `<span class="rk">${i + 1}</span><span class="bd" style="background:${r.phase === 'idle' ? 'var(--b-ink-3)' : srcColor(r.source)}"></span>` +
        `<span class="nm"><b>${esc(r.project)}</b><i>${esc(detail)}</i></span><span class="cap">${capsuleHTML(r, true)}</span>` +
        `<span class="iq${tp && tp.form === 'resting' ? ' is-pale' : ''}">${iq}</span>` +
        `<span class="bv${g.tps ? '' : ' is-quiet'}">${g.tps ? fmtSpeed(g.tps) : '–'}</span>${share}</div>`;
      g.members.filter((m) => m.depth === 1).forEach((m) => {
        const sw = [t('a11y.board.row', i + 1, m.project, fmtSpeed(m.tps))];
        if (m.score != null) sw.push(t('a11y.agent.intel', scoreWords(m.score, m.state, m.form, 0)));
        html += `<div class="brow sub" tabindex="0" data-card="${m.id}" aria-label="${esc(sw.join('，'))}">` +
          `<span class="bd" style="background:${srcColor(m.source)};width:6px;height:6px"></span>` +
          `<span class="nm"><b>${esc(m.model)}</b></span><span class="cap">${capsuleHTML(m, true)}</span>` +
          `<span class="iq${m.form === 'resting' ? ' is-pale' : ''}">${numHTML(m.score, m.state, { key: `brow-${m.id}` })}</span>` +
          `<span class="bv">${fmtSpeed(m.tps)}</span></div>`;
      });
      return html;
    }).join('');
    const f = top ? top.value / 100 : null;
    const boardWords = [t('a11y.board', t('history.allSources'), fmtSpeed(total), t('title.maglev')),
      top ? t('a11y.orb.intel', scoreWords(top.value, top.state, top.form, top.n), fmtSpeed(D.THINK_TOTAL)) : t('intel.none')].join('，');
    return '<div class="board-wrap">' +
      `<div class="board" role="list" aria-label="${esc(boardWords)}">` +
      `<div class="hero"><div class="hero-l"><div class="hero-v">${total}<small>tok/s</small></div>` +
      `<div class="hero-t" style="color:${heat}">${esc(t('title.maglev'))}</div>` +
      `<div class="hero-c">${esc(t('agents.context', gen, tools))}</div></div>` +
      `<div class="sg" tabindex="0" data-card="top" aria-label="${esc(top ? t('task.tooltip.top', top.value, top.agent.project) : t('intel.none'))}">` +
      `<svg viewBox="0 0 66 49" aria-hidden="true"><defs><linearGradient id="sgg-${uid}" x1="0" y1="1" x2="1" y2="0">` +
      '<stop offset="0" style="stop-color:var(--pearl-c)"/><stop offset=".5" style="stop-color:var(--pearl-a)"/><stop offset="1" style="stop-color:var(--pearl-b)"/></linearGradient>' +
      `<filter id="sgf-${uid}" x="-30%" y="-30%" width="160%" height="160%"><feGaussianBlur stdDeviation="1.5"/></filter></defs>` +
      `<g class="arc" data-uid="${uid}">${sgArc(f, top && unproven(top.state), top && top.form === 'resting', uid)}</g></svg>` +
      `<div class="lab">${esc(t('intel.glyph'))}</div>` +
      `<div class="num${top && top.form === 'resting' ? ' is-pale' : ''}">${top ? numHTML(top.value, top.state, { key: 'board-hero', anim: 'count' }) : '–'}</div>` +
      `<div class="tbl"><span class="l">${esc(t('intel.thinking.glyph'))}</span><span class="g"></span><span class="n">${fmtSpeed(D.THINK_TOTAL)}</span><span class="u">tok/s</span>` +
      `<span class="l">${esc(t('cost.glyph'))}</span><span class="g"></span><span class="n">0.024</span><span class="u">US$/s</span></div></div></div>` +
      `<svg class="strip" viewBox="0 0 308 16" preserveAspectRatio="none" aria-hidden="true"><defs><linearGradient id="spf-${uid}" x1="0" y1="0" x2="0" y2="1">` +
      `<stop offset="0" stop-color="${heat}" stop-opacity=".28"/><stop offset="1" stop-color="${heat}" stop-opacity="0"/></linearGradient></defs>` +
      `<path d="${sparkPath(308, 16)}L308 16L0 16Z" fill="url(#spf-${uid})"/><path d="${sparkPath(308, 16)}" fill="none" stroke="${heat}" stroke-width="1.25"/></svg>` +
      `<div class="rank"><div class="caps" aria-hidden="true"><span class="hair"></span><span class="ci">${esc(t('intel.glyph'))}</span><span class="cv">tok/s</span></div>${rows}</div>` +
      `<div class="foot"><span>${esc(t('boards.today', '128M'))} tok</span><b>US$42.15</b><span>${esc(t('boards.peak', '2.16k'))}</span></div>` +
      '</div><div class="card-slot" data-area="board"></div></div>';
  }

  // ── Dashboard ────────────────────────────────────────────────────────────────────────
  function badgeHTML(a) {
    if (a.phase === 'generating') return `<span class="dots3" style="color:${srcColor(a.source)}"><i></i><i></i><i></i></span>`;
    if (a.phase === 'tools') return `<span style="color:var(--ink-2);display:inline-flex">${I.hourglass}</span>`;
    return '<span class="idle-dot"></span>';
  }

  function dashRow(a) {
    const s = a.signals || {};
    const loop = s.looping ? `<span class="loop" title="${esc(t('work.looping', s.looping))}">${I.loop}</span>` : '';
    const iq = numHTML(a.score, a.state, { key: `dash-${a.id}`, anim: 'roll', pale: a.form === 'resting' });
    let lv;
    if (a.tps >= 0.05) lv = `<span class="lv">${fmtSpeed(a.tps)}</span>`;
    else if (a.phase === 'tools') lv = `<span class="lv" style="color:var(--ink-2)">${I.hourglass}</span>`;
    else lv = '<span class="lv is-quiet">–</span>';
    const words = [a.project, a.model, a.tps >= 0.05 ? `${fmtSpeed(a.tps)} tok/s` : t('phase.' + a.phase)];
    if (a.score != null) words.push(t('a11y.agent.intel', scoreWords(a.score, a.state, a.form, 0)));
    if (s.looping) words.push(t('work.looping', s.looping));
    return `<div class="drow" tabindex="0" data-card="${a.id}" aria-label="${esc(words.join('，'))}" aria-haspopup="dialog">` +
      (a.depth ? '<span class="indent" style="width:12px"></span>' : '') +
      `<span class="badge">${badgeHTML(a)}</span><span class="namebox">${loop}<b>${esc(a.project)}</b>` +
      `<i>${esc(a.model)} · ${esc(srcName(a.source))}</i></span>` +
      `<span class="capcol">${capsuleHTML(a)}</span><span class="iq${a.form === 'resting' ? ' is-pale' : ''}">${iq}</span>${lv}</div>`;
  }

  function dashboardSection() {
    const list = agents();
    return `<div class="dash-wrap"><div class="card-slot" data-area="dashboard" data-hint="${esc(t('a11y.task.card'))}"></div>` +
      `<div class="panel" style="--capw:84px"><div class="p-head"><h3>${esc(t('dashboard.agents'))}</h3>` +
      `<span class="cap-i" title="${esc(t('agents.intel.hint'))}">${esc(t('intel.glyph'))}</span><span class="cap-v">tok/s</span></div>` +
      `${inSets(list).map(dashRow).join('')}</div></div>` +
      `<div class="caption" style="text-align:right;margin-top:10px">${esc(t('agents.intel.hint'))}</div>`;
  }

  function cardsSection() {
    const order = [['none', 'lumen'], ['unverified', 'nova-sub'], ['failing', 'rocket'], ['failing · edited', 'vega'],
      ['passing', 'nova'], ['pending', 'quasar'], ['resting', 'orbit'], ['unsupported', 'tihu']];
    const list = agents();
    return `<div class="cards-grid">${order.filter(([s]) => !ui.cardFilter || s.startsWith(ui.cardFilter)).map(([s, id]) => {
      const a = list.find((x) => x.id === id);
      return `<figure><figcaption>${s}</figcaption>${cardHTML(a, { key: `g-${id}` })}</figure>`;
    }).join('')}</div>`;
  }

  // ── Usage History: task performance ──────────────────────────────────────────────────
  const STATS = window.TS_STATS.build(D);
  const roundScore = (v) => (v == null ? null : Math.round(v));
  const pctOfRatio = (r) => (r ? Math.round(r.v * 100) : null);

  const METRICS = [
    { id: 'success', key: 'history.tasks.metric.success', lower: false, val: (s) => pctOfRatio(s.success),
      fmt: (v) => v + '%', scale: () => 100, diff: 1, tip: (s) => s.success && t('history.tasks.rate.scored', s.success.n, s.success.d) },
    { id: 'stall', key: 'history.tasks.metric.stall', lower: true, val: (s) => pctOfRatio(s.stall),
      fmt: (v) => v + '%', scale: () => 100, diff: 1, tip: (s) => s.stall && t('history.tasks.rate.tasks', s.stall.n, s.stall.d) },
    { id: 'fixFail', key: 'history.tasks.metric.fixFail', lower: true, val: (s) => pctOfRatio(s.fixFail),
      fmt: (v) => v + '%', scale: () => 100, diff: 1, tip: (s) => s.fixFail && t('history.tasks.rate.fixes', s.fixFail.n, s.fixFail.d) },
    { id: 'attempts', key: 'history.tasks.metric.attempts', short: 'history.tasks.metric.attempts.short', lower: true,
      val: (s) => (s.attempts == null ? null : Math.round(s.attempts * 10) / 10),
      fmt: (v) => t('history.tasks.attempts', v.toFixed(1)), scale: (a, b) => Math.max(3, Math.max(a, b) * 1.25), diff: 0.1, tip: () => '' },
    { id: 'firstPass', key: 'history.tasks.metric.firstPass', short: 'history.tasks.metric.firstPass.short', lower: true,
      val: (s) => s.firstPassMs ?? null, fmt: fmtDur, scale: (a, b) => Math.max(a, b) * 1.25, diff: 0.05, relative: true, tip: () => '' },
  ];

  function better(m, va, vb, sa, sb) {
    if (va == null || vb == null) return null;
    if (sa.scored < D.HISTORY.lowSample || sb.scored < D.HISTORY.lowSample) return null;
    const gap = m.relative ? Math.abs(va - vb) / Math.max(va, vb) : Math.abs(va - vb);
    if (gap < m.diff) return null;
    return (m.lower ? va < vb : va > vb) ? 'a' : 'b';
  }

  // Bins [0,10) … [80,90), [90,100]; the median line sits at the unrounded median.
  function histSVG(s, color, w, h, cls) {
    const max = Math.max(1, ...s.bins);
    const gap = 2;
    const bw = (w - gap * 9) / 10;
    const ph = h - 10;
    const mi = s.median == null ? -1 : s.median >= 90 ? 9 : Math.floor(s.median / 10);
    const bars = s.bins.map((n, i) => {
      const bh = n ? Math.max(1.5, (n / max) * ph) : 0;
      return `<rect x="${(i * (bw + gap)).toFixed(1)}" y="${(ph - bh).toFixed(1)}" width="${bw.toFixed(1)}" height="${bh.toFixed(1)}" rx="1.2" fill="${color}" opacity="${i === mi ? 0.95 : 0.5}"/>`;
    }).join('');
    const mx = s.median == null ? '' : `<line x1="${((s.median / 100) * w).toFixed(1)}" x2="${((s.median / 100) * w).toFixed(1)}" y1="-2" y2="${ph + 1}" stroke="var(--ink)" stroke-width="1" stroke-opacity=".55"/>`;
    const label = s.median == null ? t('history.tasks.side.empty')
      : t('a11y.history.tasks.distribution', roundScore(s.q1), roundScore(s.median), roundScore(s.q3));
    return `<svg class="hist ${cls}" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}" role="img" aria-label="${esc(label)}">` +
      `<line x1="0" x2="${w}" y1="${ph + 0.5}" y2="${ph + 0.5}" stroke="var(--line-2)"/>${bars}${mx}` +
      `<text class="axis" x="0" y="${h}">0</text><text class="axis" x="${w}" y="${h}" text-anchor="end">100</text></svg>`;
  }

  const lowChip = (s) => (s.scored < D.HISTORY.lowSample
    ? `<span class="chip chip-sm" title="${esc(t('history.tasks.sample.hint', s.scored, D.HISTORY.lowSample))}">${esc(t('history.tasks.sample.low'))}</span>` : '');

  function sideHTML(id, which) {
    const s = STATS.bySource[id];
    const name = srcName(id);
    const src = `<span class="src"><span class="sdot" style="background:${srcColor(id)}"></span>${esc(name)}</span>`;
    if (!s || !s.tasks) return `<div class="side side-${which}">${src}<span class="cnt">${esc(t('history.tasks.side.empty'))}</span></div>`;
    const hist = histSVG(s, srcColor(id), 120, 34, 'hist-wide') + histSVG(s, srcColor(id), 96, 30, 'hist-medium');
    const mv = `<span class="mv"><b>${roundScore(s.median)}</b><span>${esc(t('history.tasks.metric.median.short'))}</span></span>`;
    const words = t('a11y.history.tasks.side', name, s.tasks, s.scored, roundScore(s.median));
    return `<div class="side side-${which}" role="group" aria-label="${esc(words)}">${src}` +
      `<span class="cnt">${esc(glue(t('history.tasks.counts', s.scored, s.tasks)))}</span>${lowChip(s)}` +
      `<div class="med">${which === 'a' ? hist + mv : mv + hist}</div></div>`;
  }

  function metricWords(m, a, b, va, vb, win) {
    const label = t(m.key);
    let words = t('a11y.history.tasks.compare', m.lower ? t('a11y.history.tasks.lowerBetter', label) : label,
      srcName(a), va == null ? '–' : m.fmt(va), srcName(b), vb == null ? '–' : m.fmt(vb));
    if (win) words = t('a11y.history.tasks.better', words, srcName(win === 'a' ? a : b));
    return words;
  }

  function butterflyHTML(a, b) {
    const sa = STATS.bySource[a] || {};
    const sb = STATS.bySource[b] || {};
    return METRICS.map((m) => {
      const va = sa.tasks ? m.val(sa) : null;
      const vb = sb.tasks ? m.val(sb) : null;
      const sc = m.scale(va || 0, vb || 0) || 1;
      const win = better(m, va, vb, sa, sb);
      const fill = (v, id) => (v == null ? '' : `<i style="width:${Math.min(100, (v / sc) * 100).toFixed(1)}%;background:${srcColor(id)}"></i>`);
      const tipA = sa.tasks ? m.tip(sa) : '';
      const tipB = sb.tasks ? m.tip(sb) : '';
      return `<div class="bf-v a${win === 'a' ? ' better' : ''}" aria-hidden="true"${tipA ? ` title="${esc(tipA)}"` : ''}>${va == null ? '–' : esc(m.fmt(va))}</div>` +
        `<div class="bf-t a" aria-hidden="true">${fill(va, a)}</div>` +
        `<div class="bf-l" role="group" aria-label="${esc(metricWords(m, a, b, va, vb, win))}" title="${esc(t(m.key + '.hint'))}"><b>${esc(t(m.key))}</b>` +
        (m.lower ? `<small class="lb">${esc(t('history.tasks.lowerBetter'))}</small>` : '') + '</div>' +
        `<div class="bf-t b" aria-hidden="true">${fill(vb, b)}</div>` +
        `<div class="bf-v b${win === 'b' ? ' better' : ''}" aria-hidden="true"${tipB ? ` title="${esc(tipB)}"` : ''}>${vb == null ? '–' : esc(m.fmt(vb))}</div>`;
    }).join('');
  }

  function compactHTML(a, b) {
    const sa = STATS.bySource[a] || {};
    const sb = STATS.bySource[b] || {};
    const sum = (id, s) => {
      const head = `<div class="src"><span class="sdot" style="background:${srcColor(id)}"></span>${esc(srcName(id))}</div>`;
      if (!s.tasks) return `<div class="sum">${head}<div class="cnt" style="margin-top:8px">${esc(t('history.tasks.side.empty'))}</div></div>`;
      return `<div class="sum" role="group" aria-label="${esc(t('a11y.history.tasks.side', srcName(id), s.tasks, s.scored, roundScore(s.median)))}">${head}` +
        `<div class="mv"><b>${roundScore(s.median)}</b><span>${esc(t('history.tasks.metric.median.short'))}</span></div>` +
        `<div class="cnt">${esc(glue(t('history.tasks.counts', s.scored, s.tasks)))} ${lowChip(s)}</div>${histSVG(s, srcColor(id), 150, 26, '')}</div>`;
    };
    const rows = METRICS.map((m) => {
      const va = sa.tasks ? m.val(sa) : null;
      const vb = sb.tasks ? m.val(sb) : null;
      const win = better(m, va, vb, sa, sb);
      const cell = (v, s, w) => `<div class="v${w ? ' better' : ''}"${v != null && m.tip(s) ? ` title="${esc(m.tip(s))}"` : ''}>${v == null ? '–' : esc(m.fmt(v))}</div>`;
      return `<div class="lab" role="group" aria-label="${esc(metricWords(m, a, b, va, vb, win))}" title="${esc(t(m.key + '.hint'))}">${esc(t(m.key))}` +
        `${m.lower ? `<small>${esc(t('history.tasks.lowerBetter'))}</small>` : ''}</div>${cell(va, sa, win === 'a')}${cell(vb, sb, win === 'b')}`;
    }).join('');
    const hdr = (id) => `<div class="h"><span class="sdot" style="background:${srcColor(id)}"></span>${esc(srcName(id))}</div>`;
    return `<div class="tp-compact"><div class="sumrow">${sum(a, sa)}${sum(b, sb)}</div>` +
      `<div class="ctab"><div class="h"></div>${hdr(a)}${hdr(b)}${rows}</div></div>`;
  }

  function iqrHTML(m) {
    const col = srcColor(m.sources[0]);
    return `<span class="iqr" role="img" aria-label="${esc(t('a11y.history.tasks.iqr', roundScore(m.q1), roundScore(m.q3)))}">` +
      `<i style="left:${m.q1.toFixed(1)}%;width:${(m.q3 - m.q1).toFixed(1)}%;background:${col}"></i><b style="left:calc(${m.median.toFixed(1)}% - 1px)"></b></span>`;
  }

  // One row per model id across sources; several sources overlap their dots (7 pt, 4 pt apart, a ring in the row colour).
  function dotsHTML(sources) {
    const names = sources.map(srcName).join(t('list.separator'));
    const tip = sources.length > 1 ? ` title="${esc(t('history.tasks.model.sources', names))}"` : ` title="${esc(names)}"`;
    return `<span class="mdots" style="width:${7 + 4 * (sources.length - 1)}px"${tip} aria-label="${esc(t('history.tasks.model.sources', names))}" role="img">` +
      sources.map((id, i) => `<i style="left:${4 * i}px;background:${srcColor(id)};z-index:${sources.length - i}"></i>`).join('') + '</span>';
  }

  function modelsHTML(filter) {
    const rows = STATS.byModel.filter((m) => !filter || m.sources.some((id) => filter.includes(id)));
    const cells = (m) => {
      const low = m.scored < D.HISTORY.lowSample;
      const rate = (metric) => {
        const v = metric.val(m);
        const tip = metric.tip(m);
        return { text: v == null ? '–' : metric.fmt(v), tip };
      };
      return {
        low,
        name: `<span class="mname"><span class="mn-main">${dotsHTML(m.sources)}<span class="t">${esc(m.model)}</span></span>` +
          (low ? `<span class="mn-extra">${lowChip(m)}</span>` : '') + '</span>',
        med: `<span class="medcell">${roundScore(m.median)}${iqrHTML(m)}</span>`,
        metrics: METRICS.map(rate),
      };
    };
    const th = (full, short, extra = '') => `<th scope="col" title="${esc(t(full))}" aria-label="${esc(t(full))}"${extra}>${esc(t(short || full))}`;
    const head = `<tr><th scope="col">${esc(t('history.tasks.column.model'))}</th>` +
      `${th('history.tasks.metric.tasks', '', ' class="col-sep"')}</th>` +
      `${th('history.tasks.metric.scored', 'history.tasks.metric.scored.short', ' class="col-sep"')}</th>` +
      `${th('history.tasks.column.scoredOfTasks', '', ' class="col-merged"')}</th>` +
      `${th('history.tasks.metric.median', 'history.tasks.metric.median.short')}</th>` +
      METRICS.map((m) => `${th(m.key, m.short)}</th>`).join('') + '</tr>';
    const body = rows.map((m) => {
      const c = cells(m);
      return `<tr class="${c.low ? 'low' : ''}"><td>${c.name}</td><td class="col-sep">${m.tasks}</td><td class="col-sep">${m.scored}</td>` +
        `<td class="col-merged">${m.scored} / ${m.tasks}</td><td>${c.med}</td>` +
        c.metrics.map((x) => `<td${x.tip ? ` title="${esc(x.tip)}"` : ''}>${esc(x.text)}</td>`).join('') + '</tr>';
    }).join('');
    const list = rows.map((m) => {
      const c = cells(m);
      const pair = (k, v) => `<span class="nw">${esc(k)}${v ? ' ' + esc(v) : ''}</span>`;
      const [succ, stall, fix, att, fp] = c.metrics;
      return `<div class="mitem${c.low ? ' low' : ''}"><div class="l1">${c.name}<span class="r">${c.med}</span></div>` +
        `<div class="l2">${[pair(t('history.tasks.metric.success'), succ.text), pair(t('history.tasks.metric.stall'), stall.text),
          pair(t('history.tasks.metric.fixFail'), fix.text)].join(' · ')}<br>` +
        `${[pair(glue(t('history.tasks.counts', m.scored, m.tasks)), ''), pair(t('history.tasks.metric.attempts.short'), att.text),
          pair(t('history.tasks.metric.firstPass.short'), fp.text)].join(' · ')}</div></div>`;
    }).join('');
    return `<div class="tp-models"><h3>${esc(t('history.tasks.byModel'))}</h3><table class="tbl"><thead>${head}</thead><tbody>${body}</tbody></table>` +
      `<div class="mlist">${list}</div></div>`;
  }

  function tpHeadHTML(a, b, picks = true) {
    const pick = (id, k) => `<button class="pick" aria-label="${esc(t(k))}: ${esc(srcName(id))}"><span class="sdot" style="background:${srcColor(id)}"></span>` +
      `<span class="nm">${esc(srcName(id))}</span><span class="chev">${I.chev}</span></button>`;
    return `<div class="tp-head"><h2>${esc(t('history.tasks.title'))}</h2><button class="info" title="${esc(t('history.tasks.info'))}" aria-label="${esc(t('history.tasks.info'))}">i</button>` +
      (picks ? `<div class="picks">${pick(a, 'history.tasks.pickA.a11y')}<span class="vs">${esc(t('history.tasks.vs'))}</span>${pick(b, 'history.tasks.pickB.a11y')}</div>` : '') + '</div>';
  }

  function tpHTML(mode = 'full', a = 'codex', b = 'claudeCode') {
    if (mode === 'empty') {
      return `<section class="tp" aria-label="${esc(t('history.tasks.title'))}">${tpHeadHTML(a, b, false)}<div class="tp-state">` +
        `<span class="gl">${I.gaugeMuted}</span><b>${esc(t('history.tasks.empty'))}</b><span>${esc(t('history.tasks.empty.hint'))}</span></div></section>`;
    }
    if (mode === 'analyzing') {
      return `<section class="tp" aria-busy="true" aria-label="${esc(t('history.tasks.title'))}">${tpHeadHTML(a, b, false)}<div class="skel" aria-hidden="true">` +
        '<i style="width:62%"></i><i style="width:88%"></i><i style="width:74%"></i><i style="width:81%"></i><i style="width:55%"></i></div>' +
        `<div class="tp-state" style="padding:14px 0 4px"><span>${esc(t('history.tasks.analyzing'))}</span></div></section>`;
    }
    const filter = mode === 'oneEmpty' ? [a, b] : null;
    return `<section class="tp" aria-label="${esc(t('history.tasks.title'))}">${tpHeadHTML(a, b)}` +
      `<div class="h2h">${sideHTML(a, 'a')}<div class="hcenter"></div>${sideHTML(b, 'b')}<div class="bf-sep"></div>${butterflyHTML(a, b)}</div>` +
      compactHTML(a, b) + (mode === 'oneEmpty' ? '' : modelsHTML(filter)) +
      `<div class="tp-foot">${esc(t('history.tasks.footnote'))}</div></section>`;
  }

  function hwinHTML(width, inner, narrow) {
    const ranges = ['today', 'yesterday', '7d', '30d', '90d', 'year', 'all', 'custom'];
    const seg = narrow ? `<span class="tb-pop" style="margin-left:auto">${esc(t('history.range.30d'))} ${I.chev}</span>`
      : `<span class="tb-seg">${ranges.map((r) => `<span class="${r === '30d' ? 'on' : ''}">${esc(t('history.range.' + r))}</span>`).join('')}</span>` +
        `<span class="tb-pop">${esc(t('history.export'))}</span>`;
    return `<div class="hwin${width < 800 && !narrow ? ' compact' : ''}" style="width:${width}px;max-width:100%"><div class="hbar"><span class="lights"><i></i><i></i><i></i></span>` +
      `<span class="tb-pop"><span class="sdot" style="background:conic-gradient(#34D399 0 33%,#FF8A65 0 66%,#B39DFF 0)"></span>${esc(t('history.allSources'))} ${I.chev}</span>${seg}</div>` +
      `<div class="hbody"${narrow ? ' style="padding:18px 16px 16px"' : ''}><h1>${esc(t('history.title'))}</h1>` +
      `<div class="range">${esc(D.HISTORY.range)} ${esc(t('history.range.30d').replace(/^\d+\s*/, ''))}</div>${inner}</div>` +
      `<div class="hfoot"><span></span><span class="lock">${I.lock}${esc(t(narrow ? 'history.privacy.short' : 'history.privacy'))}</span></div></div>`;
  }

  function historySection() {
    const v = ui.variant;
    const parts = [];
    if (!v || v === 'wide') parts.push(`<div><div class="variant-tag">Wide · 920</div>${hwinHTML(920, tpHTML())}</div>`);
    const row = [];
    if (!v || v === 'medium') row.push(`<div><div class="variant-tag">Medium · 720</div>${hwinHTML(720, tpHTML())}</div>`);
    if (!v || v === 'narrow') row.push(`<div style="flex:1;min-width:0;max-width:390px"><div class="variant-tag">Narrow · 390</div>${hwinHTML(390, tpHTML(), true)}</div>`);
    if (row.length) parts.push(`<div class="narrow-pair">${row.join('')}</div>`);
    if (!v || v === 'states') {
      parts.push('<div><div class="variant-tag">States</div><div class="states-row">' +
        `<div>${tpHTML('empty')}</div><div>${tpHTML('oneEmpty', 'pi', 'codex')}</div><div>${tpHTML('analyzing')}</div></div></div>`);
    }
    return `<div class="hist-variants">${parts.join('')}</div>`;
  }

  // ── Page assembly ────────────────────────────────────────────────────────────────────
  const SECTIONS = [
    { id: 'orb', title: '懸浮窗讀數', note: '最高分來自 nova（不是最快的 rocket）；虛線底＝尚未驗證或待重新驗證；淡色＝閒置（保留到 agent 離開清單，也就是最後一行後 10 分鐘，再 10 秒淡出）；– ＝沒有分數。思考仍是總和。', render: orbSection },
    { id: 'menubar', title: '選單列 / 工作列', note: '固定寬度的兩行讀數（73.5 / 68.5 pt 的格子不變）；思考為 0 時第一行置中；下方是 VoiceOver / 朗讀程式唸出的文字。', render: menubarSection },
    { id: 'board', title: '排行看板（流光玻璃 M）', note: '主讀數是整個看板的最高分；群組列是成員中的最高分；子 agent 列是自己的分數。移到「智力」副錶或任一列可看評分卡。', render: boardSection },
    { id: 'dashboard', title: '儀表板 Agent 列', note: '智力欄位寬度不變；移到任一列（或聚焦後按空白鍵）會在儀表板旁顯示評分卡。', render: dashboardSection, stacked: true },
    { id: 'cards', title: '評分卡：每一種狀態', note: '尚無評分、尚未驗證、未通過、失敗後已修改、已通過、待重新驗證、閒置、不支援的來源。修復失敗的預測只在未通過時出現；每格標示自己的校準狀態。', render: cardsSection },
    { id: 'history', title: '用量歷史：任務表現', note: '雙方都有 10 個以上已評分任務才標出較好的一方；寬、中、窄三種版面與空白、單邊無資料、重新分析中的狀態。', render: historySection, stacked: true },
  ];

  function render(animate) {
    const before = animate ? snapshot() : null;
    document.documentElement.lang = ui.lang;
    document.body.dataset.view = ui.view;
    document.body.classList.toggle('reduce', ui.reduce);
    const root = document.getElementById('sections');
    root.innerHTML = SECTIONS.filter((s) => !ui.only || ui.only === s.id).map((s) => {
      const cols = ['dark', 'light'].map((th) => `<div class="theme-col" data-theme="${th}"><div class="wall"></div>` +
        `<span class="col-tag">${th}</span>${s.render(th)}</div>`).join('');
      return `<section class="sec" id="sec-${s.id}"><div class="sec-head"><h2>${s.title}</h2><p>${s.note}</p></div>` +
        `<div class="pair"${s.stacked ? ' style="--cols:1"' : ''}>${cols}</div></section>`;
    }).join('');
    syncControls();
    document.querySelectorAll('.card-slot').forEach(slotDefault);
    if (ui.openCard) showCard(ui.openCard.id, ui.openCard.theme, ui.openCard.area, false);
    if (before) animateChanges(before);
  }

  // ── Motion ───────────────────────────────────────────────────────────────────────────
  const themeOf = (el) => el.closest('[data-theme]')?.dataset.theme || '';

  function snapshot() {
    const m = new Map();
    document.querySelectorAll('[data-k]').forEach((el) => {
      if (el.dataset.k) m.set(`${el.dataset.k}|${themeOf(el)}`, { v: el.dataset.v, pm: el.dataset.pm });
    });
    return m;
  }

  const easeOutCubic = (x) => 1 - Math.pow(1 - x, 3);

  function countUp(el, from, to) {
    const dur = Math.min(800, 300 + 12 * (to - from));
    const sg = el.closest('.sg');
    const t0 = performance.now();
    const step = (now) => {
      const k = Math.min(1, (now - t0) / dur);
      const v = Math.round(from + (to - from) * easeOutCubic(k));
      el.textContent = v;
      if (sg) updateArc(sg, v);
      if (k < 1) requestAnimationFrame(step);
    };
    el.textContent = from;
    if (sg) updateArc(sg, from);
    requestAnimationFrame(step);
  }

  function updateArc(sg, v) {
    const g = sg.querySelector('g.arc');
    const uid = g.dataset.uid;
    const num = sg.querySelector('[data-v]');
    const state = num && num.dataset.pm === '1' ? 'pending' : 'passing';
    g.innerHTML = sgArc(v / 100, unproven(state), sg.querySelector('.num').classList.contains('is-pale'), uid);
  }

  function animateChanges(prev) {
    document.querySelectorAll('[data-k]').forEach((el) => {
      if (!el.dataset.k) return;
      const old = prev.get(`${el.dataset.k}|${themeOf(el)}`);
      if (!old) return;
      if (old.pm !== '1' && el.dataset.pm === '1') el.classList.add('pm-new');
      const ov = old.v === undefined ? null : Number(old.v);
      const nv = el.dataset.v === undefined ? null : Number(el.dataset.v);
      if (ov === nv) return;
      const fade = (ms) => el.animate([{ opacity: 0.15 }, { opacity: 1 }], { duration: ms, easing: 'ease-out' });
      if (ui.reduce || ov == null || nv == null) return void fade(150);
      if (nv < ov) return void fade(200);
      if (nv - ov < 3) return;
      if (el.dataset.anim === 'count') countUp(el, ov, nv);
      else if (el.dataset.anim === 'roll') {
        el.animate([{ transform: 'translateY(60%)', opacity: 0, filter: 'blur(1.4px)' }, { transform: 'none', opacity: 1, filter: 'blur(0)' }],
          { duration: 240, easing: 'cubic-bezier(.2,.8,.2,1)' });
      }
    });
  }

  function play(kind) {
    if (kind === 'reset') { ui.scenario = 'final'; render(false); return; }
    if (kind === 'loop') {
      ui.scenario = 'final';
      render(false);
      setTimeout(() => { ui.scenario = 'loop'; render(true); }, 450);
      return;
    }
    if (kind === 'climb') {
      ui.scenario = 'before';
      render(false);
      setTimeout(() => { ui.scenario = 'final'; render(true); }, 700);
    } else {
      ui.scenario = 'final';
      render(false);
      setTimeout(() => { ui.scenario = 'drop'; render(true); }, 450);
    }
  }

  // ── Hover cards ──────────────────────────────────────────────────────────────────────
  let hoverTimer = 0;
  let hideTimer = 0;

  function slotDefault(slot) {
    if (slot.dataset.area === 'board') {
      const tp = topOf(agents());
      slot.innerHTML = tp ? `<div class="card-float rest" style="top:0">${cardHTML(tp.agent, { topN: tp.n, key: 'top' })}</div>` : '';
    } else {
      slot.innerHTML = `<div class="slot-hint">${esc(slot.dataset.hint)}</div>`;
    }
    slot.style.minHeight = '';
  }

  function hideCard() {
    document.querySelectorAll('.card-slot').forEach(slotDefault);
    document.querySelectorAll('.drow.is-open, .brow.is-open').forEach((n) => n.classList.remove('is-open'));
    ui.openCard = null;
  }

  function showCard(id, theme, area, fresh = true) {
    const col = document.querySelector(`#sec-${area} .theme-col[data-theme="${theme}"]`);
    if (!col) return;
    const host = col.querySelector(`[data-card="${id}"]`);
    const slot = col.querySelector('.card-slot');
    if (!host || !slot) return;
    const list = agents();
    let a;
    let opts = {};
    if (id === 'top') {
      const tp = topOf(list);
      if (!tp) return;
      a = tp.agent;
      opts = { topN: tp.n, key: 'top' };
    } else {
      a = list.find((x) => x.id === id);
    }
    col.querySelectorAll('.is-open').forEach((n) => n.classList.remove('is-open'));
    slot.innerHTML = `<div class="card-float"${fresh ? '' : ' style="animation:none"'}>${cardHTML(a, opts)}</div>`;
    const div = slot.firstElementChild;
    const top = Math.max(0, host.getBoundingClientRect().top - slot.getBoundingClientRect().top - 14);
    div.style.top = `${top}px`;
    slot.style.minHeight = `${top + div.offsetHeight}px`;
    host.classList.add('is-open');
    ui.openCard = { id, theme, area };
  }

  function bindHover() {
    document.addEventListener('pointerover', (e) => {
      const host = e.target.closest('[data-card]');
      if (!host) return;
      clearTimeout(hideTimer);
      clearTimeout(hoverTimer);
      const area = host.closest('#sec-board') ? 'board' : 'dashboard';
      const open = ui.openCard;
      const delay = open ? 0 : area === 'board' ? 350 : 450;
      hoverTimer = setTimeout(() => showCard(host.dataset.card, themeOf(host), area, !open), delay);
    });
    document.addEventListener('pointerout', (e) => {
      const host = e.target.closest('[data-card]');
      if (!host || host.contains(e.relatedTarget)) return;
      clearTimeout(hoverTimer);
      hideTimer = setTimeout(hideCard, 150);
    });
    document.addEventListener('keydown', (e) => {
      if (e.key === 'Escape') return hideCard();
      const host = e.target.closest && e.target.closest('[data-card]');
      if (host && (e.key === ' ' || e.key === 'Enter')) {
        e.preventDefault();
        const area = host.closest('#sec-board') ? 'board' : 'dashboard';
        if (ui.openCard && ui.openCard.id === host.dataset.card) hideCard();
        else showCard(host.dataset.card, themeOf(host), area);
      }
    });
  }

  // ── Controls ─────────────────────────────────────────────────────────────────────────
  function syncControls() {
    document.querySelectorAll('[data-lang]').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.lang === ui.lang)));
    document.querySelectorAll('[data-view]').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.view === ui.view)));
    const rm = document.getElementById('reduce');
    if (rm) rm.checked = ui.reduce;
  }

  function bindControls() {
    document.querySelectorAll('[data-lang]').forEach((b) => b.addEventListener('click', () => { ui.lang = b.dataset.lang; render(false); }));
    document.querySelectorAll('[data-view]').forEach((b) => b.addEventListener('click', () => { ui.view = b.dataset.view; render(false); }));
    document.getElementById('reduce').addEventListener('change', (e) => { ui.reduce = e.target.checked; render(false); });
    document.querySelectorAll('[data-play]').forEach((b) => b.addEventListener('click', () => play(b.dataset.play)));
  }

  window.addEventListener('error', (e) => {
    const box = document.getElementById('errors');
    if (box) box.textContent += `ERROR: ${e.message} @${e.lineno}\n`;
  });

  document.addEventListener('DOMContentLoaded', () => {
    bindControls();
    bindHover();

    render(false);
    if (ui.open) {
      const area = ui.open === 'hero' || ui.open.startsWith('b:') ? 'board' : 'dashboard';
      const id = ui.open === 'hero' ? 'top' : ui.open.replace(/^b:/, '');
      ['light', 'dark'].forEach((th) => showCard(id, th, area, false));
    }
    if (params.get('debug')) {
      const vw = document.documentElement.clientWidth;
      const wide = [...document.querySelectorAll('body *')].filter((el) => el.getBoundingClientRect().right > vw + 0.5)
        .slice(0, 8).map((el) => `${el.tagName}.${el.className} ${Math.round(el.getBoundingClientRect().right)}`);
      document.getElementById('errors').textContent += `vw ${vw} sw ${document.documentElement.scrollWidth}\n${wide.join('\n')}`;
    }
    const auto = params.get('autoplay') || (!ui.isStatic && !ui.only && !ui.open ? 'climb' : '');
    if (auto) setTimeout(() => play(auto), 600);
  });
})();
