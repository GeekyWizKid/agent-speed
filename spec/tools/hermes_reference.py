#!/usr/bin/env python3
"""Independent normalization of Hermes's native per-call agent.log records.

Only the fixed metadata prefix and token counters are retained. Hermes timestamps
have no UTC offset: real logs use local time; shared fixtures explicitly use UTC.
"""
from __future__ import annotations

import json
import math
import re
import sys
from datetime import datetime, timezone
from decimal import Decimal, InvalidOperation
from pathlib import Path
from zoneinfo import ZoneInfo

CAP = 10 ** 12
MAX_LINE = 64 * 1024
MAX_DURATION_MS = 900_000
NAME = re.compile(r"agent\.log(?:\.[0-9]+)?\Z")
LINE = re.compile(
    r"^(?P<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3}) INFO"
    r"(?:[ \t]*\[(?P<session>[^\]\r\n]+)\])?[ \t]+"
    r"(?:run_agent|agent\.conversation_loop|agent\.turn_usage): "
    r"API call #(?P<call>\d+): model=(?P<model>\S+) provider=(?P<provider>\S+) "
    r"in=(?P<input>\d+) out=(?P<output>\d+) total=\d+ "
    r"latency=(?P<duration>\d+(?:\.\d+)?)s(?P<suffix>(?: .*)?)$"
)
CACHE = re.compile(r"(?:^| )cache=(\d+)/(\d+) \(\d+%\)(?: |$)")
WRITE = re.compile(r"(?:^| )write=(\d+)(?: |$)")


def count(raw):
    digits = raw.lstrip("0") or "0"
    return CAP if len(digits) > len(str(CAP)) else min(CAP, int(digits))


def parse_line(line, log_root, time_zone=None):
    """Parse one completed API call; unknown or rounded-zero timing stays None."""
    line = line.rstrip("\r\n")
    if len(line.encode("utf-8")) > MAX_LINE:
        return None
    match = LINE.fullmatch(line)
    if match is None:
        return None
    try:
        date = datetime.strptime(match["time"], "%Y-%m-%d %H:%M:%S,%f")
        if time_zone is not None:
            zone = ZoneInfo(time_zone) if isinstance(time_zone, str) else time_zone
            date = date.replace(tzinfo=zone, fold=1)
            # Reject the nonexistent spring-forward hour instead of normalizing it.
            if date.astimezone(timezone.utc).astimezone(zone).replace(tzinfo=None) != date.replace(tzinfo=None):
                return None
        stamp = date.timestamp()
        if not math.isfinite(stamp):
            return None
        end = round(stamp * 1000)
        duration = Decimal(match["duration"]) * 1000
        if not math.isfinite(float(duration)):
            return None
        start = end - int(duration) if 0 < duration <= MAX_DURATION_MS else None
        inp, out = count(match["input"]), count(match["output"])
        cached, write = CACHE.search(match["suffix"]), WRITE.search(match["suffix"])
        session = match["session"]
        identity = session or str(log_root)
        return dict(
            key="hermes:%s:%d:%s" % (identity, end, match["call"]),
            sessionId=session, startMs=start, endMs=end, input=inp,
            cachedInput=min(inp, count(cached[1])) if cached else 0,
            cacheWriteInput=min(inp, count(write[1])) if write else 0,
            cacheWrite1hInput=0, output=out, reasoning=0,
            model=None if match["model"] == "unknown" else match["model"],
            provider=None if match["provider"] == "unknown" else match["provider"],
            final=True, fast=False, requestCount=1,
        )
    except (ValueError, OverflowError, OSError, InvalidOperation):
        return None


def read_hermes(path, seen=None, time_zone=None):
    """Read one physical log using a shared dedupe set across rotated copies."""
    path = Path(path)
    if path.is_symlink() or not path.is_file():
        return []
    seen = seen if seen is not None else set()
    rows = []
    with path.open("rb") as handle:
        while True:
            raw = handle.readline(MAX_LINE + 2)
            if not raw:
                break
            if len(raw.rstrip(b"\r\n")) > MAX_LINE:
                while raw and not raw.endswith(b"\n"):
                    raw = handle.readline(MAX_LINE + 2)
                continue
            if not raw.endswith(b"\n"):
                break  # A writer's incomplete final line is not a completed usage event.
            try:
                row = parse_line(raw.decode("utf-8"), str(path.parent.resolve()), time_zone)
            except UnicodeError:
                continue
            if row is None or row["key"] in seen:
                continue
            seen.add(row["key"])
            rows.append(row)
    return rows


def discover(root):
    """A Hermes home, a logs folder or one agent.log file; named profiles included."""
    root = Path(root)
    if root.is_symlink():
        return []
    if root.is_file():
        return [root] if NAME.fullmatch(root.name) else []
    candidates = [root, root / "logs"]
    profiles = root / "profiles"
    if profiles.is_dir() and not profiles.is_symlink():
        candidates.extend(p / "logs" for p in profiles.iterdir() if p.is_dir() and not p.is_symlink())
    result = set()
    for folder in candidates:
        if folder.is_dir() and not folder.is_symlink():
            result.update(p for p in folder.iterdir()
                          if NAME.fullmatch(p.name) and p.is_file() and not p.is_symlink())
    return sorted(result, key=lambda p: (str(p.parent), -int(p.name[10:]) if p.name.startswith("agent.log.") else 0))


def collect(root):
    """Return (source, agentId, response) triples for the common reference engine."""
    seen, result = set(), []
    if not root:
        return result
    for path in discover(root):
        for row in read_hermes(path, seen):
            session = row.pop("sessionId")
            agent_id = str(path.parent.resolve()) + "#" + (session or "untagged")
            result.append(("hermes", agent_id, row))
    return result


def check_fixtures(root):
    """Return failures; root is the repository's spec/fixtures directory."""
    root = Path(root)
    expected = json.loads((root / "hermes/expected.json").read_text(encoding="utf-8"))
    failures, seen, actual = [], set(), []
    for item in expected["files"]:
        path = root / item["path"]
        got = read_hermes(path, seen, expected["timeZone"])
        want = [dict(row, key=row["key"].replace("@LOG_ROOT@", str(path.parent.resolve()))) for row in item["responses"]]
        if got != want:
            failures.append("%s: expected %r got %r" % (item["path"], want, got))
        actual.extend(got)
    for case in expected["timeZones"]:
        got = parse_line(case["line"], "fixture-root", case["zone"])
        if (got["endMs"] if got else None) != case["endMs"]:
            failures.append("timezone %s: expected %s got %r" % (case["zone"], case["endMs"], got))
    for case in expected.get("lineCases", []):
        raw = case["line"]
        if case.get("padToBytes"):
            raw += " " * (case["padToBytes"] - len(raw.encode("utf-8")))
        got = parse_line(raw, "fixture-root", "UTC")
        if got != case["response"]:
            failures.append("line case %s: expected %r got %r" % (case["name"], case["response"], got))
    found = [p.relative_to(root).as_posix() for p in discover(root / "hermes")]
    if sorted(found) != sorted(expected["discover"]):
        failures.append("discovery: expected %r got %r" % (expected["discover"], found))
    totals = dict(requests=len(actual), input=sum(r["input"] for r in actual),
                  output=sum(r["output"] for r in actual), cachedInput=sum(r["cachedInput"] for r in actual),
                  cacheWriteInput=sum(r["cacheWriteInput"] for r in actual),
                  measuredDurationMs=sum(r["endMs"] - r["startMs"] for r in actual if r["startMs"] is not None))
    if totals != expected["totals"]:
        failures.append("totals: expected %r got %r" % (expected["totals"], totals))
    return failures


if __name__ == "__main__":
    errors = check_fixtures(sys.argv[1] if len(sys.argv) > 1 else Path(__file__).resolve().parent.parent / "fixtures")
    for error in errors:
        print(error)
    print("Hermes fixtures: %s" % ("FAILED" if errors else "OK"))
    raise SystemExit(bool(errors))
