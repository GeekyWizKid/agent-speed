#!/usr/bin/env python3
"""Compares `--dump-today` (and `--dump-tasks`) outputs from different implementations row by row.

  python3 spec/tools/compare_dumps.py reference.jsonl swift.jsonl csharp.jsonl

Responses that end within --grace-ms of the newest dump's capture time are ignored, because the dumps are taken
seconds apart while agents keep writing. The task score rows (METRICS 6.9, 6.11) are compared whenever both dumps have
them: `task` rows per agent (skipped for agents written to within the grace period), the `taskSets` row only when both
dumps were evaluated at the same `nowMs` (`--now`), `record` rows per agent and ordinal (skipped for agents written to
within the grace period, or whose 30 idle minutes that decide `complete` end between the two dumps' `analysisMs`), and `calibration` and `comparison` only when every
record could be compared. Numbers compare within 1e-9 (calibration coefficients within 1e-6).
"""
import argparse
import json
import sys

FIELDS = ("source", "agentId", "model", "startMs", "endMs", "input", "cachedInput", "cacheWriteInput", "output",
          "reasoning", "final")
# Added with the cost feature (METRICS 3.2, 9): compared whenever the other dump has them.
NEW_FIELDS = ("cacheWrite1hInput", "provider", "fast", "costMicroUsd", "requestCount")
# Reasoning effort and speed (EXECUTION-MODES): compared only when both dumps carry them.
MODE_FIELDS = ("effort", "speed")
COMPLETE_IDLE_MS = 30 * 60_000


class Dump:
    def __init__(self, path):
        self.path = path
        self.rows, self.summary, self.tasks, self.sets = {}, None, {}, None
        self.records, self.calibration, self.comparison = {}, {}, None
        with open(path, encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue
                obj = json.loads(line)
                if "summary" in obj:
                    self.summary = obj["summary"]
                elif "task" in obj:
                    self.tasks[obj["task"]["agentId"]] = obj["task"]
                elif "taskSets" in obj:
                    self.sets = obj["taskSets"]
                elif "record" in obj:
                    self.records[(obj["record"]["agentId"], obj["record"]["ordinal"])] = obj["record"]
                elif "calibration" in obj:
                    self.calibration[obj["calibration"]["source"]] = obj["calibration"]["models"]
                elif "comparison" in obj:
                    self.comparison = obj["comparison"]
                else:
                    self.rows[obj["key"]] = obj

    def newest_response(self):
        return max((r["endMs"] for r in self.rows.values()), default=0)

    def newest(self):
        ends = [r["endMs"] for r in self.rows.values()] + [r["endMs"] for r in self.records.values()]
        ends += [t["lastLineMs"] for t in self.tasks.values() if t.get("lastLineMs") is not None]
        return max(ends, default=0)


def close(a, b, tolerance):
    if isinstance(a, bool) or isinstance(b, bool) or a is None or b is None or isinstance(a, str):
        return a == b
    if isinstance(a, (int, float)) and isinstance(b, (int, float)):
        return abs(a - b) <= tolerance
    if isinstance(a, dict) and isinstance(b, dict):
        return a.keys() == b.keys() and all(close(a[k], b[k], tolerance) for k in a)
    if isinstance(a, list) and isinstance(b, list):
        return len(a) == len(b) and all(close(x, y, tolerance) for x, y in zip(a, b))
    return a == b


def differing(a, b, tolerance, label):
    """Paths where two JSON values differ."""
    if isinstance(a, dict) and isinstance(b, dict):
        out = []
        for k in sorted(set(a) | set(b)):
            if k not in a or k not in b:
                out.append("%s.%s present on one side only" % (label, k))
            else:
                out += differing(a[k], b[k], tolerance, "%s.%s" % (label, k))
        return out
    if isinstance(a, list) and isinstance(b, list) and len(a) == len(b):
        out = []
        for i, (x, y) in enumerate(zip(a, b)):
            out += differing(x, y, tolerance, "%s[%d]" % (label, i))
        return out
    return [] if close(a, b, tolerance) else ["%s: %r != %r" % (label, a, b)]


def compare_responses(base, other, cutoff):
    missing = [k for k, r in base.rows.items() if k not in other.rows and r["endMs"] < cutoff]
    extra = [k for k, r in other.rows.items() if k not in base.rows and r["endMs"] < cutoff]
    diffs = []
    for key, ref in base.rows.items():
        row = other.rows.get(key)
        if row is None or ref["endMs"] >= cutoff:
            continue
        for field in (FIELDS + tuple(f for f in NEW_FIELDS if f in row)
                      + tuple(f for f in MODE_FIELDS if f in row and f in ref)):
            if ref.get(field) != row.get(field):
                diffs.append("%s %s: %r != %r" % (key, field, ref.get(field), row.get(field)))
    return missing, extra, diffs


def compare_tasks(base, other, cutoff):
    if not base.tasks and base.sets is None or not other.tasks and other.sets is None:
        return [], "task rows: not in both dumps"
    diffs, notes = [], []
    for aid in sorted(set(base.tasks) | set(other.tasks)):
        a, b = base.tasks.get(aid), other.tasks.get(aid)
        last = max((t.get("lastLineMs") or 0) for t in (a, b) if t is not None)
        if last >= cutoff:
            continue
        if a is None or b is None:
            diffs.append("task %s: in one dump only" % aid)
            continue
        diffs += ["task " + d for d in differing(a, b, 1e-9, aid)]
    now_a, now_b = (base.summary or {}).get("nowMs"), (other.summary or {}).get("nowMs")
    if base.sets is not None and other.sets is not None:
        if now_a is not None and now_a == now_b:
            diffs += ["taskSets " + d for d in differing(base.sets, other.sets, 1e-9, "sets")]
        else:
            notes.append("taskSets not compared (nowMs %r vs %r; pass --now to both)" % (now_a, now_b))
    return diffs, "; ".join(notes)


def compare_records(base, other, cutoff, grace):
    if not base.records or not other.records:
        return [], "records: not in both dumps"
    analysis = [d.summary.get("analysisMs") for d in (base, other) if d.summary and d.summary.get("analysisMs")]
    low = (min(analysis) if analysis else cutoff) - grace - COMPLETE_IDLE_MS
    high = (max(analysis) if analysis else cutoff) - COMPLETE_IDLE_MS
    latest = {}
    for dump in (base, other):
        for (aid, _), r in dump.records.items():
            latest[aid] = max(latest.get(aid, 0), r["endMs"])
    skipped = {aid for aid, end in latest.items() if end >= cutoff or low <= end <= high}
    diffs = []
    for key in sorted(set(base.records) | set(other.records)):
        if key[0] in skipped:
            continue
        a, b = base.records.get(key), other.records.get(key)
        if a is None or b is None:
            diffs.append("record %s #%d: in one dump only" % key)
            continue
        diffs += ["record " + d for d in differing(a, b, 1e-9, "%s#%d" % key)]
    note = ""
    if skipped:
        note = "calibration and comparison not compared (%d active agents skipped)" % len(skipped)
    else:
        for source in sorted(set(base.calibration) | set(other.calibration)):
            a, b = base.calibration.get(source), other.calibration.get(source)
            if a is None or b is None:
                diffs.append("calibration %s: in one dump only" % source)
            else:
                diffs += ["calibration " + d for d in differing(a, b, 1e-6, source)]
        if base.comparison is not None and other.comparison is not None:
            diffs += ["comparison " + d for d in differing(base.comparison, other.comparison, 1e-9, "comparison")]
    return diffs, note


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("dumps", nargs="+")
    parser.add_argument("--grace-ms", type=int, default=120_000)
    args = parser.parse_args()
    loaded = [Dump(p) for p in args.dumps]
    response_cutoff = max(d.newest_response() for d in loaded) - args.grace_ms
    cutoff = max(d.newest() for d in loaded) - args.grace_ms
    base = loaded[0]
    failures = 0
    for d in loaded:
        print("%-60s responses=%d tasks=%d records=%d summary=%s" % (d.path[-60:], len(d.rows), len(d.tasks),
                                                                       len(d.records), json.dumps(d.summary)[:120]))
    for other in loaded[1:]:
        missing, extra, diffs = compare_responses(base, other, response_cutoff)
        task_diffs, task_note = compare_tasks(base, other, cutoff)
        record_diffs, record_note = compare_records(base, other, cutoff, args.grace_ms)
        failures += len(missing) + len(extra) + len(diffs) + len(task_diffs) + len(record_diffs)
        print("\n%s vs %s: missing=%d extra=%d field differences=%d task differences=%d record differences=%d" % (
            base.path, other.path, len(missing), len(extra), len(diffs), len(task_diffs), len(record_diffs)))
        for note in (task_note, record_note):
            if note:
                print("  note   " + note)
        for key in missing[:10]:
            print("  missing", key)
        for key in extra[:10]:
            print("  extra  ", key)
        for diff in (diffs + task_diffs + record_diffs)[:30]:
            print("  diff   " + diff)
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
