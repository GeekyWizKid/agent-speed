// A leaderboard board that can render any style (glass / timing / neon) with any metrics layout option (A / B / C),
// several on one page. Board rules follow board-glass/-timing/-neon.html (DESIGN 2c); the options add the live
// intelligence, thinking and cost readouts (METRICS 6.9, 9.5; DESIGN 9) and a per-row intelligence column.
(function (global) {
  "use strict";
  const S = global.AgentSim, F = global.MetricFormat;
  const NS = "http://www.w3.org/2000/svg";
  const BOLT = '<svg viewBox="0 0 8 10"><path d="M4.8 0 0.6 5.6h2.7L2.6 10 7.4 4.1H4.6z"/></svg>';
  const LIMITS = { S: { groups: 4, subs: [0, 0, 0, 0] }, M: { groups: 6, subs: [2, 1, 1] }, L: { groups: 8, subs: [3, 3, 3, 2, 2] } };
  const STYLES = {
    glass: { radius: 26, fx: { radius: 26 }, spring: [170, 0.9], rowExtra: 2, moreH: 20, firstY: 6, colsH: 12 },
    timing: { radius: 20, fx: { radius: 20, comets: [1, 1, 2, 2, 3], ringWidth: 1.2 }, spring: [260, 0.72], rowExtra: 0, moreH: 19, firstY: 0, colsH: 14 },
    neon: { radius: 22, fx: { radius: 22, comets: [1, 2, 3, 4, 4], ringWidth: 1.4, warp: true }, spring: [170, 0.9], rowExtra: 2, moreH: 20, firstY: 6, colsH: 12 },
  };
  const KEYS = ["intel", "think", "cost"];
  const IQ_MIN = 60, IQ_SPAN = 90;
  let uid = 0;

  const el = (tag, cls, html) => { const e = document.createElement(tag); if (cls) e.className = cls; if (html !== undefined) e.innerHTML = html; return e; };

  /** Per-character rolling digits; a change of length crossfades instead (DESIGN 2c: digits roll, nothing jumps). */
  function roll(node, text, up, reduce) {
    const old = node.dataset.t;
    if (old === text) return;
    node.dataset.t = text;
    if (node.hasAttribute("data-ghost")) node.setAttribute("data-ghost", text.replace(/[0-9]/g, "8"));
    if (reduce || old === undefined || old.length !== text.length) {
      node.replaceChildren(...[...text].map(c => { const s = el("span", "slot"), ch = el("span", "ch"); ch.textContent = c; s.appendChild(ch); return s; }));
      return;
    }
    [...text].forEach((c, i) => {
      if (old[i] === c) return;
      const slot = node.children[i], prev = slot.querySelector(".ch:not(.out)"), next = el("span", "ch in");
      next.textContent = c; next.style.transform = `translateY(${up ? 100 : -100}%)`; next.style.opacity = "0";
      if (prev) prev.classList.add("out");
      slot.appendChild(next);
      requestAnimationFrame(() => {
        next.style.transform = ""; next.style.opacity = "";
        if (prev) { prev.style.transform = `translateY(${up ? -100 : 100}%)`; prev.style.opacity = "0"; }
      });
      setTimeout(() => prev && prev.remove(), 300);
    });
  }

  function create(host, options) {
    const o = Object.assign({ style: "glass", option: "A", size: "M", metrics: { intel: true, think: true, cost: true },
                              rowCost: false, currency: "USD", lang: "zh-Hant", reduceMotion: false, effects: true, tip: null }, options);
    const st = STYLES[o.style], id = ++uid;
    const L = F.LABELS[o.lang];
    const on = (k) => !!o.metrics[k];
    const anyMetric = () => KEYS.some(on);
    const optB = () => o.option === "B" && anyMetric();
    const unitTok = o.style === "timing" ? "TOK/S" : "tok/s";
    // Two-line rows (model · cost per row): always in C, a toggle elsewhere; S keeps single-line rows.
    const rows2 = (o.option === "C" || o.rowCost) && on("cost");

    // ── Build ─────────────────────────────────────────────────────────────────────────
    const panel = el("section", `mb ${o.style} opt${o.option}`);
    panel.dataset.size = o.size;
    panel.setAttribute("role", "list");
    panel.setAttribute("aria-label", "排行看板");
    if (on("intel")) panel.classList.add("cap-on");
    if (rows2) panel.classList.add("rows2");
    if (o.style === "glass") panel.appendChild(el("div", "aurora", "<i></i><i></i>"));
    if (o.style === "timing") panel.appendChild(el("div", "rail", "<i></i>"));

    const hero = el("div", "hero"), hl = el("div", "hl"), hr = el("div", "hr");
    if (o.style === "timing") hl.appendChild(el("span", "live", "<i></i>LIVE"));
    const value = el("div", "value"), num = el("span", "num");
    if (o.style === "neon") { const glow = el("span", "glow"); glow.appendChild(num); value.appendChild(glow); } else value.appendChild(num);
    value.appendChild(el("span", "unit", unitTok));
    const meta = el("div", "meta"), title = el("span", "title"), counts = el("span", "counts");
    meta.append(title, counts);
    hl.append(value, meta);
    hero.append(hl, hr);
    panel.appendChild(hero);

    // History: glass sparkline, timing 30 bars, neon 24-segment LED (always full width in neon).
    const history = makeHistory(optB() && o.style !== "neon");
    if (o.style === "neon") { const led = el("div", "led"); for (let i = 0; i < 24; i++) led.appendChild(el("i")); hero.appendChild(led); history.led = led; }
    else if (!optB()) hr.appendChild(history.node);

    const cells = {};
    function cell(k, extra) {
      const c = el("div", "m" + (extra ? " " + extra : ""));
      c.dataset.k = k;
      const label = el("span", "ml"); label.textContent = L[k === "think" ? "thinking" : k];
      const mv = el("span", "mv"), ge = el("span", "ge", "≥"), mn = el("span", "mn"), mu = el("span", "mu");
      if (o.style === "neon") mn.setAttribute("data-ghost", "");
      mv.append(ge, mn, mu);
      c.append(label, mv);
      cells[k] = c;
      return c;
    }

    let arc = null, segs = null;
    if (o.option === "A" && anyMetric()) {
      const band = el("div", "band");
      KEYS.filter(on).forEach(k => band.appendChild(cell(k)));
      panel.appendChild(band);
    } else if (optB()) {
      if (o.style === "timing") {
        const box = el("div", "dbox");
        KEYS.filter(on).forEach((k, i) => box.appendChild(cell(k, i === 0 ? "main" : "")));
        hr.appendChild(box);
      } else {
        const g = el("div", "gauge"), ks = KEYS.filter(on);
        const main = ks[0] === "intel" ? ks.shift() : null;
        if (main && o.style === "glass") {
          const dial = el("div", "dial");
          dial.innerHTML = `<svg viewBox="0 0 66 66" aria-hidden="true"><defs><linearGradient id="pearl-${id}" x1="0" y1="1" x2="1" y2="0">` +
            `<stop offset="0" style="stop-color:var(--pearl-c)"/><stop offset=".5" style="stop-color:var(--pearl-a)"/><stop offset="1" style="stop-color:var(--pearl-b)"/></linearGradient></defs>` +
            `<path class="trk" d="${arcPath()}" pathLength="100"/><path class="arc" d="${arcPath()}" pathLength="100" stroke-dasharray="100 100" stroke-dashoffset="100" style="stroke:url(#pearl-${id})"/>` +
            `<circle class="tip" r="2.3" cx="${33 + 29 * Math.cos(150 * Math.PI / 180)}" cy="${33 + 29 * Math.sin(150 * Math.PI / 180)}" style="transform-origin:33px 33px;transform-box:view-box"/></svg>`;
          dial.appendChild(cell("intel", "main"));
          g.appendChild(dial);
          arc = { path: dial.querySelector(".arc"), tip: dial.querySelector(".tip") };
        } else if (main) {
          g.appendChild(cell("intel", "main"));
          if (o.style === "neon") { segs = el("div", "segs"); for (let i = 0; i < 12; i++) segs.appendChild(el("i")); g.appendChild(segs); }
        }
        if (ks.length) { const subs = el("div", "subs"); ks.forEach(k => subs.appendChild(cell(k))); g.appendChild(subs); }
        hr.appendChild(g);
      }
      if (o.style !== "neon") { const strip = el("div", "strip"); strip.appendChild(history.node); panel.appendChild(strip); }
    }

    const board = el("div", "board");
    panel.appendChild(board);
    let cols = null;
    if (on("intel")) {
      cols = el("div", "cols");
      cols.innerHTML = o.style === "timing" ? `<i class="ln"></i><span class="g">間隔</span><span>${L.intel}</span><span>${unitTok}</span>`
                                            : `<i class="ln"></i><span>${L.intel}</span><span>${unitTok}</span>`;
      board.appendChild(cols);
    }

    const foot = el("div", "foot");
    const today = el("b"), costToday = el("b"), peak = el("b");
    if (o.option === "C" && anyMetric()) {
      foot.classList.add("two");
      // Live over today, column by column: 智力 over today's tokens, the cost rate over today's cost, 思考 over the peak.
      ["intel", "cost", "think"].forEach(k => foot.appendChild(on(k) ? cell(k) : el("span")));
    }
    const s1 = el("span"), s2 = el("span"), s3 = el("span", "peak");
    if (o.style === "timing") { s1.append("TODAY ", today); s3.append("PEAK ", peak); } else { s1.append("今日 ", today, " tok"); s3.append("峰值 ", peak); }
    s2.appendChild(costToday);
    foot.append(s1, s2, s3);
    panel.appendChild(foot);
    host.appendChild(panel);

    const fx = global.BoardFX.create(panel, st.fx);
    fx.setReduceMotion(o.reduceMotion || !o.effects);

    function arcPath() {
      const r = 29, a0 = 150 * Math.PI / 180, a1 = 30 * Math.PI / 180;
      return `M${(33 + r * Math.cos(a0)).toFixed(2)} ${(33 + r * Math.sin(a0)).toFixed(2)} A${r} ${r} 0 1 1 ${(33 + r * Math.cos(a1)).toFixed(2)} ${(33 + r * Math.sin(a1)).toFixed(2)}`;
    }

    function makeHistory(wide) {
      if (o.style === "glass") {
        const svg = document.createElementNS(NS, "svg");
        svg.setAttribute("class", "spark"); svg.setAttribute("aria-hidden", "true"); svg.setAttribute("preserveAspectRatio", "none");
        const W = wide ? 300 : 104, H = wide ? 16 : 40;
        svg.setAttribute("viewBox", `0 0 ${W} ${H}`);
        svg.innerHTML = `<defs><linearGradient id="sg-${id}" x1="0" x2="0" y1="0" y2="1"><stop offset="0" stop-color="currentColor" stop-opacity="${wide ? .28 : .35}"/>` +
          `<stop offset="1" stop-color="currentColor" stop-opacity="0"/></linearGradient></defs><path class="a" fill="url(#sg-${id})"/>` +
          `<path class="l" fill="none" stroke="currentColor" stroke-width="${wide ? 1.25 : 1.5}" stroke-linejoin="round" vector-effect="non-scaling-stroke"/>` +
          (wide ? "" : `<circle class="d" r="2.4" fill="currentColor"/>`);
        return { node: svg, W, H, wide };
      }
      const bars = el("div", "bars");
      for (let i = 0; i < 30; i++) bars.appendChild(el("i"));
      return { node: bars, wide };
    }

    // ── Rows ──────────────────────────────────────────────────────────────────────────
    const items = new Map();
    function capsule() {
      return el("span", "cap none", '<span class="meter"><i></i><i></i><i></i><i></i><i></i></span><span class="word"></span><span class="bolts"></span>');
    }
    function paintCapsule(c, agent) {
      const e = agent.effort, bolts = agent.bolts, key = (e ? e.value : "-") + "/" + bolts;
      if (c.dataset.key === key) return;
      c.dataset.key = key;
      const meter = c.querySelector(".meter");
      c.classList.toggle("none", !e && !bolts);
      c.classList.toggle("max", !!e && e.value === "max");
      c.classList.toggle("top", !!e && e.top);
      meter.style.display = e && e.level !== null ? "" : "none";
      [...meter.children].forEach((bar, i) => bar.classList.toggle("on", !!e && e.level > i));
      const w = c.querySelector(".word"); w.textContent = e ? e.value : ""; w.style.display = e ? "" : "none";
      c.querySelector(".bolts").innerHTML = BOLT.repeat(bolts);
    }

    function makeItem(kind) {
      const it = el("div", "item " + kind);
      it.setAttribute("role", "listitem");
      const timing = o.style === "timing";
      if (kind === "more") {
        it.innerHTML = timing ? '<div class="row"><span></span><span></span><span class="name"></span><span></span><span class="gap"></span><span class="iq"></span><span class="speed"></span></div>'
          : '<div class="row"><span></span><span></span><span class="name"><span class="stack"><i></i><i></i><i></i></span><span class="label"></span></span><span></span><span class="iq"></span><span class="speed"></span></div>';
      } else {
        it.innerHTML = timing
          ? '<div class="row"><span class="wipe"></span><span class="pos"><span></span></span><span class="stripe"></span><span class="name"><b></b><small></small></span>' +
            '<span class="capslot"></span><span class="gap"></span><span class="iq"></span><span class="speed"></span><span class="l2"></span><span class="chip"></span></div>'
          : '<div class="row"><span class="bar"></span><span class="sweep"></span><span class="rank"><span></span></span><span class="badge"></span><span class="name"><b></b><small></small></span>' +
            '<span class="capslot"></span><span class="iq"></span><span class="speed"></span><span class="l2"></span><span class="chip"></span></div>';
        it.querySelector(".capslot").replaceWith(capsule());
        if (kind === "group") {
          it.addEventListener("mouseenter", (ev) => o.tip && o.tip.show(tipHtml(it.dataset.id), ev.currentTarget));
          it.addEventListener("mouseleave", () => o.tip && o.tip.hide());
        }
      }
      it.style.opacity = "0";
      board.appendChild(it);
      return { el: it, kind, y: null, target: 0, v: 0, alive: true };
    }

    function badge(b, phase) {
      const cls = phase === "generating" ? "gen" : phase === "tools" ? "tools" : "idle";
      if (b.dataset.phase === cls) return;
      b.dataset.phase = cls; b.className = "badge " + cls; b.innerHTML = cls === "gen" ? "<i></i><i></i><i></i>" : "";
    }

    function iqCell(node, live, grade) {
      const shown = live !== null && live !== undefined;
      node.textContent = on("intel") ? String(Math.round(shown ? live : grade)) : "";
      node.classList.toggle("grade", !shown);
    }

    function costLine(node, live, rate, share) {
      if (!on("cost") || !(live > 0.05)) { node.textContent = ""; return; }
      const r = F.rate(rate, share, o.currency, o.lang);
      const unit = r.unknown ? "" : `<span class="u">${o.style === "timing" ? r.unit.toUpperCase() : r.unit}</span>`;
      node.innerHTML = `<span class="ge">≥</span>${r.number}${unit}`;
      node.classList.toggle("partial", r.partial);
      node.classList.toggle("dim", r.unknown);
    }

    function paintRow(it, agent, p) {
      const row = it.el.firstElementChild;
      it.el.dataset.id = p.id;
      row.style.setProperty("--src", agent.sourceColor);
      const h = S.heatOf(p.live), live = p.live > 0.05;
      if (o.style !== "timing") {
        row.style.setProperty("--share", (p.total > 0 ? Math.min(1, p.live / p.total) : 0).toFixed(4));
        row.style.setProperty("--rowheat", S.heatColor(Math.max(0.12, h)));
        badge(row.querySelector(".badge"), p.generating ? "generating" : agent.phase);
      } else {
        row.querySelector(".gap").textContent = p.gap || "";
      }
      row.classList.toggle("gen", p.generating || agent.phase === "generating");
      row.querySelector(".name b").textContent = p.name;
      row.querySelector(".name small").textContent = p.detail;
      const sp = row.querySelector(".speed");
      sp.textContent = live ? S.formatSpeed(p.live) : "–";
      sp.classList.toggle("dim", !live);
      if (o.style !== "timing") sp.style.color = live && h > 0.45 ? S.heatColor(h) : "";
      iqCell(row.querySelector(".iq"), p.intelligence, p.grade);
      costLine(row.querySelector(".l2"), p.live, p.costRate, p.costKnownShare);
      paintCapsule(row.querySelector(".cap"), agent);
      if (p.rank !== undefined) {
        const r = row.querySelector(o.style === "timing" ? ".pos span" : ".rank span");
        if (r.textContent !== String(p.rank)) {
          const prev = Number(r.textContent) || p.rank;
          if (!o.reduceMotion && r.textContent) {
            r.style.transition = "none"; r.style.transform = `translateY(${p.rank < prev ? 100 : -100}%)`;
            requestAnimationFrame(() => { r.style.transition = ""; r.style.transform = ""; });
          }
          r.textContent = p.rank;
        }
        it.el.classList.toggle("lead", p.rank === 1);
      }
      row.setAttribute("aria-label", p.rank ? `第 ${p.rank} 名，${p.name}，每秒 ${live ? S.formatSpeed(p.live) : 0} 個 token，智力 ${Math.round(p.intelligence ?? p.grade)}` : p.name);
    }

    function accent(it, ev) {
      if (o.reduceMotion || !o.effects) return;
      const row = it.el.firstElementChild;
      const sw = row.querySelector(o.style === "timing" ? ".wipe" : ".sweep"), chip = row.querySelector(".chip");
      sw.classList.remove("go"); chip.classList.remove("go"); void row.offsetWidth;
      sw.classList.add("go"); chip.textContent = "▲" + (ev.from - ev.to); chip.classList.add("go");
      const p = panel.getBoundingClientRect(), r = row.getBoundingClientRect(), y = r.top - p.top + r.height / 2;
      const src = getComputedStyle(row).getPropertyValue("--src").trim() || "#ffffff";
      fx.streak(r.left - p.left, y, r.right - p.left, src);
      const rk = row.querySelector(o.style === "timing" ? ".pos" : ".rank").getBoundingClientRect();
      const rx = rk.left - p.left + rk.width / 2, ry = rk.top - p.top + rk.height / 2;
      if (ev.type === "newLeader") {
        if (o.style === "neon") fx.lightning(rx + 30 + Math.random() * 120, -6, rx, ry, "#FACC15", { branches: 3, life: 0.45 });
        fx.celebrate(rx, ry);
      } else {
        fx.burst(rx, ry, { count: 14, speed: 130, life: .7, colors: [src, "#FFFFFF"], size: 1.5, gravity: 60 });
      }
    }

    // Groups first, then sub rows in rank order while room remains (DESIGN 2c Layout per size).
    function layout(snap) {
      const lim = LIMITS[o.size], cs = getComputedStyle(panel);
      const rowH = parseFloat(cs.getPropertyValue("--row")) + (rows2 && o.size !== "S" ? 8 : st.rowExtra);
      const subH = parseFloat(cs.getPropertyValue("--sub")), moreH = st.moreH;
      const y0 = cols ? st.colsH + 2 : st.firstY;
      const room = board.clientHeight - y0;
      const all = snap.groups;
      let groups = all.slice(0, lim.groups);
      const restRow = () => all.length > groups.length ? moreH : 0;
      while (groups.length > 1 && groups.length * rowH + restRow() > room) groups = groups.slice(0, -1);
      let free = room - groups.length * rowH - restRow();
      const plan = groups.map((g, i) => ({ g, subs: g.subs.filter(s => s.live > 0.05).slice(0, lim.subs[i] || 0), more: false }));
      for (const p of plan) {
        const keep = [];
        for (const s of p.subs) {
          const needMore = p.g.subs.length - keep.length - 1 > 0 && !p.more ? moreH : 0;
          if (free >= subH + needMore) { keep.push(s); free -= subH; if (needMore) { p.more = true; free -= moreH; } }
        }
        if (p.more && keep.length === p.g.subs.length) { p.more = false; free += moreH; }
        p.subs = keep;
      }
      const list = [];
      let y = y0;
      for (const p of plan) {
        list.push({ key: p.g.id, kind: "group", g: p.g, y }); y += rowH;
        for (const s of p.subs) { list.push({ key: s.id, kind: "sub", g: p.g, s, y }); y += subH; }
        if (p.more) { list.push({ key: p.g.id + "#more", kind: "more", g: p.g, rest: p.g.subs.length - p.subs.length, y }); y += moreH; }
      }
      const hidden = all.slice(groups.length);
      if (hidden.length) list.push({ key: "#rest", kind: "more", groups: true, g: hidden[0], rest: hidden.length, restLive: hidden.reduce((s, g) => s + g.live, 0), y });
      return list;
    }

    // ── Metrics ───────────────────────────────────────────────────────────────────────
    const prevNum = {};
    function paintMetrics(total) {
      const reduce = o.reduceMotion;
      const put = (k, text, unit, cls) => {
        const c = cells[k]; if (!c) return;
        const mn = c.querySelector(".mn"), before = prevNum[k];
        roll(mn, text, before === undefined || parseFloat(text) >= before, reduce);
        prevNum[k] = parseFloat(text);
        c.querySelector(".mu").textContent = unit;
        c.classList.toggle("partial", !!(cls && cls.partial));
        c.classList.toggle("unknown", !!(cls && cls.unknown));
        c.classList.toggle("none", !!(cls && cls.none));
      };
      const iq = total.intelligence;
      put("intel", F.intelligence(iq), "", { none: iq === null });
      put("think", S.formatSpeed(total.thinking), unitTok);
      const r = F.rate(total.costRate, total.costKnownShare, o.currency, o.lang);
      put("cost", r.number, o.style === "timing" ? r.unit.toUpperCase() : r.unit, r);
      const frac = iq === null ? 0 : Math.max(0, Math.min(1, (iq - IQ_MIN) / IQ_SPAN));
      if (arc) {
        arc.path.setAttribute("stroke-dashoffset", (100 - frac * 100).toFixed(2));
        arc.path.style.opacity = frac > 0.002 ? "1" : "0";
        arc.tip.style.transform = `rotate(${(frac * 240).toFixed(2)}deg)`;
        arc.tip.style.opacity = iq === null ? "0" : "1";
      }
      if (segs) { const lit = Math.round(frac * 12); [...segs.children].forEach((s, i) => s.classList.toggle("on", i < lit)); }
      const rate = F.rate(total.costRate, total.costKnownShare, o.currency, o.lang);
      panel.setAttribute("aria-label", `排行看板，每秒 ${S.formatSpeed(total.live)} 個 token，${total.title.zh}` +
        (on("intel") ? `，智力 ${F.intelligence(iq)}` : "") + (on("think") ? `，思考每秒 ${S.formatSpeed(total.thinking)} 個 token` : "") +
        (on("cost") ? `，每秒費用 ${rate.partial ? "至少 " : ""}${rate.number} ${rate.unit}（API 等值）` : ""));
    }

    function drawHistory(hist, color, heat) {
      if (history.led) {
        const lit = Math.round(heat * 24), pk = Math.min(23, Math.round(S.heatOf(last.total.peak) * 24) - 1);
        [...history.led.children].forEach((seg, i) => {
          seg.classList.toggle("on", i < lit); seg.classList.toggle("pk", i === pk && i >= lit);
          seg.style.setProperty("--c", S.heatColor((i + 1) / 24));
        });
        return;
      }
      if (o.style === "glass") {
        const svg = history.node; svg.style.color = color;
        const n = hist.length; if (n < 2) return;
        const { W, H } = history, max = Math.max(10, ...hist) * 1.12;
        const pts = hist.map((v, i) => [i / (n - 1) * W, H - (history.wide ? 1.5 : 2) - v / max * (H - (history.wide ? 3 : 6))]);
        let d = `M${pts[0][0].toFixed(1)},${pts[0][1].toFixed(1)}`;
        for (let i = 1; i < pts.length; i++) {
          const [x0, y0] = pts[i - 1], [x1, y1] = pts[i];
          d += ` Q${x0.toFixed(1)},${y0.toFixed(1)} ${((x0 + x1) / 2).toFixed(1)},${((y0 + y1) / 2).toFixed(1)}`;
        }
        const lp = pts[pts.length - 1]; d += ` T${lp[0].toFixed(1)},${lp[1].toFixed(1)}`;
        svg.querySelector(".l").setAttribute("d", d);
        svg.querySelector(".a").setAttribute("d", d + ` L${W},${H} L0,${H} Z`);
        const dot = svg.querySelector(".d"); if (dot) { dot.setAttribute("cx", lp[0]); dot.setAttribute("cy", lp[1]); }
        return;
      }
      const h30 = hist.slice(-30), max = Math.max(10, ...h30), H = history.wide ? (o.size === "S" ? 10 : 12) : 38;
      [...history.node.children].forEach((b, i) => { const v = h30[i - (30 - h30.length)] || 0; b.style.height = Math.max(history.wide ? 1.5 : 2, v / max * H) + "px"; });
    }

    // ── Render ────────────────────────────────────────────────────────────────────────
    let last = null, lastTotal = 0;
    function render(snap, instant) {
      last = snap;
      const total = snap.total;
      roll(num, S.formatSpeed(total.live), total.live >= lastTotal, o.reduceMotion || instant);
      lastTotal = total.live;
      fx.setHeat(total.heat, total.overdrive);
      const heat = S.heatColor(Math.max(o.style === "timing" ? 0.1 : 0.06, total.heat));
      const ps = panel.style;
      ps.setProperty("--heat", heat);
      ps.setProperty("--heat-2", S.heatColor(Math.min(1, total.heat + 0.18)));
      ps.setProperty("--heat-soft", S.heatColor(total.heat, .55));
      ps.setProperty("--heat-amount", total.heat.toFixed(3));
      ps.setProperty("--maxc", S.heatColor(0.82)); ps.setProperty("--topa", S.heatColor(0.72)); ps.setProperty("--topb", S.heatColor(1));
      if (o.style === "neon") ps.setProperty("--breath", (3.2 - 2.2 * total.heat).toFixed(2) + "s");
      if (o.style === "timing") panel.querySelector(".live").classList.toggle("idle", total.generating === 0);
      title.textContent = total.title.zh;
      counts.textContent = o.style === "timing" ? `GEN ${total.generating} · TOOL ${total.tools}` : `· 生成中 ${total.generating} · 工具 ${total.tools}`;
      today.textContent = S.formatSpeed(total.today) + (o.style === "timing" ? " TOK" : "");
      costToday.textContent = F.amount(total.costToday, o.currency, o.lang);
      peak.textContent = S.formatSpeed(total.peak);
      drawHistory(snap.history, heat, total.heat);
      paintMetrics(total);

      const list = layout(snap), seen = new Set();
      for (const entry of list) {
        seen.add(entry.key);
        let it = items.get(entry.key);
        const fresh = !it;
        if (fresh) { it = makeItem(entry.kind); items.set(entry.key, it); }
        it.alive = true; it.el.classList.remove("gone");
        if (entry.kind === "group") {
          const g = entry.g, r = g.root, i = snap.groups.indexOf(g), ahead = i > 0 ? snap.groups[i - 1] : null;
          const subs = g.subs.length ? (o.style === "timing" ? `${g.subs.length} 子` : `${g.subs.length} 個子 agent`) : "";
          paintRow(it, r, { id: g.id, live: g.live, total: total.live, rank: g.rank, generating: g.generating > 0, name: r.project,
                            detail: [r.model, subs].filter(Boolean).join(" · "), intelligence: g.intelligence, grade: r.grade,
                            costRate: g.costRate, costKnownShare: g.costKnownShare,
                            gap: !ahead ? "領先" : (ahead.live - g.live >= 0 ? "−" : "+") + S.formatSpeed(Math.abs(ahead.live - g.live)) });
        } else if (entry.kind === "sub") {
          const s = entry.s;
          paintRow(it, s, { id: s.id, live: s.live, total: total.live, generating: false, name: s.label, detail: "",
                            intelligence: s.intelligence, grade: s.grade, costRate: s.costRate, costKnownShare: s.priced ? 1 : 0 });
        } else {
          const row = it.el.firstElementChild;
          row.style.setProperty("--src", entry.g.root.sourceColor);
          const restLive = entry.groups ? entry.restLive : entry.g.subs.slice(entry.g.subs.length - entry.rest).reduce((s, a) => s + a.live, 0);
          const label = entry.groups ? `還有 ${entry.rest} 個 agent` : `+${entry.rest} 個子 agent`;
          (row.querySelector(".label") || row.querySelector(".name")).textContent = label;
          row.querySelector(".speed").textContent = restLive > 0.05 ? S.formatSpeed(restLive) : "";
        }
        it.target = entry.y;
        if (fresh || instant || o.reduceMotion) { it.y = instant || o.reduceMotion ? entry.y : entry.y + 10; it.v = 0; }
        if (instant) it.el.style.opacity = ""; else requestAnimationFrame(() => { it.el.style.opacity = ""; });
      }
      for (const [key, it] of items) {
        if (seen.has(key) || !it.alive) continue;
        it.alive = false; it.el.classList.add("gone");
        setTimeout(() => { if (!it.alive) { it.el.remove(); items.delete(key); } }, 320);
      }
      if (!instant) for (const ev of snap.events) { const it = items.get(ev.id); if (it) accent(it, ev); }
      let w = 0;
      board.querySelectorAll(".item:not(.gone) .cap:not(.none)").forEach(c => { c.style.width = "auto"; w = Math.max(w, c.scrollWidth); });
      board.querySelectorAll(".cap").forEach(c => { c.style.width = ""; });
      panel.style.setProperty("--capw", w ? Math.ceil(w) + "px" : "0px");
      if (instant) { for (const it of items.values()) { it.y = it.target; it.v = 0; it.el.style.transform = `translate3d(0, ${it.y}px, 0)`; } }
      else kick();
    }

    // Springs per style; the loop stops when everything has settled (no idle frames).
    let raf = 0, lastT = 0;
    function kick() { if (!raf) { lastT = performance.now(); raf = requestAnimationFrame(frame); } }
    function frame(now) {
      const dt = Math.max(0, Math.min(0.05, (now - lastT) / 1000)); lastT = now;
      const [k, z] = st.spring, c = 2 * Math.sqrt(k) * z;
      let moving = false;
      for (const it of items.values()) {
        if (it.y === null) it.y = it.target;
        it.v += (-k * (it.y - it.target) - c * it.v) * dt; it.y += it.v * dt;
        if (Math.abs(it.y - it.target) < 0.05 && Math.abs(it.v) < 0.05) { it.y = it.target; it.v = 0; } else moving = true;
        it.el.style.transform = `translate3d(0, ${it.y.toFixed(2)}px, 0)`;
      }
      raf = moving ? requestAnimationFrame(frame) : 0;
    }

    function tipHtml(gid) {
      if (!last) return "";
      const g = last.groups.find(x => x.id === gid); if (!g) return "";
      const r = g.root, e = r.effort, cls = S.classOf(r.model);
      const klass = cls === 100 ? "旗艦" : cls === 80 ? "標準" : "輕量";
      const rate = F.rate(g.costRate, g.costKnownShare, o.currency, o.lang);
      const iq = g.intelligence === null ? `等級 ${r.grade} · ${klass}（閒置）` : `智力 ${Math.round(g.intelligence)} · 思考 ${Math.round(g.thinkingShare * 100)}%`;
      return `<b>${r.project}</b> · ${r.sourceName}<br>${r.model}<br>推理強度：${e ? e.value : "未記錄"}` +
        `<br>速度 <b>${S.formatSpeed(g.live)}</b> tok/s<br>${iq}<br>等級 ${r.grade} · ${klass}` +
        `<br>費用 ${rate.partial ? "≥" : ""}${rate.number} ${rate.unit}（API 等值）` +
        `<span class="cap-note">智力為估算指數，不是測驗分數</span>`;
    }

    return {
      el: panel, render,
      setEffects(v) { o.effects = v; fx.setReduceMotion(o.reduceMotion || !v); },
      setReduceMotion(v) { o.reduceMotion = v; fx.setReduceMotion(v || !o.effects); },
      destroy() { cancelAnimationFrame(raf); fx.setReduceMotion(true); panel.remove(); },
    };
  }

  global.MetricBoards = { create, STYLES };
})(typeof window !== "undefined" ? window : globalThis);
