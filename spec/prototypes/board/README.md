# Leaderboard panel prototypes (排行看板)

A new floating window, separate from the orbs (which stay exactly as they are): a rounded square that ranks every
agent by live tok/s so the user sees their speed and what matters at a glance. These prototypes compare visual
directions before the platform specification (DESIGN 2c) is written. All of them render the same simulation
(`sim.js`), so differences are design only.

Open any `board-*.html` directly in a browser (no server, no network).

## Content (every direction)

1. **Hero** — total live tok/s (METRICS 7 format, tabular digits), `tok/s`, the speed title in heat color
   (DESIGN 1), `生成中 N · 工具 M`, and the last 60 s of total speed as an ambient sparkline.
2. **Board** — groups ranked by group speed (a root agent plus all its sub-agents, the user's choice). Each group row:
   rank, phase badge in the source color (generating: three-dot shimmer; tools: wrench; idle: grey dot), project,
   model, the effort capsule (DESIGN 4 Agents: meter, word, bolts), live value, and a bar of its share of the total.
   Under a group, its fastest sub-agents (smaller rows), then `+97 個子 agent · 8.6k` for the rest.
3. **Footer** — today's output tokens, today's cost, today's peak.

Sizes: S 280 × 280 (up to 4 groups, no sub rows), M 340 × 340 (default; up to 6 groups, two sub rows for #1, one for
#2–#3), L 420 × 420 (up to 8 groups; three sub rows for #1–#3, two for #4–#5). Groups first, sub rows fill what
remains. Light and dark. The normative specification is `spec/DESIGN.md` 2c; these files are its visual reference.

## Ranking (shared, `sim.js`)

Groups rank by the value they show (the engine's 3 s live rate); a group passes the one above only after leading it by
more than 10 % + 1 tok/s for two ticks (1 s), or at once by more than 2× + 20, so the window's wobble never reshuffles
the board. Idle groups keep their order by latest
activity. Each tick reports `events`: `overtake` / `newLeader` with `from` and `to` ranks.

## Motion rules

- Overtake: rows move to their new places with a spring (FLIP, ≤ 0.5 s); the climber gets a short accent (light
  sweep, rank roll, `▲2` chip); the passed row only follows. New leader: a distinct, richer accent on row 1.
- Values: digits roll, bars and meters spring; nothing jumps.
- Idle and steady states cost nothing: only transform / opacity / filter animations, no layout thrash, and no
  animation frames while nothing changes (a slow breathing glow at most).
- Reduce Motion (control in the page): crossfades only.

## Controls (every page)

Scenario (`swarm`: one workflow with 100 sub-agents plus three projects; `team`; `duel`; `calm`), size S/M/L, theme
dark/light, Reduce Motion, pause / step. Keep the controls quiet and outside the panel; the panel floats on a desktop
backdrop.

## Effects

`fx.js` is the shared effects layer: perimeter comets (count and lap time by heat), the heat ring and bloom, sparks,
row streaks, shockwaves, lightning, the new-leader celebration and neon's hyperspace streaks. Append `?steps=40&
scenario=team&size=L&theme=light` to a page for a settled frame without motion (screenshots); add `&fx=1` to keep the
effects and fire a new-leader celebration.

## Directions

| File | Direction |
| --- | --- |
| `board-glass.html` | 流光玻璃 — refined translucent glass, heat-tinted aurora light, calm typography; elegance first |
| `board-timing.html` | 計時塔 — racing broadcast timing tower: position blocks, gap-to-leader column, kinetic overtakes |
| `board-neon.html` | 霓虹座艙 — night HUD in the LED orb's language: neon edges, LED digits, warp streaks and digit lightning |
