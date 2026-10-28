#!/usr/bin/env python3
"""Synthetic Hermes logs and hand-specified expectations; never reads a real installation."""
import json
import sys
from pathlib import Path

BASE = 1792497600000  # 2026-10-20T12:00:00Z; the test parser must explicitly use UTC.


def line(clock, session, call, inp, out, latency, suffix="", logger="agent.conversation_loop"):
    tag = " [%s]" % session if session else ""
    return ("2026-10-20 12:00:%s INFO%s %s: API call #%d: model=anthropic/claude-sonnet-4.6 "
            "provider=anthropic in=%s out=%s total=%d latency=%ss%s" %
            (clock, tag, logger, call, inp, out, inp + out, latency, suffix))


def response(session, call, end, duration, inp, out, read=0, write=0):
    return dict(key="hermes:%s:%d:%d" % (session or "@LOG_ROOT@", BASE + end, call), sessionId=session,
                startMs=BASE + end - duration if duration is not None else None, endMs=BASE + end,
                input=inp, cachedInput=read, cacheWriteInput=write, cacheWrite1hInput=0,
                output=out, reasoning=0, model="anthropic/claude-sonnet-4.6", provider="anthropic",
                final=True, fast=False, requestCount=1)


def line_cases():
    overflow = 18446744073709551616
    cap = 10 ** 12
    unknown = response("unknown", 1, 4000, 1000, 100, 10)
    unknown["model"] = unknown["provider"] = None
    missing = response(None, 1, 4000, 1000, 100, 10)
    missing["key"] = missing["key"].replace("@LOG_ROOT@", "fixture-root")
    simple = line("04,000", "edge", 1, 100, 10, "1.0")
    return [
        dict(name="positive counters beyond Int64 saturate", line=line("04,000", "edge", 1, overflow, overflow, "1.0"),
             response=response("edge", 1, 4000, 1000, cap, cap)),
        dict(name="cache counters beyond Int64 clamp to input", line=line("04,000", "edge", 1, 100, 10, "1.0",
             " cache=%d/100 (100%%) write=%d" % (overflow, overflow)), response=response("edge", 1, 4000, 1000, 100, 10, 100, 100)),
        dict(name="unknown is a real session tag", line=line("04,000", "unknown", 1, 100, 10, "1.0")
             .replace("model=anthropic/claude-sonnet-4.6 provider=anthropic", "model=unknown provider=unknown"), response=unknown),
        dict(name="missing session uses log directory", line=line("04,000", None, 1, 100, 10, "1.0"), response=missing),
        dict(name="extra header spaces", line=simple.replace("INFO [edge] ", "INFO  [edge]  "),
             response=response("edge", 1, 4000, 1000, 100, 10)),
        dict(name="unrelated suffix content is not retained", line=simple + " prompt=PRIVATE_SYNTHETIC_PROMPT tool_output=PRIVATE_SYNTHETIC_OUTPUT",
             response=response("edge", 1, 4000, 1000, 100, 10)),
        dict(name="64 KiB metadata line accepted", line=simple, padToBytes=65536,
             response=response("edge", 1, 4000, 1000, 100, 10)),
        dict(name="over 64 KiB ignored", line=simple, padToBytes=65537, response=None),
        dict(name="invalid calendar day ignored", line=simple.replace("2026-10-20", "2026-02-30"), response=None),
    ]


def generate(root):
    root = Path(root)
    first = line("04,000", "sess-a", 1, 1200, 200, "4.0", " cache=500/1200 (42%) write=100 id=fixture-response upstream=Example Host")
    files = [
        ("hermes/logs/agent.log.1", [first, line("04,500", "sess-b", 1, 800, 80, "2.0", " cache=100/800 (12%)")],
         [response("sess-a", 1, 4000, 4000, 1200, 200, 500, 100), response("sess-b", 1, 4500, 2000, 800, 80, 100)]),
        ("hermes/logs/agent.log", [
            first,  # A rotated/copied record is still one request.
            line("05,000", "sess-a", 2, 20, 5, "0.0"),
            line("09,000", "sess-a", 1, 600, 60, "3.0"),  # A resumed process can reset its counter.
            line("11,000", "sess-c", 3, 1000, 90, "1.5", " cache=700/1000 (70%) write=30", "run_agent"),
            line("12,500", None, 1, 400, 40, "1.0", logger="agent.turn_usage"),
            line("13,000", "sess-long", 1, 500, 30, "901.0"),
            "2026-10-20 12:00:14,000 INFO [sess-a] agent.conversation_loop: API call #4: model=test provider=test in=? out=? total=? latency=0.1s usage=unavailable",
            line("15,000", "wrong-logger", 1, 999, 999, "1.0", logger="unrelated.logger"),
            line("15,000", "invalid-date", 1, 999, 999, "1.0").replace("2026-10-20", "2026-02-30"),
            line("15,000", "negative", 1, -1, 999, "1.0"),
            line("15,000", "bad-latency", 1, 999, 999, "NaN"),
            "API call #1: model=example provider=example in=999 out=999 total=1998 latency=1.0s",
            "2026-10-20 12:00:16,000 INFO [sess-a] agent.conversation_loop: Turn ended: reason=text_response model=example api_calls=2/20",
        ], [response("sess-a", 2, 5000, None, 20, 5), response("sess-a", 1, 9000, 3000, 600, 60),
            response("sess-c", 3, 11000, 1500, 1000, 90, 700, 30), response(None, 1, 12500, 1000, 400, 40),
            response("sess-long", 1, 13000, None, 500, 30)]),
        ("hermes/profiles/coder/logs/agent.log", [line("14,500", "sess-profile", 1, 300, 20, "0.5", " write=100")],
         [response("sess-profile", 1, 14500, 500, 300, 20, write=100)]),
    ]
    for path, lines, _ in files:
        target = root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text("\n".join(lines) + "\n", encoding="utf-8")
    for ignored in ("hermes/logs/gateway.log", "hermes/logs/agent.log.backup", "hermes/logs/nested/agent.log"):
        target = root / ignored
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(first + "\n", encoding="utf-8")
    expected = dict(version=1, timeZone="UTC", files=[dict(path=p, responses=r) for p, _, r in files],
                    discover=[p for p, _, _ in files],
                    timeZones=[dict(zone="UTC", line=first, endMs=BASE + 4000),
                               dict(zone="America/Los_Angeles", line=first, endMs=BASE + 7 * 3600 * 1000 + 4000),
                               dict(zone="America/Los_Angeles", line=first.replace("2026-10-20 12:00:04", "2026-03-08 02:30:00"), endMs=None)],
                    lineCases=line_cases(),
                    totals=dict(requests=8, input=4820, output=525, cachedInput=1300,
                                cacheWriteInput=230, measuredDurationMs=12000))
    (root / "hermes/expected.json").write_text(json.dumps(expected, indent=2) + "\n", encoding="utf-8")
    return expected


if __name__ == "__main__":
    generate(sys.argv[1] if len(sys.argv) > 1 else Path(__file__).resolve().parent.parent / "fixtures")
