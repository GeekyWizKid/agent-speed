#!/usr/bin/env python3
"""Reference implementation of the `taskScore` report extension (METRICS 11.10, REPORTING-EXTENSIONS 9).

Standard library only. It implements:

- when the extension is active for a report (the service advertises it and the `models` section is shared);
- the live values (the highest task score of a set, its form, the thinking speed) at the top level and per model,
  with the membership and `other` rules of the `models` section;
- what one completed task adds to its (model, source) row, and the task ledger of METRICS 11.10 as a simulator:
  which tasks are watched, when they count, what a break or a build does;
- the checks a service makes when it advertises the extension, and the conformance rules of the client;
- a receiver that merges reports into a model leaderboard (latest live values per device, summed increments);
- the `taskScore` part of spec/fixtures/report-vectors.json (written by report_reference.py --write, checked by it).
"""

from __future__ import annotations

import copy
import math

import report_reference as ref
from execution_modes_reference import supported

EXTENSION = "taskScore"
FORMS = ("live", "resting", "none")
STAT_FIELDS = ("completed", "scored", "scoreSum", "scoreBins", "successes", "stalls", "fixAttempts", "fixFailures",
               "recovered", "recoverAttempts")
COUNT_FIELDS = tuple(field for field in STAT_FIELDS if field != "scoreBins")
BINS = 10
MAX_COMPLETED = 1000          # completed tasks in one report, over every row
MAX_ROWS = 16                 # taskStats rows of one model entry
FIRST_EVENT_AGE_MS = 120_000  # a task whose first event is older than this when it is read is history
COMPLETE_IDLE_MS = 1_800_000  # METRICS 6.11 `complete`: 30 minutes since the agent's last line
UNRANKED = ("other", "unknown", "local")


def round_half_away(value: float, places: int = 0):
    """Half away from zero; every value here is >= 0."""
    scale = 10 ** places
    rounded = math.floor(value * scale + 0.5) / scale
    return int(rounded) if places == 0 else rounded


# ------------------------------------------------------------------------------------------------- capability

def active(document: dict, models_switch: bool = True) -> bool:
    """METRICS 11.10: the extension is active when the report carries `models` by consent and the service
    advertises taskScore. `document` is what parse_discovery returns."""
    return (models_switch and "models" in document.get("sections", []) and document["limits"]["maxModels"] >= 1
            and supported(document.get("extensions"), EXTENSION))


# ------------------------------------------------------------------------------------------------- live values

FORM_RANK = {"live": 2, "resting": 1, "none": 0}


def set_value(values) -> dict:
    """METRICS 6.9 Values for a set: (form, unrounded score) of each agent -> the highest live score, else the
    highest resting score, else none."""
    for form in ("live", "resting"):
        scores = [score for f, score in values if f == form and score is not None]
        if scores:
            return {"score": round_half_away(max(scores)), "form": form}
    return {"score": None, "form": "none"}


def live_object(values, thinking: float) -> dict:
    return {**set_value(values), "thinkingTokS": round_half_away(thinking, 1)}


def empty_row(source: str) -> dict:
    return {"source": source, **{field: 0 for field in COUNT_FIELDS}, "scoreBins": [0] * BINS}


def ordered_row(row: dict) -> dict:
    return {"source": row["source"], **{field: row[field] for field in STAT_FIELDS}}


def add_row(target: dict, row: dict) -> dict:
    for field in COUNT_FIELDS:
        target[field] += row[field]
    target["scoreBins"] = [a + b for a, b in zip(target["scoreBins"], row["scoreBins"])]
    return target


def record_row(record: dict) -> dict | None:
    """What one completed task adds (METRICS 11.10 Counting): None for a task without actions (no 6.11 record)."""
    if record.get("actions", 0) < 1:
        return None
    row = empty_row(record["source"])
    row["completed"] = 1
    score = record.get("score")
    if score is not None:
        shown = round_half_away(score)
        row["scored"] = 1
        row["scoreSum"] = shown
        row["scoreBins"][min(BINS - 1, shown // 10)] = 1
        row["successes"] = 1 if record["success"] else 0
    row["stalls"] = 1 if record["stalled"] else 0
    for field in ("fixAttempts", "fixFailures", "recovered", "recoverAttempts"):
        row[field] = record["counts"][field]
    return row


def merge_rows(rows) -> list:
    merged = {}
    for row in rows:
        target = merged.setdefault(row["source"], empty_row(row["source"]))
        add_row(target, row)
    return [ordered_row(merged[source]) for source in sorted(merged, key=ref.source_order)]


def assemble(inputs: dict) -> dict:
    """METRICS 11.10 Live values, Membership and `other`: the top-level object and the final models[] list.

    inputs: maxModels; agents (the 6.9 set of all enabled sources: id, source, model id resolved as 11.3, form,
    unrounded score); thinking (`total` and per-model unrounded liveThinking); models (the entries of 11.3 before the
    extension: ids with a ledger increment or a generating agent, with their usage fields); ledger (per model id, the
    task ledger's rows)."""
    agents, ledger = inputs["agents"], inputs.get("ledger", {})
    thinking = inputs["thinking"]
    entries = {}
    for model in inputs["models"]:
        entries[model["id"]] = {**copy.deepcopy(model), "sources": list(model["sources"])}

    def entry(model_id):
        return entries.setdefault(model_id, {"id": model_id, "sources": [], "requests": 0, "output": 0,
                                             "measuredOutput": 0, "measuredMs": 0, "generating": 0})

    for agent in agents:
        if agent["form"] == "live":
            target = entry(agent["model"])
            if agent["source"] not in target["sources"]:
                target["sources"].append(agent["source"])
    for model_id, rows in ledger.items():
        target = entry(model_id)
        for row in rows:
            if row["source"] not in target["sources"]:
                target["sources"].append(row["source"])
    built = []
    for model_id, item in entries.items():
        item["sources"] = sorted(item["sources"], key=ref.source_order)
        item["_values"] = [(a["form"], a["score"]) for a in agents if a["model"] == model_id]
        item["_thinking"] = thinking.get("models", {}).get(model_id, 0.0)
        item["_rows"] = list(ledger.get(model_id, []))
        built.append(item)
    built.sort(key=lambda m: (-m["output"], m["id"]))
    limit = inputs.get("maxModels", 16)
    if len(built) > limit:
        kept, rest = built[:limit - 1], built[limit - 1:]
        other = {"id": "other", "sources": sorted({s for m in rest for s in m["sources"]}, key=ref.source_order)}
        for field in ("requests", "output", "measuredOutput", "measuredMs", "generating"):
            other[field] = sum(m[field] for m in rest)
        if any("deltaMicroUsd" in m for m in rest):
            other["deltaMicroUsd"] = sum(m.get("deltaMicroUsd", 0) for m in rest)
        other["_values"] = [value for m in rest for value in m["_values"]]
        other["_thinking"] = ref_seq_sum(m["_thinking"] for m in rest)
        other["_rows"] = [row for m in rest for row in m["_rows"]]
        built = kept + [other]
    models = []
    for item in built:
        out = {key: value for key, value in item.items() if not key.startswith("_")}
        out["taskScore"] = live_object(item["_values"], item["_thinking"])
        out["taskStats"] = merge_rows(item["_rows"])
        models.append(out)
    top = live_object([(a["form"], a["score"]) for a in agents], thinking["total"])
    return {"taskScore": top, "models": models}


def ref_seq_sum(values) -> float:
    total = 0.0
    for value in values:
        total = total + value
    return total


# ------------------------------------------------------------------------------------------------- task ledger

class TaskLedger:
    """METRICS 11.10 Watch, Counting and the task ledger, driven by events (the `ledger` vectors)."""

    def __init__(self):
        self.watch = False
        self.watched: set = set()
        self.rows: dict = {}  # model id -> {source: row}
        self.builds: list = []

    def completed(self) -> int:
        return sum(row["completed"] for rows in self.rows.values() for row in rows.values())

    def take(self) -> dict:
        taken = {model_id: [ordered_row(rows[s]) for s in sorted(rows, key=ref.source_order)]
                 for model_id, rows in sorted(self.rows.items())}
        self.rows = {}
        return taken

    def apply(self, event: dict) -> None:
        if "watch" in event:
            if self.watch and not event["watch"]:
                self.watched.clear()
                self.rows = {}
            self.watch = event["watch"]
        elif "start" in event:
            ids = [event["start"]] if "count" not in event else [f"{event['start']}-{i}" for i in range(event["count"])]
            for task in ids:
                if self.watch and not event.get("seeding") and event["at"] - event["t"] <= FIRST_EVENT_AGE_MS:
                    self.watched.add(task)
        elif "withdraw" in event or "drop" in event:
            self.watched.discard(event.get("withdraw", event.get("drop")))
        elif "complete" in event:
            ids = ([event["complete"]] if "count" not in event
                   else [f"{event['complete']}-{i}" for i in range(event["count"])])
            for task in ids:
                if task not in self.watched:
                    continue
                self.watched.discard(task)
                row = record_row(event["record"])
                if row is None or self.completed() >= MAX_COMPLETED:
                    continue
                target = self.rows.setdefault(event["record"]["model"], {})
                add_row(target.setdefault(row["source"], empty_row(row["source"])), row)
        elif "build" in event:
            taken = self.take()
            self.builds.append({"seq": event["build"], "taskStats": taken if event.get("models", True) else None})
        elif "relaunch" in event:
            self.watch = False
            self.watched.clear()
            self.rows = {}
        # "discard" (quiet, a gap, not ready), "newRun" and "lost" leave the task ledger as it is.


def simulate_ledger(case: dict) -> dict:
    ledger = TaskLedger()
    for event in case["events"]:
        ledger.apply(event)
    return {"builds": ledger.builds, "watched": sorted(ledger.watched), "pending": ledger.take()}


# ------------------------------------------------------------------------------------------------- checks

def _live_errors(value: dict, pointer: str) -> list:
    if (value["score"] is None) != (value["form"] == "none"):
        return [(pointer + "/score", "score must be null exactly when form is none")]
    return []


def _row_errors(row: dict, pointer: str) -> list:
    errors = []
    bins = row["scoreBins"]
    low = sum(10 * index * count for index, count in enumerate(bins))
    high = sum(min(100, 10 * index + 9) * count for index, count in enumerate(bins))
    if row["scored"] > row["completed"]:
        errors.append((pointer + "/scored", "above completed"))
    if sum(bins) != row["scored"]:
        errors.append((pointer + "/scoreBins", "the bins do not add up to scored"))
    elif not low <= row["scoreSum"] <= high:
        errors.append((pointer + "/scoreSum", "outside the bounds of the bins"))
    if row["successes"] > row["scored"]:
        errors.append((pointer + "/successes", "above scored"))
    if row["stalls"] > row["completed"]:
        errors.append((pointer + "/stalls", "above completed"))
    if row["fixFailures"] > row["fixAttempts"]:
        errors.append((pointer + "/fixFailures", "above fixAttempts"))
    if row["recovered"] > row["recoverAttempts"]:
        errors.append((pointer + "/recovered", "above recoverAttempts"))
    if row["recoverAttempts"] > row["fixAttempts"]:
        errors.append((pointer + "/recoverAttempts", "above fixAttempts"))
    return errors


def errors(report: dict) -> list:
    """What a service that advertises taskScore rejects with 422 payload_invalid, as (JSON pointer, reason), after
    the schema check. Rows are checked in order; the service reports any one problem."""
    problems = []
    top = report.get("taskScore")
    if top is not None:
        problems += _live_errors(top, "/taskScore")
    completed = 0
    for index, model in enumerate(report.get("models", [])):
        pointer = f"/models/{index}"
        if top is None:
            problems += [(f"{pointer}/{key}", "present without the top-level taskScore")
                         for key in ("taskScore", "taskStats") if key in model]
            continue
        for key in ("taskScore", "taskStats"):
            if key not in model:
                problems.append((f"{pointer}/{key}", "every model entry carries it with the top-level taskScore"))
        if "taskScore" in model:
            problems += _live_errors(model["taskScore"], pointer + "/taskScore")
        seen = set()
        for row_index, row in enumerate(model.get("taskStats", [])):
            row_pointer = f"{pointer}/taskStats/{row_index}"
            if row["source"] not in model["sources"] or row["source"] in seen:
                problems.append((row_pointer + "/source", "source must be unique and belong to the model"))
            seen.add(row["source"])
            problems += _row_errors(row, row_pointer)
            completed += row["completed"]
    if completed > MAX_COMPLETED:
        problems.append(("/models", f"more than {MAX_COMPLETED} completed tasks in one report"))
    return problems


def conformance(report: dict) -> list:
    """Client rules that services do not reject on (REPORTING 6.9): order, rounding and the set relations."""
    top = report.get("taskScore")
    if top is None:
        return []
    problems = []
    entries = [m for m in report.get("models", []) if "taskScore" in m]
    for value, name in [(top, "taskScore")] + [(m["taskScore"], f"models.{m['id']}.taskScore") for m in entries]:
        if not ref._decimals_ok(value["thinkingTokS"], 1):
            problems.append(f"{name}.thinkingTokS: more than one decimal")
        if (FORM_RANK[value["form"]], value["score"] or 0) > (FORM_RANK[top["form"]], top["score"] or 0):
            problems.append(f"{name}: above the top-level value, which is the highest of every agent")
    total = sum(m["taskScore"]["thinkingTokS"] for m in entries)
    if total > top["thinkingTokS"] + 0.05 * len(entries) + 1e-6:
        problems.append("models: thinkingTokS adds up to more than the top-level value")
    for model in entries:
        listed = [row["source"] for row in model.get("taskStats", [])]
        if listed != sorted(listed, key=ref.source_order):
            problems.append(f"models.{model['id']}.taskStats: not in source order")
    return problems


def strip(report: dict) -> dict:
    """The body a service that does not advertise taskScore validates and keeps: the extension's members removed."""
    if "taskScore" not in report and not any("taskScore" in m or "taskStats" in m for m in report.get("models", [])
                                             if isinstance(m, dict)):
        return report
    copied = {key: value for key, value in report.items() if key != "taskScore"}
    if isinstance(report.get("models"), list):
        copied["models"] = [{k: v for k, v in m.items() if k not in ("taskScore", "taskStats")}
                            if isinstance(m, dict) else m for m in report["models"]]
    return copied


# ------------------------------------------------------------------------------------------------- receiver

class Board:
    """REPORTING-EXTENSIONS 9.6: live values from each online device's latest accepted report, increments summed
    once per accepted report. Not production storage."""

    def __init__(self):
        self.live: dict = {}          # device id -> (top-level taskScore, {model id: taskScore}) or None
        self.contributions: dict = {}  # device id -> {(model id, source): row}

    def record(self, device_id: str, report: dict) -> None:
        top = report.get("taskScore")
        models = report.get("models", [])
        self.live[device_id] = None if top is None else (
            top, {m["id"]: m["taskScore"] for m in models if "taskScore" in m})
        rows = self.contributions.setdefault(device_id, {})
        for model in models:
            for row in model.get("taskStats", []):
                key = (model["id"], row["source"])
                add_row(rows.setdefault(key, empty_row(row["source"])), row)

    def forget(self, device_id: str) -> None:
        self.live.pop(device_id, None)
        self.contributions.pop(device_id, None)

    def leaderboard(self, online: list) -> dict:
        """online: the device ids that are online (REPORTING 6.10 presence), in any order."""
        totals: dict = {}
        for rows in self.contributions.values():
            for (model_id, source), row in rows.items():
                by_source = totals.setdefault(model_id, {})
                add_row(by_source.setdefault(source, empty_row(source)), row)
        live: dict = {}
        top_values, top_thinking = [], 0.0
        for device_id in sorted(online):
            state = self.live.get(device_id)
            if state is None:
                continue
            top, per_model = state
            top_values.append((top["form"], top["score"]))
            top_thinking += top["thinkingTokS"]
            for model_id, value in per_model.items():
                item = live.setdefault(model_id, {"values": [], "thinking": 0.0, "devices": 0})
                item["values"].append((value["form"], value["score"]))
                item["thinking"] += value["thinkingTokS"]
                item["devices"] += 1
        rows = []
        for model_id in sorted(set(totals) | set(live)):
            by_source = totals.get(model_id, {})
            total = empty_row("")
            for row in by_source.values():
                add_row(total, row)
            total.pop("source")
            stats = {field: total[field] for field in STAT_FIELDS}
            item = live.get(model_id)
            rows.append({
                "id": model_id,
                "ranked": model_id not in UNRANKED,
                "stats": stats,
                "bySource": [ordered_row(by_source[s]) for s in sorted(by_source, key=ref.source_order)],
                "meanScore": stats["scoreSum"] / stats["scored"] if stats["scored"] else None,
                "successRate": stats["successes"] / stats["scored"] if stats["scored"] else None,
                "stallRate": stats["stalls"] / stats["completed"] if stats["completed"] else None,
                "fixFailureRate": stats["fixFailures"] / stats["fixAttempts"] if stats["fixAttempts"] else None,
                "recoverAttempts": stats["recoverAttempts"] / stats["recovered"] if stats["recovered"] else None,
                "live": None if item is None else {**set_value(item["values"]),
                                                   "thinkingTokS": round_half_away(item["thinking"], 1),
                                                   "devices": item["devices"]},
            })
        return {"live": {**set_value(top_values), "thinkingTokS": round_half_away(top_thinking, 1)},
                "models": rows}


def simulate_receiver(case: dict) -> dict:
    board = Board()
    for step in case["reports"]:
        if step.get("forget"):
            board.forget(step["device"])
        elif step.get("accepted", True):
            board.record(step["device"], step["body"])
    return board.leaderboard(case["online"])


# ------------------------------------------------------------------------------------------------- vectors

def _record(model, source, score, success=False, stalled=False, actions=5, fix=(0, 0), rec=(0, 0)):
    return {"model": model, "source": source, "actions": actions, "score": score, "success": success,
            "stalled": stalled, "counts": {"fixAttempts": fix[0], "fixFailures": fix[1], "recovered": rec[0],
                                           "recoverAttempts": rec[1]}}


def _row(source, completed=1, scored=1, score_sum=0, bins=None, successes=0, stalls=0, fix=(0, 0), rec=(0, 0)):
    bins = bins or {}
    return {"source": source, "completed": completed, "scored": scored, "scoreSum": score_sum,
            "scoreBins": [bins.get(index, 0) for index in range(BINS)], "successes": successes, "stalls": stalls,
            "fixAttempts": fix[0], "fixFailures": fix[1], "recovered": rec[0], "recoverAttempts": rec[1]}


OPUS, GPT, SONNET = "claude-opus-5-5", "gpt-5.5", "claude-sonnet-5-5"
T = 1_800_000_000_000

CAPABILITY_CASES = [
    {"name": "advertised with the models section and the switch on: active",
     "document": {"sections": ["sources", "models", "cost"], "extensions": {"taskScore": 1}}, "modelsSwitch": True,
     "expect": True},
    {"name": "no sections list means every section: active", "document": {"extensions": {"taskScore": 1}},
     "modelsSwitch": True, "expect": True},
    {"name": "the user turned the models switch off: inactive, nothing of the extension is sent",
     "document": {"extensions": {"taskScore": 1}}, "modelsSwitch": False, "expect": False},
    {"name": "the service does not list models: inactive", "document": {"sections": ["sources", "cost"],
                                                                        "extensions": {"taskScore": 1}},
     "modelsSwitch": True, "expect": False},
    {"name": "maxModels 0 sends no models: inactive", "document": {"limits": {"maxModels": 0},
                                                                    "extensions": {"taskScore": 1}},
     "modelsSwitch": True, "expect": False},
    {"name": "not advertised: inactive (older services)", "document": {"extensions": {"executionModes": 1}},
     "modelsSwitch": True, "expect": False},
    {"name": "true is not the integer 1", "document": {"extensions": {"taskScore": True}}, "modelsSwitch": True,
     "expect": False},
    {"name": "the string \"1\" is not the integer 1", "document": {"extensions": {"taskScore": "1"}},
     "modelsSwitch": True, "expect": False},
    {"name": "another version is unknown", "document": {"extensions": {"taskScore": 2}}, "modelsSwitch": True,
     "expect": False},
    {"name": "null is absent", "document": {"extensions": {"taskScore": None}}, "modelsSwitch": True,
     "expect": False},
]

RECORD_CASES = [
    {"name": "a passing task: its score rounded half away from zero, bin 9 for 90 to 100",
     "record": _record(OPUS, "claudeCode", 93.75, success=True, fix=(2, 0), rec=(1, 2)),
     "expect": _row("claudeCode", score_sum=94, bins={9: 1}, successes=1, fix=(2, 0), rec=(1, 2))},
    {"name": "9.5 rounds to 10 and lands in bin 1; the bins use the rounded score",
     "record": _record(GPT, "codex", 9.5), "expect": _row("codex", score_sum=10, bins={1: 1})},
    {"name": "99.5 rounds to 100, still bin 9",
     "record": _record(GPT, "codex", 99.5, success=True), "expect": _row("codex", score_sum=100, bins={9: 1},
                                                                          successes=1)},
    {"name": "a task without a score counts as completed only (and may stall)",
     "record": _record(SONNET, "claudeDesktop", None, stalled=True),
     "expect": _row("claudeDesktop", scored=0, stalls=1)},
    {"name": "a failing task that never fixed its check: every failing run is a fix failure",
     "record": _record(SONNET, "claudeDesktop", 23.0, stalled=True, fix=(4, 4)),
     "expect": _row("claudeDesktop", score_sum=23, bins={2: 1}, stalls=1, fix=(4, 4))},
    {"name": "a task without actions has no 6.11 record and adds nothing",
     "record": _record(OPUS, "claudeCode", None, actions=0), "expect": None},
]

ASSEMBLY_CASES = [
    {"name": "top-level and per-model values; a model worked with in the tools phase and a model with only a "
             "completion get entries; a resting-only model does not",
     "input": {"maxModels": 16,
               "agents": [
                   {"id": "a1", "source": "claudeCode", "model": OPUS, "form": "live", "score": 91.5},
                   {"id": "a2", "source": "claudeCode", "model": OPUS, "form": "resting", "score": 97.0},
                   {"id": "a3", "source": "codex", "model": GPT, "form": "live", "score": 63.75},
                   {"id": "a4", "source": "omp", "model": "glm-5", "form": "live", "score": 40.0},
                   {"id": "a5", "source": "pi", "model": "kimi-k3", "form": "resting", "score": 99.0},
                   {"id": "a6", "source": "claudeDesktop", "model": SONNET, "form": "resting", "score": 23.0},
                   {"id": "a7", "source": "opencode", "model": "deepseek-v4", "form": "none", "score": None}],
               "thinking": {"total": 38.64, "models": {GPT: 38.64}},
               "models": [{"id": OPUS, "sources": ["claudeCode"], "requests": 3, "output": 1500,
                           "measuredOutput": 1400, "measuredMs": 3300, "generating": 2},
                          {"id": GPT, "sources": ["codex"], "requests": 1, "output": 340, "measuredOutput": 302,
                           "measuredMs": 820, "generating": 1},
                          {"id": "deepseek-v4", "sources": ["opencode"], "requests": 0, "output": 0,
                           "measuredOutput": 0, "measuredMs": 0, "generating": 1}],
               "ledger": {SONNET: [_row("claudeDesktop", score_sum=23, bins={2: 1}, stalls=1, fix=(4, 4))],
                          OPUS: [_row("claudeCode", completed=2, scored=2, score_sum=141, bins={4: 1, 9: 1},
                                      successes=1, fix=(3, 1), rec=(1, 2))]}},
     "expect": {
         "taskScore": {"score": 92, "form": "live", "thinkingTokS": 38.6},
         "models": [
             {"id": OPUS, "sources": ["claudeCode"], "requests": 3, "output": 1500, "measuredOutput": 1400,
              "measuredMs": 3300, "generating": 2, "taskScore": {"score": 92, "form": "live", "thinkingTokS": 0.0},
              "taskStats": [_row("claudeCode", completed=2, scored=2, score_sum=141, bins={4: 1, 9: 1}, successes=1,
                                 fix=(3, 1), rec=(1, 2))]},
             {"id": GPT, "sources": ["codex"], "requests": 1, "output": 340, "measuredOutput": 302,
              "measuredMs": 820, "generating": 1, "taskScore": {"score": 64, "form": "live", "thinkingTokS": 38.6},
              "taskStats": []},
             {"id": SONNET, "sources": ["claudeDesktop"], "requests": 0, "output": 0, "measuredOutput": 0,
              "measuredMs": 0, "generating": 0, "taskScore": {"score": 23, "form": "resting", "thinkingTokS": 0.0},
              "taskStats": [_row("claudeDesktop", score_sum=23, bins={2: 1}, stalls=1, fix=(4, 4))]},
             {"id": "deepseek-v4", "sources": ["opencode"], "requests": 0, "output": 0, "measuredOutput": 0,
              "measuredMs": 0, "generating": 1, "taskScore": {"score": None, "form": "none", "thinkingTokS": 0.0},
              "taskStats": []},
             {"id": "glm-5", "sources": ["omp"], "requests": 0, "output": 0, "measuredOutput": 0, "measuredMs": 0,
              "generating": 0, "taskScore": {"score": 40, "form": "live", "thinkingTokS": 0.0}, "taskStats": []}]}},
    {"name": "beyond maxModels the rest merges into other: the highest value live before resting, thinking summed "
             "before rounding, rows summed per source",
     "input": {"maxModels": 2,
               "agents": [
                   {"id": "a1", "source": "claudeCode", "model": OPUS, "form": "live", "score": 50.0},
                   {"id": "a2", "source": "codex", "model": GPT, "form": "resting", "score": 88.0},
                   {"id": "a3", "source": "claudeCode", "model": SONNET, "form": "live", "score": 12.25}],
               "thinking": {"total": 20.08, "models": {OPUS: 10.0, GPT: 10.04, SONNET: 0.04}},
               "models": [{"id": OPUS, "sources": ["claudeCode"], "requests": 2, "output": 900, "measuredOutput": 900,
                           "measuredMs": 3000, "generating": 1},
                          {"id": GPT, "sources": ["codex"], "requests": 1, "output": 300, "measuredOutput": 300,
                           "measuredMs": 1000, "generating": 0},
                          {"id": SONNET, "sources": ["claudeCode"], "requests": 1, "output": 100,
                           "measuredOutput": 100, "measuredMs": 500, "generating": 1}],
               "ledger": {GPT: [_row("codex", score_sum=88, bins={8: 1}, successes=1)],
                          SONNET: [_row("claudeCode", score_sum=41, bins={4: 1}, fix=(2, 1), rec=(1, 1))]}},
     "expect": {
         "taskScore": {"score": 50, "form": "live", "thinkingTokS": 20.1},
         "models": [
             {"id": OPUS, "sources": ["claudeCode"], "requests": 2, "output": 900, "measuredOutput": 900,
              "measuredMs": 3000, "generating": 1, "taskScore": {"score": 50, "form": "live", "thinkingTokS": 10.0},
              "taskStats": []},
             {"id": "other", "sources": ["codex", "claudeCode"], "requests": 2, "output": 400,
              "measuredOutput": 400, "measuredMs": 1500, "generating": 1,
              "taskScore": {"score": 12, "form": "live", "thinkingTokS": 10.1},
              "taskStats": [_row("codex", score_sum=88, bins={8: 1}, successes=1),
                            _row("claudeCode", score_sum=41, bins={4: 1}, fix=(2, 1), rec=(1, 1))]}]}},
    {"name": "no agent has a score: none everywhere, thinking still reported",
     "input": {"maxModels": 16,
               "agents": [{"id": "a1", "source": "gemini", "model": "gemini-3-pro", "form": "none", "score": None}],
               "thinking": {"total": 4.25, "models": {"gemini-3-pro": 4.25}},
               "models": [{"id": "gemini-3-pro", "sources": ["gemini"], "requests": 1, "output": 50,
                           "measuredOutput": 50, "measuredMs": 400, "generating": 1}]},
     "expect": {
         "taskScore": {"score": None, "form": "none", "thinkingTokS": 4.3},
         "models": [{"id": "gemini-3-pro", "sources": ["gemini"], "requests": 1, "output": 50, "measuredOutput": 50,
                     "measuredMs": 400, "generating": 1,
                     "taskScore": {"score": None, "form": "none", "thinkingTokS": 4.3}, "taskStats": []}]}},
]

_DONE = _record(OPUS, "claudeCode", 86.4, success=True, fix=(1, 0), rec=(1, 1))
_DONE_ROW = _row("claudeCode", score_sum=86, bins={8: 1}, successes=1, fix=(1, 0), rec=(1, 1))
_GPT_DONE = _record(GPT, "codex", 55.0, fix=(3, 1), rec=(1, 2))
_GPT_ROW = _row("codex", score_sum=55, bins={5: 1}, fix=(3, 1), rec=(1, 2))

LEDGER_CASES = [
    {"name": "a task watched from its start counts once, when it completes, in the next built report",
     "events": [{"at": T, "watch": True}, {"at": T + 1000, "start": "x", "t": T + 900},
                {"at": T + 5000, "build": 1}, {"at": T + 9000, "complete": "x", "record": _DONE},
                {"at": T + 10_000, "build": 2}, {"at": T + 15_000, "build": 3}],
     "expect": {"builds": [{"seq": 1, "taskStats": {}}, {"seq": 2, "taskStats": {OPUS: [_DONE_ROW]}},
                           {"seq": 3, "taskStats": {}}], "watched": [], "pending": {}}},
    {"name": "history is never counted: a task read while seeding, one that started before reporting was on, and "
             "one whose first event is more than 2 minutes old when read (a session found late)",
     "events": [{"at": T, "start": "seeded", "t": T - 60_000, "seeding": True},
                {"at": T + 1000, "start": "early", "t": T + 1000},
                {"at": T + 2000, "watch": True},
                {"at": T + 3000, "start": "late", "t": T + 3000 - 120_001},
                {"at": T + 4000, "start": "edge", "t": T + 4000 - 120_000},
                {"at": T + 6000, "complete": "seeded", "record": _DONE},
                {"at": T + 6000, "complete": "early", "record": _DONE},
                {"at": T + 6000, "complete": "late", "record": _DONE},
                {"at": T + 6000, "complete": "edge", "record": _GPT_DONE},
                {"at": T + 7000, "build": 1}],
     "expect": {"builds": [{"seq": 1, "taskStats": {GPT: [_GPT_ROW]}}], "watched": [], "pending": {}}},
    {"name": "a break (pause, models switch off, the service dropping the extension, network off) drops every "
             "watched task and every pending completion; tasks that start afterwards count",
     "events": [{"at": T, "watch": True}, {"at": T + 1000, "start": "a", "t": T + 1000},
                {"at": T + 1000, "start": "b", "t": T + 1000},
                {"at": T + 2000, "complete": "a", "record": _DONE},
                {"at": T + 3000, "watch": False}, {"at": T + 60_000, "watch": True},
                {"at": T + 61_000, "complete": "b", "record": _DONE},
                {"at": T + 62_000, "start": "c", "t": T + 62_000},
                {"at": T + 63_000, "complete": "c", "record": _GPT_DONE},
                {"at": T + 65_000, "build": 1}],
     "expect": {"builds": [{"seq": 1, "taskStats": {GPT: [_GPT_ROW]}}], "watched": [], "pending": {}}},
    {"name": "a leaving report on a user action takes the pending completions before the break empties the ledger",
     "events": [{"at": T, "watch": True}, {"at": T + 1000, "start": "a", "t": T + 1000},
                {"at": T + 1000, "start": "b", "t": T + 1000},
                {"at": T + 2000, "complete": "a", "record": _DONE},
                {"at": T + 3000, "build": 1, "activity": "leaving"}, {"at": T + 3000, "watch": False}],
     "expect": {"builds": [{"seq": 1, "taskStats": {OPUS: [_DONE_ROW]}}], "watched": [], "pending": {}}},
    {"name": "quiet, sleep gaps, backoff and new runs keep the task ledger; a built report that is lost loses its "
             "completions; a report whose models were dropped for size loses them too",
     "events": [{"at": T, "watch": True},
                {"at": T + 1000, "start": "a", "t": T + 1000}, {"at": T + 1000, "start": "b", "t": T + 1000},
                {"at": T + 1000, "start": "c", "t": T + 1000}, {"at": T + 1000, "start": "d", "t": T + 1000},
                {"at": T + 1_801_000, "complete": "a", "record": _DONE},
                {"at": T + 1_801_000, "discard": "quiet"}, {"at": T + 1_900_000, "discard": "gap"},
                {"at": T + 1_905_000, "build": 1, "lost": True},
                {"at": T + 1_906_000, "complete": "b", "record": _GPT_DONE},
                {"at": T + 1_907_000, "newRun": "seq_conflict"}, {"at": T + 1_907_000, "discard": "notReady"},
                {"at": T + 1_912_000, "build": 1},
                {"at": T + 1_913_000, "complete": "c", "record": _DONE},
                {"at": T + 1_917_000, "build": 2, "models": False},
                {"at": T + 1_918_000, "complete": "d", "record": _GPT_DONE},
                {"at": T + 1_922_000, "build": 3}],
     "expect": {"builds": [{"seq": 1, "taskStats": {OPUS: [_DONE_ROW]}}, {"seq": 1, "taskStats": {GPT: [_GPT_ROW]}},
                           {"seq": 2, "taskStats": None}, {"seq": 3, "taskStats": {GPT: [_GPT_ROW]}}],
                "watched": [], "pending": {}}},
    {"name": "a withdrawn prompt never started a task; a task without actions adds nothing; an agent whose file or "
             "source went away drops its task; rows add up per model and source",
     "events": [{"at": T, "watch": True},
                {"at": T + 1000, "start": "w", "t": T + 1000}, {"at": T + 1500, "withdraw": "w"},
                {"at": T + 2000, "start": "empty", "t": T + 2000}, {"at": T + 2000, "start": "gone", "t": T + 2000},
                {"at": T + 2000, "start": "a", "t": T + 2000}, {"at": T + 2000, "start": "b", "t": T + 2000},
                {"at": T + 2000, "start": "c", "t": T + 2000},
                {"at": T + 3000, "complete": "w", "record": _DONE},
                {"at": T + 3000, "complete": "empty", "record": _record(OPUS, "claudeCode", None, actions=0)},
                {"at": T + 3000, "drop": "gone"}, {"at": T + 3500, "complete": "gone", "record": _DONE},
                {"at": T + 4000, "complete": "a", "record": _DONE},
                {"at": T + 4000, "complete": "b", "record": _record(OPUS, "claudeCode", 9.5, stalled=True)},
                {"at": T + 4000, "complete": "c", "record": _record(OPUS, "claudeDesktop", 70.0, success=True)},
                {"at": T + 5000, "build": 1}],
     "expect": {"builds": [{"seq": 1, "taskStats": {OPUS: [
         _row("claudeCode", completed=2, scored=2, score_sum=96, bins={1: 1, 8: 1}, successes=1, stalls=1,
              fix=(1, 0), rec=(1, 1)),
         _row("claudeDesktop", score_sum=70, bins={7: 1}, successes=1)]}}], "watched": [], "pending": {}}},
    {"name": "relaunch: nothing survives; the task rebuilt from its file after the launch is history",
     "events": [{"at": T, "watch": True}, {"at": T + 1000, "start": "a", "t": T + 1000},
                {"at": T + 2000, "start": "b", "t": T + 2000}, {"at": T + 3000, "complete": "b", "record": _DONE},
                {"at": T + 4000, "relaunch": True}, {"at": T + 10_000, "watch": True},
                {"at": T + 11_000, "complete": "a", "record": _DONE}, {"at": T + 15_000, "build": 1}],
     "expect": {"builds": [{"seq": 1, "taskStats": {}}], "watched": [], "pending": {}}},
    {"name": "at most 1000 completed tasks wait for one report; later completions are dropped, not deferred",
     "events": [{"at": T, "watch": True}, {"at": T + 1000, "start": "bulk", "t": T + 1000, "count": 1001},
                {"at": T + 2000, "complete": "bulk", "count": 1001, "record": _record(GPT, "codex", None)},
                {"at": T + 5000, "build": 1}],
     "expect": {"builds": [{"seq": 1, "taskStats": {GPT: [_row("codex", completed=1000, scored=0)]}}],
                "watched": [], "pending": {}}},
]


def _example(name: str) -> dict:
    return copy.deepcopy(ref.load_examples()[name])


def _validation_cases() -> list:
    good = _example("report-taskscore")
    cases = [{"name": "the example body is valid", "body": ref.compact_json(good).decode("utf-8"), "expect": []}]

    def mutate(name, change, expect):
        body = copy.deepcopy(good)
        change(body)
        cases.append({"name": name, "body": ref.compact_json(body).decode("utf-8"), "expect": expect})

    mutate("a body without the extension is valid (an older client, or models not shared)",
           lambda b: [b.pop("taskScore")] + [(m.pop("taskScore"), m.pop("taskStats")) for m in b["models"]], [])
    mutate("models dropped for size: the top-level object alone is valid", lambda b: b.pop("models"), [])
    mutate("form none needs a null score", lambda b: b["taskScore"].update(form="none"), ["/taskScore/score"])
    mutate("a null score needs form none",
           lambda b: b["models"][1]["taskScore"].update(score=None), ["/models/1/taskScore/score"])
    mutate("a score above 100", lambda b: b["taskScore"].update(score=101), ["/taskScore/score"])
    mutate("an unknown form", lambda b: b["taskScore"].update(form="sleeping"), ["/taskScore/form"])
    mutate("a model entry without taskStats while the top level has taskScore",
           lambda b: b["models"][1].pop("taskStats"), ["/models/1/taskStats"])
    mutate("per-model fields without the top-level object", lambda b: b.pop("taskScore"),
           ["/models/0/taskScore", "/models/0/taskStats", "/models/1/taskScore", "/models/1/taskStats",
            "/models/2/taskScore", "/models/2/taskStats"])
    mutate("a row whose source the model does not list",
           lambda b: b["models"][0]["taskStats"][0].update(source="codex"), ["/models/0/taskStats/0/source"])
    mutate("two rows for one source",
           lambda b: b["models"][0]["taskStats"].append(copy.deepcopy(b["models"][0]["taskStats"][0])),
           ["/models/0/taskStats/1/source"])
    mutate("a row with no completed task", lambda b: b["models"][0]["taskStats"][0].update(completed=0),
           ["/models/0/taskStats/0/completed"])
    mutate("scored above completed",
           lambda b: b["models"][0]["taskStats"][0].update(scored=3, scoreBins=[0, 0, 0, 0, 1, 0, 0, 0, 1, 1],
                                                          scoreSum=235),
           ["/models/0/taskStats/0/scored"])
    mutate("bins that do not add up to scored",
           lambda b: b["models"][0]["taskStats"][0].update(scoreBins=[0, 0, 0, 0, 1, 0, 0, 0, 0, 0]),
           ["/models/0/taskStats/0/scoreBins"])
    mutate("nine bins", lambda b: b["models"][0]["taskStats"][0].update(scoreBins=[0, 0, 0, 0, 1, 0, 0, 0, 1]),
           ["/models/0/taskStats/0/scoreBins"])
    mutate("a score sum outside its bins (two tasks in bins 4 and 9 sum to 130-149)",
           lambda b: b["models"][0]["taskStats"][0].update(scoreSum=150), ["/models/0/taskStats/0/scoreSum"])
    mutate("more successes than scored tasks",
           lambda b: b["models"][0]["taskStats"][0].update(successes=3), ["/models/0/taskStats/0/successes"])
    mutate("more fix failures than attempts",
           lambda b: b["models"][0]["taskStats"][0].update(fixFailures=4), ["/models/0/taskStats/0/fixFailures"])
    mutate("recovered episodes without as many attempts",
           lambda b: b["models"][0]["taskStats"][0].update(recovered=3), ["/models/0/taskStats/0/recovered"])
    mutate("recovery attempts above the fix attempts",
           lambda b: b["models"][0]["taskStats"][0].update(recoverAttempts=4),
           ["/models/0/taskStats/0/recoverAttempts"])

    def flood(body):
        rows = [_row(source, completed=500, scored=0) for source in ("claudeCode",)]
        body["models"][0]["taskStats"] = rows
        body["models"][1]["taskStats"] = [_row("codex", completed=501, scored=0)]
    mutate("more than 1000 completed tasks in one report", flood, ["/models"])
    return cases


def body_problems(body: dict, schema: dict) -> list:
    """The pointers a service that advertises taskScore answers with 422 payload_invalid (any one of them)."""
    found = [ref.json_pointer(error) for error in ref.schema_errors(body, schema["$defs"]["report"], schema)]
    if found:
        return found
    return [pointer for pointer, _ in errors(body)]


RECEIVER_DEVICES = ("6f1c2a9e-3b4d-4e5f-8a7b-9c0d1e2f3a4b", "0b7e4d2c-9a1f-4c3e-b5d6-7e8f9a0b1c2d",
                    "5a6b7c8d-9e0f-4a1b-8c2d-3e4f5a6b7c8d")


def _body(top, models):
    return {"taskScore": top, "models": models}


def _entry(model_id, sources, value, rows):
    return {"id": model_id, "sources": sources, "taskScore": value, "taskStats": rows}


def _receiver_cases() -> list:
    d1, d2, d3 = RECEIVER_DEVICES
    live = lambda score, thinking=0.0: {"score": score, "form": "live", "thinkingTokS": thinking}
    resting = lambda score: {"score": score, "form": "resting", "thinkingTokS": 0.0}
    row_a = _row("claudeCode", completed=2, scored=2, score_sum=141, bins={4: 1, 9: 1}, successes=1, fix=(3, 1),
                 rec=(1, 2))
    row_b = _row("claudeCode", score_sum=86, bins={8: 1}, successes=1, fix=(1, 0), rec=(1, 1))
    row_c = _row("claudeDesktop", score_sum=23, bins={2: 1}, stalls=1, fix=(4, 4))
    row_d = _row("codex", completed=3, scored=2, score_sum=120, bins={5: 1, 6: 1}, successes=1, stalls=1,
                 fix=(5, 2), rec=(2, 3))
    reports = [
        {"device": d1, "body": _body(live(92, 12.0), [_entry(OPUS, ["claudeCode"], live(92, 12.0), [row_a])])},
        {"device": d1, "accepted": False, "note": "a duplicate (same run and seq) is never recorded again",
         "body": _body(live(92, 12.0), [_entry(OPUS, ["claudeCode"], live(92, 12.0), [row_a])])},
        {"device": d2, "body": _body(live(64, 30.25), [_entry(GPT, ["codex"], live(64, 30.25), [row_d]),
                                                       _entry(OPUS, ["claudeCode"], resting(97), [row_b])])},
        {"device": d1, "body": _body(resting(92), [_entry(SONNET, ["claudeDesktop"], resting(23), [row_c])])},
        {"device": d3, "body": _body(live(99, 5.0), [_entry(OPUS, ["claudeCode"], live(99, 5.0), [row_b])])},
        {"device": d3, "forget": True, "note": "forget this device: its contributions and live values go"},
        {"device": d2, "body": {"models": [{"id": GPT, "sources": ["codex"]}]},
         "note": "a later report without the extension (models off or an older client) clears the live values, "
                 "keeps the sums"},
    ]

    def stats(row_list):
        total = empty_row("")
        for row in row_list:
            add_row(total, row)
        total.pop("source")
        return {field: total[field] for field in STAT_FIELDS}

    def model(model_id, by_source, live_value):
        s = stats(by_source)
        return {"id": model_id, "ranked": True, "stats": s, "bySource": by_source,
                "meanScore": s["scoreSum"] / s["scored"] if s["scored"] else None,
                "successRate": s["successes"] / s["scored"] if s["scored"] else None,
                "stallRate": s["stalls"] / s["completed"] if s["completed"] else None,
                "fixFailureRate": s["fixFailures"] / s["fixAttempts"] if s["fixAttempts"] else None,
                "recoverAttempts": s["recoverAttempts"] / s["recovered"] if s["recovered"] else None,
                "live": live_value}

    opus_row = add_row(copy.deepcopy(row_a), row_b)
    return [{
        "name": "live values from each online device's latest report (highest, live before resting; thinking "
                "summed); increments summed once per accepted report; a forgotten device leaves no trace",
        "reports": reports,
        "online": [d1, d2],
        "expect": {
            "live": {"score": 92, "form": "resting", "thinkingTokS": 0.0},
            "models": [
                model(OPUS, [ordered_row(opus_row)], None),
                model(SONNET, [row_c], {"score": 23, "form": "resting", "thinkingTokS": 0.0, "devices": 1}),
                model(GPT, [row_d], None),
            ]}}]


def build_vectors() -> dict:
    return {
        "_comment": "The taskScore extension (METRICS 11.10, REPORTING-EXTENSIONS 9), written by "
                    "spec/tools/report_reference.py --write from spec/tools/report_task_score.py, whose expectations "
                    "are written by hand. capability: discovery and the models switch -> active. records: one "
                    "completed task -> its row. assembly: agents' values, per-model thinking, the models of 11.3 "
                    "and the task ledger -> the top-level taskScore and the final models[]. ledger: events in order "
                    "-> what each built report carries (null: models dropped for size). validation: a body (compact "
                    "JSON text) -> the JSON pointers a service that advertises taskScore may name in 422 "
                    "payload_invalid (empty: accepted). receiver: accepted reports -> the merged model leaderboard.",
        "capability": CAPABILITY_CASES,
        "records": RECORD_CASES,
        "assembly": ASSEMBLY_CASES,
        "ledger": LEDGER_CASES,
        "validation": _validation_cases(),
        "receiver": _receiver_cases(),
    }


def _close(a, b) -> bool:
    if isinstance(a, float) or isinstance(b, float):
        return isinstance(a, (int, float)) and isinstance(b, (int, float)) and abs(a - b) <= 1e-9
    if isinstance(a, dict) and isinstance(b, dict):
        return a.keys() == b.keys() and all(_close(a[k], b[k]) for k in a)
    if isinstance(a, list) and isinstance(b, list):
        return len(a) == len(b) and all(_close(x, y) for x, y in zip(a, b))
    return a == b


def check_vectors(vectors: dict) -> list:
    failures = []
    schema = ref.load_schema()
    for case in vectors["capability"]:
        document = {"protocol": "agent-speed-report", "versions": [1],
                    "service": {"name": "x", "terms": "https://example.com/t", "privacy": "https://example.com/p"},
                    "api": "https://api.example.com", **case["document"]}
        got = active(ref.parse_discovery(document, ref.HTTPS_DISCOVERY), case["modelsSwitch"])
        if got != case["expect"]:
            failures.append(f"taskScore.capability: {case['name']}: {got}")
    for case in vectors["records"]:
        if record_row(case["record"]) != case["expect"]:
            failures.append(f"taskScore.records: {case['name']}: {record_row(case['record'])}")
    for case in vectors["assembly"]:
        got = assemble(case["input"])
        if got != case["expect"]:
            failures.append(f"taskScore.assembly: {case['name']}: {got}")
    for case in vectors["ledger"]:
        got = simulate_ledger(case)
        if got != case["expect"]:
            failures.append(f"taskScore.ledger: {case['name']}: {got}")
    for case in vectors["validation"]:
        got = body_problems(ref.strict_json(case["body"].encode("utf-8")), schema)
        if got != case["expect"]:
            failures.append(f"taskScore.validation: {case['name']}: {got}")
    for case in vectors["receiver"]:
        got = simulate_receiver(case)
        if not _close(got, case["expect"]):
            failures.append(f"taskScore.receiver: {case['name']}: {got}")
    return failures


def hand_check() -> list:
    """The hand-written expectations against the reference, before --write stores them."""
    return check_vectors({"capability": CAPABILITY_CASES, "records": RECORD_CASES, "assembly": ASSEMBLY_CASES,
                          "ledger": LEDGER_CASES, "validation": _validation_cases(), "receiver": _receiver_cases()})
