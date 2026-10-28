#!/usr/bin/env python3
"""Reference implementation of the live intelligence index (METRICS 6.9).

  python3 spec/tools/intelligence.py            check spec/fixtures/intelligence-cases.json

The index weighs every output token generating in the 3-second window (METRICS 6.3) by the grade of the model and
effort that produced it, then adds the share of those tokens that is reasoning. The same sums restricted to one agent
give that agent's intelligence (live), else its grade (resting), else nothing. It reuses the engine's intervals and
estimates (reference.py) and the price-table resolution (prices.py); nothing here reads a log.
"""
from __future__ import annotations

import json
import os
import sys

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import execution_modes_reference as modes  # noqa: E402
import prices as pricing  # noqa: E402
import reference as engine  # noqa: E402

FIXTURES = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "fixtures")

FLAGSHIP, STANDARD, LIGHT = 100, 80, 60
FLAGSHIP_OUTPUT, STANDARD_OUTPUT = 15, 3          # USD per million output tokens, at least
LEVEL_STEP, DEFAULT_LEVEL, THINKING_BONUS = 6, 2, 20
NO_SEPARATE_REASONING = ("deepseek",)             # sources whose output does not split out reasoning (METRICS 4.3)


def model_class(index, model, provider):
    """Flagship, standard or light by the base output price of the resolved entry (unknown: standard)."""
    resolution = index.resolve(model, provider)
    if resolution.kind == "free":
        return LIGHT
    if resolution.kind != "priced":
        return STANDARD
    output = resolution.entry.get("prices", {}).get("output", 0)
    return FLAGSHIP if output >= FLAGSHIP_OUTPUT else STANDARD if output >= STANDARD_OUTPUT else LIGHT


def effort_level(effort):
    """The meter level of EXECUTION-MODES plus one for a top rung; unknown or level-less efforts count as medium."""
    display = modes.effort_display(effort) if effort else {"level": None, "top": False}
    level = display["level"]
    return DEFAULT_LEVEL if level is None else level + (1 if display["top"] else 0)


def grade(index, model, provider, effort):
    return model_class(index, model, provider) + LEVEL_STEP * effort_level(effort)


class Window:
    """The weighted sums of METRICS 6.9 for one set of responses and agents at one instant."""

    def __init__(self):
        self.weight = self.graded = self.reasoning = self.reasoning_weight = 0.0

    def add(self, source, weight, reasoning, grade_value):
        self.weight += weight
        self.graded += weight * grade_value
        if source not in NO_SEPARATE_REASONING:
            self.reasoning += reasoning
            self.reasoning_weight += weight

    @property
    def thinking_share(self):
        return self.reasoning / self.reasoning_weight if self.reasoning_weight > 0 else 0.0

    def result(self):
        intelligence = self.graded / self.weight + THINKING_BONUS * self.thinking_share if self.weight > 0 else None
        return {"intelligence": intelligence, "liveThinking": self.reasoning / (engine.WINDOW_MS / 1000),
                "thinkingShare": self.thinking_share}


def reasoning_share(responses):
    output = sum(r["output"] for r in responses)
    return sum(min(r.get("reasoning", 0), r["output"]) for r in responses) / output if output > 0 else 0.0


def agent_provider(agent_id, responses):
    """The provider of the agent's latest response that names one, in reading order (None when none does)."""
    provider = None
    for r in responses:
        if r["agentId"] == agent_id and r.get("provider") is not None:
            provider = r["provider"]
    return provider


def agent_grade(index, agent, responses):
    """The grade of the agent's current model, latest named provider and current effort; None without a model."""
    if not agent.get("model"):
        return None
    return grade(index, agent["model"], agent_provider(agent["id"], responses), agent.get("effort"))


def contributions(index, responses, agents, now):
    """Every contribution of the window (METRICS 6.9) as (agent id, source, weight, reasoning, grade)."""
    w0 = now - engine.WINDOW_MS
    measured = [r for r in responses if engine.interval(r) is not None and engine.is_final(r)]
    unmeasured = [r for r in responses if engine.interval(r) is not None and not engine.is_final(r)]
    by_id = {a["id"]: a for a in agents}
    found = []

    def response_grade(r):
        return grade(index, r.get("model"), r.get("provider"), r.get("effort"))

    for r in measured:
        start, end = engine.interval(r)
        share = engine.overlap(w0, now, start, end) / (end - start)
        found.append((r["agentId"], r["source"], r["output"] * share,
                      min(r.get("reasoning", 0), r["output"]) * share, response_grade(r)))

    def estimate(agent, seconds, grade_value):
        refs = engine.reference_set(measured, now, agent)
        speed = engine.speed_of(refs) if refs else None
        if speed is None or seconds <= 0:
            return
        weight = speed[0] * seconds
        found.append((agent["id"], agent["source"], weight, weight * reasoning_share(refs), grade_value))

    for r in unmeasured:
        agent = by_id.get(r["agentId"], {"id": r["agentId"], "model": r.get("model"), "source": r["source"]})
        start, end = engine.interval(r)
        estimate(agent, engine.overlap(w0, now, start, end) / 1000, response_grade(r))
    for a in agents:
        if a["phase"] != "generating" or a.get("inFlightStartMs") is None or a.get("lastLineMs") is None:
            continue
        if now - a["lastLineMs"] > 300_000:
            continue
        start = max([a["inFlightStartMs"]] + [r["endMs"] for r in responses if r["agentId"] == a["id"]])
        estimate(a, engine.overlap(w0, now, start, now) / 1000,
                 grade(index, a.get("model"), agent_provider(a["id"], responses), a.get("effort")))
    return found


def window_sums(index, responses, agents, now):
    """The sums of the whole set and of every agent with a contribution: (total, {agent id: Window})."""
    total, per_agent = Window(), {}
    for agent_id, source, weight, reasoning, grade_value in contributions(index, responses, agents, now):
        total.add(source, weight, reasoning, grade_value)
        per_agent.setdefault(agent_id, Window()).add(source, weight, reasoning, grade_value)
    return total, per_agent


def live_intelligence(index, responses, agents, now):
    """METRICS 6.9 over the given responses and agents (all sources, or an orb's set)."""
    return window_sums(index, responses, agents, now)[0].result()


def agent_intelligence(index, responses, agents, now, agent, per_agent=None):
    """METRICS 6.9 Agent intelligence: live from the agent's own sums, else its grade (resting), else none."""
    if per_agent is None:
        per_agent = window_sums(index, responses, agents, now)[1]
    window = per_agent.get(agent["id"], Window())
    sums = window.result()
    grade_value = agent_grade(index, agent, responses)
    if window.weight > 0:
        form, value = "live", sums["intelligence"]
    elif grade_value is not None:
        form, value = "resting", grade_value
    else:
        form, value = "none", None
    return {"form": form, "intelligence": value, "shown": None if value is None else round_half_away(value),
            "liveThinking": sums["liveThinking"], "thinkingShare": sums["thinkingShare"], "grade": grade_value}


def round_half_away(value):
    return int(value + 0.5) if value >= 0 else -int(-value + 0.5)


def close(expected, actual):
    return (expected is None) == (actual is None) and (expected is None or abs(expected - actual) <= 1e-6)


def check_set(failures, label, got, want):
    shown = None if got["intelligence"] is None else round_half_away(got["intelligence"])
    if shown != want["shown"]:
        failures.append("%s: shown expected %r got %r" % (label, want["shown"], shown))
    for name in ("intelligence", "liveThinking", "thinkingShare"):
        if not close(want[name], got[name]):
            failures.append("%s: %s expected %r got %r" % (label, name, want[name], got[name]))


def check_agent_windows(index, cases, failures):
    """Every listed agent's form and values, and the agents' sums adding up to the set's (METRICS 6.9)."""
    for case in cases:
        responses, agents = case["responses"], case["agents"]
        for query in case["queries"]:
            now = query["nowMs"]
            label = "%s @%d" % (case["name"], now)
            total, per_agent = window_sums(index, responses, agents, now)
            check_set(failures, label, total.result(), query["expect"])
            for field in ("weight", "graded", "reasoning", "reasoning_weight"):
                added = sum(getattr(w, field) for w in per_agent.values())
                if abs(added - getattr(total, field)) > 1e-6:
                    failures.append("%s: agents' %s add to %r, the set has %r" % (label, field, added,
                                                                                   getattr(total, field)))
            if sorted(query["agents"]) != sorted(a["id"] for a in agents):
                failures.append("%s: expectations for %s, agents %s" % (label, sorted(query["agents"]),
                                                                         sorted(a["id"] for a in agents)))
            for agent in agents:
                want = query["agents"].get(agent["id"])
                if want is None:
                    continue
                got = agent_intelligence(index, responses, agents, now, agent, per_agent)
                for name in ("form", "shown", "grade"):
                    if got[name] != want[name]:
                        failures.append("%s %s: %s expected %r got %r" % (label, agent["id"], name, want[name],
                                                                         got[name]))
                for name in ("intelligence", "liveThinking", "thinkingShare"):
                    if not close(want[name], got[name]):
                        failures.append("%s %s: %s expected %r got %r" % (label, agent["id"], name, want[name],
                                                                         got[name]))


def check_cases(path=None):
    cases = json.load(open(path or os.path.join(FIXTURES, "intelligence-cases.json"), encoding="utf-8"))
    index = pricing.Index(pricing.load(os.path.join(FIXTURES, cases["table"])))
    failures = []
    for case in cases["grades"]:
        got = {"class": model_class(index, case["model"], case.get("provider")),
               "level": effort_level(case.get("effort")),
               "grade": grade(index, case["model"], case.get("provider"), case.get("effort"))}
        if got != case["expect"]:
            failures.append("grade %s: expected %r got %r" % (case["name"], case["expect"], got))
    for case in cases["windows"]:
        for query in case["queries"]:
            subset = set(query.get("sources") or [r["source"] for r in case["responses"]] +
                         [a["source"] for a in case["agents"]])
            responses = [r for r in case["responses"] if r["source"] in subset]
            agents = [a for a in case["agents"] if a["source"] in subset]
            got = live_intelligence(index, responses, agents, query["nowMs"])
            check_set(failures, "%s @%d" % (case["name"], query["nowMs"]), got, query["expect"])
    agent_windows = cases.get("agentWindows", [])
    check_agent_windows(index, agent_windows, failures)
    counts = (len(cases["grades"]), sum(len(c["queries"]) for c in cases["windows"]),
              sum(len(c["queries"]) for c in agent_windows))
    return failures, counts


def main():
    failures, (grades, queries, agent_queries) = check_cases()
    if failures:
        print("\n".join(failures))
        sys.exit(1)
    print("intelligence reference matches %d grade cases, %d window queries and %d agent window queries"
          % (grades, queries, agent_queries))


if __name__ == "__main__":
    main()
