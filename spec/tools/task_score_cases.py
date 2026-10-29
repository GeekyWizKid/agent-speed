#!/usr/bin/env python3
"""Inputs of spec/fixtures/task-score-cases.json (METRICS 6.9, 6.11), with the expectations written by hand (`want`).

`python3 spec/tools/task_score.py --write` computes every case, checks each `want` against the result and writes the
fixture. Event streams are written by hand: digests are SHA-256 prefixes of readable labels, never of real content.
"""
import hashlib

from task_score_thinking import THINKING

T = 1_000_000_000_000
S = 1000
MIN = 60 * S

COMMENT = (
    "METRICS 6.9 and 6.11, checked by spec/tools/task_score.py and generated from spec/tools/task_score_cases.py "
    "(--write). Events are the normalised stream of 6.9 (prompt / action with kind, tool, digest, command, outcome, "
    "failures, hunks); an outcome whose `t` is after a query's `nowMs` is not known yet. `continuations`: is the text a "
    "continuation. `promptLines`: the prompt kind of one line of a source (`t`: the prompt's own time when it is not "
    "the line's, a queued Claude message). `segmentation`: prompt events from "
    "classified lines. `tasks`: the current task of a stream at `nowMs` (absent: everything): state, score (unrounded), "
    "shown (half away from zero), the five dimensions, IG, loop and oscillation shares, counts, first pass, stall "
    "window ends and the task facts vHeld, openFailure and lastPass. `values`: agent forms and the highest-score rule for groups and the whole set (agent `weight` is "
    "ignored on purpose). `predictions`: P(success), P(stall), P(fix fails) of the current task with the given or prior "
    "coefficients. `records`: the task records of a whole stream. `calibration`: per source and model, coefficients "
    "(tolerance 1e-6), flag and task count, and probe probabilities. `comparison`: the aggregates over a date range "
    "(and one source when `source` is given). "
    "`thinking`: the live thinking line (6.9 Live thinking): liveThinking, thinkingShare and the window weight of the "
    "set, every agent and every board group.")


def h(label):
    return hashlib.sha256(label.encode("utf-8")).hexdigest()[:16]


def at(seconds):
    return T + int(seconds * S)


# ----------------------------------------------------------------------------- event helpers

def prompt(t, continuation=False):
    return {"t": at(t), "type": "prompt", "continuation": continuation}


def read(t, path, tool="Read"):
    return {"t": at(t), "type": "action", "kind": "read", "tool": tool, "digest": h("read " + path),
            "outcome": {"t": at(t) + 200, "exitCode": None, "failed": False}}


def search(t, pattern):
    return {"t": at(t), "type": "action", "kind": "search", "tool": "Grep", "digest": h("grep " + pattern),
            "outcome": {"t": at(t) + 200, "exitCode": None, "failed": False}}


def todo(t, n):
    return {"t": at(t), "type": "action", "kind": "other", "tool": "TodoWrite", "digest": h("todo %d" % n),
            "outcome": {"t": at(t) + 100, "exitCode": None, "failed": False}}


def edit(t, path, old, new, failed=False, tool="Edit"):
    return {"t": at(t), "type": "action", "kind": "edit", "tool": tool, "digest": h("edit %s %s>%s" % (path, old, new)),
            "hunks": [{"file": h(path), "old": h(old), "new": h(new)}],
            "outcome": {"t": at(t) + 300, "exitCode": None, "failed": failed}}


def write(t, path, content):
    return {"t": at(t), "type": "action", "kind": "edit", "tool": "Write", "digest": h("write %s %s" % (path, content)),
            "hunks": [{"file": h(path), "old": None, "new": h(content)}],
            "outcome": {"t": at(t) + 300, "exitCode": None, "failed": False}}


UNSET = object()


def run(t, command, cls="test", suite="cargo test", targeted=False, result="pass", fingerprints=(),
        failed_count=None, passed_count=None, duration=10, failed=UNSET, exit_code=None):
    """A test, build or check command. `result` pass/fail sets the outcome unless `failed` is given."""
    if failed is UNSET:
        failed = result == "fail"
        exit_code = 1 if failed else 0
    if failed_count is None and result == "fail" and fingerprints:
        failed_count = len(fingerprints)
    if passed_count is None and result == "pass" and cls == "test":
        passed_count = 12
    return {"t": at(t), "type": "action", "kind": "command", "tool": "Bash", "digest": h("bash " + command),
            "command": {"class": cls, "suite": suite, "targeted": targeted, "key": h("key " + command)},
            "outcome": {"t": at(t + duration), "exitCode": exit_code, "failed": failed},
            "failures": {"fingerprints": sorted(h("fp " + f) for f in fingerprints), "failedCount": failed_count,
                         "passedCount": passed_count, "framework": "cargo"}}


def fps(*names):
    return list(names)


def dims(V, C, R, E, K):
    return {"V": V, "C": C, "R": R, "E": E, "K": K}


def last_pass(command, suite="cargo test", cls="test", build_only=False):
    return {"suite": suite, "class": cls, "key": h("key " + command), "buildOnly": build_only}


def open_failure(command, errors, peak, suite="cargo test", cls="test", counted=True, edited=False):
    return {"suite": suite, "class": cls, "key": h("key " + command), "errors": errors, "peak": peak,
            "counted": counted, "edited": edited}


# ----------------------------------------------------------------------------- segmentation

def continuations():
    yes = ["continue", "Continue.", "  CONTINUE!  ", "go  on", "Keep going", "proceed", "ok", "OK!", "Ｏｋ", "okay",
           "yes", "lgtm", "繼續", "繼續。", "继续", "続けて", "好", "好的", "是", "go ahead…", "はい"]
    no = ["continue with the tests", "continue?", "ok, now fix the parser", "", "   ", "next", "繼續修 parser",
          "no", "stop", "<command-name>/continue</command-name>", "ok ok"]
    return [{"text": t, "want": True} for t in yes] + [{"text": t, "want": False} for t in no]


def codex_text(text):
    return {"type": "input_text", "text": text}


def codex_user(content):
    return {"type": "response_item", "payload": {"type": "message", "role": "user", "content": content}}


def prompt_lines():
    def claude(content, **extra):
        return dict({"type": "user", "message": {"role": "user", "content": content}}, **extra)

    p = {"kind": "prompt", "continuation": False}
    c = {"kind": "prompt", "continuation": True}
    cases = [
        ("claude: a typed request is a prompt", "claudeCode", claude("Fix the failing parser test"), p),
        ("claude: a continuation word continues", "claudeCode", claude("continue"), c),
        ("claude: text blocks; tag-only blocks are left out of the continuation text", "claudeDesktop",
         claude([{"type": "text", "text": "<ide_opened_file>src/a.rs</ide_opened_file>"},
                 {"type": "text", "text": "go on"}]), c),
        ("claude: an image alone is a prompt", "claudeCode",
         claude([{"type": "image", "source": {"type": "base64", "data": "AAAA"}}]), p),
        ("claude: a tool result is not a prompt", "claudeCode",
         claude([{"type": "tool_result", "tool_use_id": "t1", "content": "ok"}]), None),
        ("claude: a tool result with text is not a prompt", "claudeCode",
         claude([{"type": "tool_result", "tool_use_id": "t1", "content": "ok"},
                 {"type": "text", "text": "continue"}]), None),
        ("claude: injected meta lines are not prompts", "claudeCode", claude("Base directory for this skill", isMeta=True),
         None),
        ("claude: a compaction summary is not a prompt", "claudeCode",
         claude("This session is being continued from a previous conversation", isCompactSummary=True), None),
        ("claude: an interruption marker is not a prompt", "claudeCode",
         claude([{"type": "text", "text": "[Request interrupted by user for tool use]"}]), None),
        ("claude: a slash command is a command", "claudeCode",
         claude("<command-message>review is running…</command-message>\n<command-name>/review</command-name>"),
         {"kind": "command"}),
        ("claude: local command output", "claudeCode",
         claude("<local-command-stdout>Set model to opus</local-command-stdout>"), {"kind": "commandOutput"}),
        ("claude: bash mode input is not a prompt", "claudeCode", claude("<bash-input>ls</bash-input>"), None),
        ("claude: a caveat is not a prompt", "claudeCode",
         claude("<local-command-caveat>Caveat: generated by local commands</local-command-caveat>"), None),
        ("claude: blank text is not a prompt", "claudeCode", claude([{"type": "text", "text": "  \n"}]), None),
        ("claude: assistant lines are never prompts", "claudeCode",
         {"type": "assistant", "message": {"role": "assistant", "content": "continue"}}, None),
        ("codex: a user message item is a prompt", "codex", codex_user([codex_text("Add a --json flag")]), p),
        ("codex: a continuation", "codex", codex_user([codex_text("繼續")]), c),
        ("codex: an image alone is a prompt", "codex",
         codex_user([{"type": "input_image", "image_url": "data:image/png;base64,AAAA"}]), p),
        ("codex: an IDE mention list with the request is a prompt", "codex",
         codex_user([codex_text("# Files mentioned by the user:\n\n## a.rs: src/a.rs\n\n## My request for Codex:\nok"),
                     codex_text("<image name=[Image #1]>"), {"type": "input_image", "image_url": "data:"},
                     codex_text("</image>")]), p),
        ("codex: tag blocks are left out of the continuation text", "codex",
         codex_user([codex_text("go on"), codex_text("<image name=[Image #1]>")]), c),
        ("codex: injected environment context is not a prompt", "codex",
         codex_user([codex_text("<environment_context>\n  <cwd>/w</cwd>\n</environment_context>")]), None),
        ("codex: AGENTS.md instructions are not a prompt", "codex",
         codex_user([codex_text("# AGENTS.md instructions for /w\n\n<INSTRUCTIONS>\nbe brief\n</INSTRUCTIONS>")]), None),
        ("codex: a heartbeat or a reply to the agent's question is not a prompt", "codex",
         codex_user([codex_text("<heartbeat>tick</heartbeat>")]), None),
        ("codex: blank text is not a prompt", "codex", codex_user([codex_text(" \n")]), None),
        ("codex: the user message event is not (it repeats the item in older versions)", "codex",
         {"type": "event_msg", "payload": {"type": "user_message", "message": "Add a --json flag"}}, None),
        ("codex: developer messages are not prompts", "codex",
         {"type": "response_item", "payload": {"type": "message", "role": "developer",
                                               "content": [codex_text("rules")]}}, None),
        ("omp: a user message is a prompt", "omp",
         {"type": "message", "id": "e1", "message": {"role": "user", "content": [{"type": "text", "text": "續"}]}}, p),
        ("pi: string content", "pi",
         {"type": "message", "id": "e1", "message": {"role": "user", "content": "keep going"}}, c),
        ("openclaw: a developer message is not a prompt", "openclaw",
         {"type": "message", "id": "e1", "message": {"role": "developer", "content": "rules"}}, None),
        ("openclaw: a tool result is not a prompt", "openclaw",
         {"type": "message", "id": "e1", "message": {"role": "toolResult", "toolCallId": "c1", "content": "x"}}, None),
        ("droid: a user message without visibility is a prompt", "droid",
         {"type": "message", "id": "m1", "message": {"role": "user", "content": [{"type": "text",
                                                                                 "text": "Fix the cache flush"}]}}, p),
        ("droid: a continuation", "droid",
         {"type": "message", "id": "m2", "message": {"role": "user", "content": "keep going"}}, c),
        ("droid: the per-turn context (llm_only) is not a prompt", "droid",
         {"type": "message", "id": "context-t1", "message": {"role": "user", "visibility": "llm_only",
                                                             "content": [{"type": "text", "text": "cwd: /w"}]}}, None),
        ("droid: a notice (user_only) is not a prompt", "droid",
         {"type": "message", "id": "n1", "message": {"role": "user", "visibility": "user_only",
                                                     "content": [{"type": "text", "text": "Hook ran"}]}}, None),
        ("droid: tool results are not a prompt", "droid",
         {"type": "message", "id": "r1", "message": {"role": "user", "content": [
             {"type": "tool_result", "tool_use_id": "toolu_1", "content": "ok", "is_error": False}]}}, None),
        ("hermes has no tasks", "hermes", {"type": "message", "message": {"role": "user", "content": "hi"}}, None),
    ]

    def queued(mode, prompt_value, line_meta=None, **fields):
        attachment = dict({"type": "queued_command", "commandMode": mode, "prompt": prompt_value,
                           "timestamp": "2026-10-28T09:15:02.250Z", "origin": "user", "source_uuid": "u-1",
                           "delivery_id": "d-1"}, **fields)
        line = {"type": "attachment", "timestamp": "2026-10-28T09:15:40.000Z", "attachment": attachment}
        if line_meta is not None:
            line["isMeta"] = line_meta
        return line

    typed_ms = 1_793_178_902_250
    cases += [
        ("claude: a message typed mid-turn (queued, human) is a prompt at its own time", "claudeCode",
         queued("prompt", "Also cover the empty input case", humanTurn=True),
         {"kind": "prompt", "continuation": False, "t": typed_ms}),
        ("claude: a queued continuation, prompt as blocks", "claudeCode",
         queued("prompt", [{"type": "text", "text": "keep going"}], humanTurn=True),
         {"kind": "prompt", "continuation": True, "t": typed_ms}),
        ("claude: a queued message without a valid timestamp takes the line's time", "claudeDesktop",
         queued("prompt", "and the docs", humanTurn=True, timestamp="2026-10-28 09:15"), p),
        ("claude: a queued background-task notice is not a prompt", "claudeCode",
         queued("task-notification", "<task-notification>build finished</task-notification>"), None),
        ("claude: a queued sub-agent hand-back (meta) is not a prompt", "claudeCode",
         queued("prompt", "<agent-message from=\"reviewer\">done</agent-message>", isMeta=True), None),
        ("claude: a meta line around a queued prompt is not a prompt", "claudeCode",
         queued("prompt", "<agent-message from=\"reviewer\">done</agent-message>", line_meta=True, humanTurn=True), None),
        ("claude: a queued prompt that is not a human turn is not a prompt", "claudeCode",
         queued("prompt", "Run the linter"), None),
        ("claude: other attachments are not prompts", "claudeCode",
         {"type": "attachment", "attachment": {"type": "todo_reminder", "content": []}}, None),
    ]
    return [{"name": n, "source": s, "line": line, "want": want} for n, s, line, want in cases]


def segmentation():
    def ln(t, line, **extra):
        return dict({"t": at(t), "line": line}, **extra)

    return [
        {"name": "prompts take effect at once; a continuation keeps its flag",
         "lines": [ln(0, "prompt"), ln(1, "assistant"), ln(9, "prompt", continuation=True), ln(10, "assistant")],
         "want": [prompt(0), prompt(9, True)]},
        {"name": "a local command is withdrawn by its output", "lines": [
            ln(0, "prompt"), ln(1, "assistant"), ln(5, "command"), ln(5.01, "commandOutput"), ln(9, "prompt", continuation=True)],
         "want": [prompt(0), prompt(9, True)]},
        {"name": "a prompt command is confirmed by the model's reply; later output does nothing", "lines": [
            ln(0, "command"), ln(0.5, "assistant"), ln(3, "commandOutput")], "want": [prompt(0)]},
        {"name": "a copied prompt (its first reply is a duplicate) is withdrawn", "lines": [
            ln(0, "prompt"), ln(1, "assistant", duplicate=True), ln(2, "assistant", duplicate=True),
            ln(100, "prompt"), ln(101, "assistant")], "want": [prompt(100)]},
        {"name": "only the first reply decides", "lines": [
            ln(0, "prompt"), ln(1, "assistant"), ln(2, "assistant", duplicate=True)], "want": [prompt(0)]},
        {"name": "command output never withdraws a prompt", "lines": [ln(0, "prompt"), ln(0.1, "commandOutput")],
         "want": [prompt(0)]},
    ]


# ----------------------------------------------------------------------------- tasks

IDEAL = [prompt(0), read(1, "src/lib.rs"), edit(2, "src/lib.rs", "a1", "a2"), run(3, "cargo test")]
CONVERGE = [prompt(0), edit(1, "src/p.rs", "p1", "p2"),
            run(2, "cargo test", result="fail", fingerprints=fps("a", "b", "c", "d", "e")),
            edit(20, "src/p.rs", "p2", "p3"), run(21, "cargo test", result="fail", fingerprints=fps("a", "b")),
            edit(40, "src/p.rs", "p3", "p4"), run(41, "cargo test")]
PENDING = [prompt(0), edit(1, "src/a.rs", "x1", "x2"), run(2, "cargo test"), edit(20, "src/a.rs", "x2", "x3"),
           run(30, "cargo test")]
READING = [prompt(0), read(1, "README.md"), read(2, "src/main.rs"), search(3, "fn parse")]


def tasks():
    t = []
    t.append({"name": "none: reading only has no score", "events": READING,
              "queries": [{"want": {"state": "none", "score": None, "shown": None, "dims": dims(0, 0, 0, 3.75, 0)}}]})
    t.append({"name": "unverified: edits without a check stay low (E only)",
              "events": [prompt(0), read(1, "a"), read(2, "b"), edit(3, "f1", "A", "B"), edit(4, "f2", "C", "D")],
              "queries": [{"want": {"state": "unverified", "score": 5.0, "shown": 5, "dims": dims(0, 0, 0, 5.0, 0)}}]})
    t.append({"name": "an edit then a full test pass; before its result the task is unverified", "events": IDEAL,
              "queries": [{"nowMs": at(8), "want": {"state": "unverified", "score": 3.75, "shown": 4}},
                          {"want": {"state": "passing", "score": 93.75, "shown": 94, "dims": dims(40, 25, 15, 3.75, 10),
                                    "firstPassMs": at(13), "vHeld": None, "openFailure": None,
                                    "lastPass": last_pass("cargo test")}}]})
    t.append({"name": "convergence 5 → 2 → pass, recovered on the second attempt", "events": CONVERGE,
              "queries": [{"nowMs": at(35), "want": {"state": "failing", "score": 24.0, "shown": 24,
                                                     "dims": dims(0, 15, 0, 5, 4), "lastPass": None,
                                                     "openFailure": open_failure("cargo test", 2, 5)}},
                          {"want": {"state": "passing", "score": 92.5, "shown": 93, "dims": dims(40, 25, 10, 7.5, 10),
                                    "counts": {"failingRuns": 2, "episodes": 1, "recovered": 1, "recoverAttempts": 2,
                                               "fixAttempts": 2, "fixFailures": 1}}}]})
    t.append({"name": "pending: an edit after a pass takes V and K away until the check runs again", "events": PENDING,
              "queries": [{"nowMs": at(15), "want": {"state": "passing", "score": 92.5, "shown": 93}},
                          {"nowMs": at(25), "want": {"state": "pending", "score": 43.75, "shown": 44,
                                                     "dims": dims(0, 25, 15, 3.75, 0), "vHeld": 40.0,
                                                     "lastPass": last_pass("cargo test")}},
                          {"want": {"state": "passing", "score": 95.0, "shown": 95}}]})
    t.append({"name": "regression: a check that passed fails after an edit",
              "events": [prompt(0), edit(1, "f", "a", "b"), run(2, "cargo test"), edit(20, "g", "c", "d"),
                         run(21, "cargo test", result="fail", fingerprints=fps("x", "y"))],
              "queries": [{"nowMs": at(15), "want": {"state": "passing", "score": 92.5}},
                          {"want": {"state": "failing", "score": 9.0, "shown": 9, "dims": dims(0, 0, 0, 5, 4)}}]})
    t.append({"name": "regression penalty: a new failure after a decrease counts twice",
              "events": [prompt(0), edit(1, "f", "a", "b"),
                         run(2, "cargo test", result="fail", fingerprints=fps("a", "b", "c", "d", "e")),
                         edit(20, "f", "b", "c"), run(21, "cargo test", result="fail", fingerprints=fps("a", "x"))],
              "queries": [{"want": {"state": "failing", "score": 19.0, "dims": dims(0, 10, 0, 5, 4)}}]})
    t.append({"name": "loop: the same failing test re-run without an edit",
              "events": [prompt(0), edit(1, "f", "a", "b")] + [
                  run(10 + 20 * i, "cargo test", result="fail", fingerprints=fps("a", "b", "c")) for i in range(4)],
              "queries": [{"want": {"state": "failing", "dims": dims(0, 0, 0, 10 * 0.625 / 3 * 0.4, 4),
                                    "ig": 1 / 3, "loop": 0.6, "shown": 5,
                                    "counts": {"loopActions": 3, "fixAttempts": 4, "fixFailures": 4}}}]})
    t.append({"name": "oscillation: an edit that reverts an earlier one",
              "events": [prompt(0), edit(1, "f", "A", "B"), run(2, "cargo test", result="fail", fingerprints=fps("p")),
                         edit(20, "f", "B", "A"), run(21, "cargo test", result="fail", fingerprints=fps("q")),
                         edit(40, "f", "A", "C"), run(41, "cargo test")],
              "queries": [{"want": {"state": "passing", "osc": 1 / 3, "ig": 8 / 9, "dims": dims(40, 25, 10, 40 / 9, 10),
                                    "shown": 89, "counts": {"oscillations": 1, "edits": 3}}}]})
    t.append({"name": "whole-file writes: an earlier version oscillates, the same content changes nothing",
              "events": [prompt(0), write(1, "f", "X"), write(2, "f", "Y"), write(3, "f", "X"), write(4, "f", "X")],
              "queries": [{"want": {"state": "unverified", "osc": 1 / 3, "ig": 0.5, "score": 10 * 0.5 * 0.5 * 2 / 3,
                                    "shown": 2, "counts": {"edits": 3, "oscillations": 1}}}]})
    t.append({"name": "a failed edit changes nothing: still passing",
              "events": [prompt(0), edit(1, "f", "a", "b"), run(2, "cargo test"), edit(20, "f", "zz", "b", failed=True)],
              "queries": [{"want": {"state": "passing", "score": 92.8125, "shown": 93, "ig": 0.75}}]})
    t.append({"name": "a build-only pass is worth half of V",
              "events": [prompt(0), edit(1, "f", "a", "b"), run(2, "cargo build", cls="build", suite="cargo build")],
              "queries": [{"want": {"state": "passing", "score": 66.5, "shown": 67, "dims": dims(20, 25, 15, 2.5, 4),
                                    "lastPass": last_pass("cargo build", "cargo build", "build", True)}}]})
    t.append({"name": "pending after a build-only pass holds 20",
              "events": [prompt(0), edit(1, "f", "a", "b"), run(2, "cargo build", cls="build", suite="cargo build"),
                         edit(20, "f", "b", "c")],
              "queries": [{"want": {"state": "pending", "score": 43.75, "vHeld": 20.0, "dims": dims(0, 25, 15, 3.75, 0),
                                    "lastPass": last_pass("cargo build", "cargo build", "build", True)}}]})
    t.append({"name": "a failure without counts: one error, not counted",
              "events": [prompt(0), edit(1, "f", "a", "b"), run(2, "make check", cls="check", suite=None, result="fail")],
              "queries": [{"want": {"state": "failing", "score": 6.5, "shown": 7, "dims": dims(0, 0, 0, 2.5, 4),
                                    "openFailure": open_failure("make check", 1, 1, None, "check", counted=False)}}]})
    t.append({"name": "verification without an edit (run the tests and report)",
              "events": [prompt(0), read(1, "Cargo.toml"), run(2, "cargo test")],
              "queries": [{"want": {"state": "passing", "score": 92.5, "dims": dims(40, 25, 15, 2.5, 10)}}]})
    targeted = [prompt(0), run(1, "cargo test", result="fail", fingerprints=fps("a", "b", "c")),
                edit(20, "f", "a", "b"),
                run(21, "cargo test parser::a", targeted=True, result="fail", fingerprints=fps("a")),
                edit(40, "f", "b", "c"), run(41, "cargo test parser::a", targeted=True),
                run(60, "cargo test")]
    t.append({"name": "a targeted fix, then the full suite", "events": targeted,
              "queries": [{"nowMs": at(55), "want": {"state": "passing", "score": 68.0,
                                                     "dims": dims(40, 6.25, 7.5, 6.25, 8),
                                                     "lastPass": last_pass("cargo test parser::a")}},
                          {"want": {"state": "passing", "score": 97.5, "shown": 98, "dims": dims(40, 25, 15, 7.5, 10),
                                    "lastPass": last_pass("cargo test")}}]})
    cont = [prompt(0), edit(1, "f", "a", "b"), run(2, "cargo test"), prompt(100, True), edit(101, "f", "b", "c"),
            run(102, "cargo test"), prompt(200), read(201, "docs.md")]
    t.append({"name": "a continuation keeps the task; a new prompt starts with no score", "events": cont,
              "queries": [{"nowMs": at(100), "want": {"state": "passing", "score": 92.5}},
                          {"nowMs": at(150), "want": {"state": "passing", "score": 95.0}},
                          {"nowMs": at(200), "want": {"state": "none", "score": None,
                                                      "counts": {"actions": 0}}},
                          {"want": {"state": "none", "score": None, "counts": {"actions": 1}}}]})
    t.append({"name": "a passing check does not hide a failing test",
              "events": [prompt(0), edit(1, "f", "a", "b"), run(2, "tsc --noEmit", cls="check", suite="tsc"),
                         run(5, "npm test", suite="npm test", result="fail", fingerprints=fps("t1", "t2"))],
              "queries": [{"want": {"state": "failing", "score": 11.75, "dims": dims(0, 0, 0, 3.75, 8)}}]})
    t.append({"name": "failed, then edited: still failing until the check runs again",
              "events": [prompt(0), edit(1, "f", "a", "b"),
                         run(2, "cargo test", result="fail", fingerprints=fps("x", "y")), edit(20, "f", "b", "c")],
              "queries": [{"want": {"state": "failing", "score": 3.75, "shown": 4, "dims": dims(0, 0, 0, 3.75, 0),
                                    "openFailure": open_failure("cargo test", 2, 2, edited=True)}}]})
    t.append({"name": "an unknown outcome is not a run; counts alone can make a pass",
              "events": [prompt(0), edit(1, "f", "a", "b"),
                         run(2, "cargo test", result=None, failed=None, exit_code=None, passed_count=None),
                         run(30, "cargo test", result=None, failed=None, exit_code=None, failed_count=0, passed_count=7)],
              "queries": [{"nowMs": at(25), "want": {"state": "unverified", "score": 2.5, "shown": 3}},
                          {"want": {"state": "passing", "score": 93.75, "shown": 94}}]})
    t.append({"name": "exit 0 with reported failures is a failure (piped output)",
              "events": [prompt(0), run(1, "cargo test | tail", result="fail", failed=False, exit_code=0, failed_count=3),
                         edit(20, "f", "a", "b"), run(21, "cargo test | tail")],
              "queries": [{"nowMs": at(15), "want": {"state": "failing", "score": 5.25, "shown": 5,
                                                     "openFailure": open_failure("cargo test | tail", 3, 3)}},
                          {"want": {"state": "passing", "score": 93.75, "dims": dims(40, 25, 15, 3.75, 10)}}]})
    t.append({"name": "a piped build that reports no error passes; a test summary that ran nothing is no run",
              "events": [prompt(0), edit(1, "f", "a", "b"),
                         run(2, "cargo build | tail", cls="build", suite="cargo build", failed=None, failed_count=0),
                         run(20, "cargo test none | tail", targeted=True, failed=None, failed_count=0, passed_count=0)],
              "queries": [{"want": {"state": "passing", "score": 67.75, "dims": dims(20, 25, 15, 3.75, 4),
                                    "counts": {"runs": 1}, "lastPass": {"buildOnly": True}}}]})
    stall = [prompt(0)] + [read(1 + i, "a" if i % 2 == 0 else "b") for i in range(9)]
    t.append({"name": "stall: eight actions in a row that find almost nothing new", "events": stall,
              "queries": [{"want": {"state": "none", "score": None, "ig": 2 / 9, "loop": 7 / 9, "stallEnds": [8, 9],
                                    "counts": {"loopActions": 7}}}]})
    other = [prompt(0), edit(1, "f", "a", "b"), run(2, "cargo test", result="fail", fingerprints=fps("a")),
             todo(15, 1), run(16, "cargo test", result="fail", fingerprints=fps("a")), todo(30, 2),
             run(31, "cargo test", result="fail", fingerprints=fps("a"))]
    t.append({"name": "an `other` tool neither counts nor breaks a loop", "events": other,
              "queries": [{"want": {"state": "failing", "loop": 0.5, "ig": 3 / 7, "shown": 5,
                                    "dims": dims(0, 0, 0, 10 * 0.5 * 3 / 7 * 0.5, 4),
                                    "counts": {"actions": 6, "counted": 4}}}]})
    return t


# ----------------------------------------------------------------------------- values

def agent(id, phase, events, last, parent=None, weight=None, source="codex"):
    a = {"id": id, "parentId": parent, "source": source, "phase": phase, "lastLineMs": at(last), "events": events}
    if weight is not None:
        a["weight"] = weight
    return a


UNVERIFIED = [prompt(0), read(1, "a"), read(2, "b"), edit(3, "f1", "A", "B"), edit(4, "f2", "C", "D")]
FAILING_24 = [e for e in CONVERGE if e["t"] <= at(21)]


def values():
    now = at(500)
    return [
        {"name": "agent forms: live while working, resting while idle, none without a score", "nowMs": now,
         "agents": [agent("a1", "generating", IDEAL, 13), agent("a2", "idle", PENDING[:4], 21),
                    agent("a3", "tools", READING, 3), agent("a4", "idle", [], 0)],
         "want": {"agents": {"a1": {"form": "live", "shown": 94}, "a2": {"form": "resting", "shown": 44,
                                                                          "state": "pending"},
                             "a3": {"form": "none", "state": "none"}, "a4": {"form": "none", "state": None}},
                  "all": {"form": "live", "shown": 94, "agent": "a1"}}},
        {"name": "the highest live score wins even when a lower one writes far more tokens", "nowMs": now,
         "agents": [agent("b1", "generating", UNVERIFIED, 4, weight=5000.0),
                    agent("b2", "generating", IDEAL, 13, weight=10.0)],
         "want": {"all": {"form": "live", "score": 93.75, "shown": 94, "agent": "b2"}}},
        {"name": "no live score: the highest resting score", "nowMs": now,
         "agents": [agent("c1", "idle", CONVERGE, 51), agent("c2", "idle", PENDING[:4], 21),
                    agent("c3", "generating", READING, 3)],
         "want": {"all": {"form": "resting", "score": 92.5, "agent": "c1"}}},
        {"name": "groups: live max over members, else resting max, else none; ties by latest activity, then id",
         "nowMs": now,
         "agents": [agent("r1", "idle", CONVERGE, 51), agent("s1", "generating", UNVERIFIED, 4, parent="r1"),
                    agent("s2", "tools", FAILING_24, 31, parent="r1"),
                    agent("t1", "idle", PENDING[:4], 21),
                    agent("u1", "generating", READING, 3), agent("u2", "idle", [], 0, parent="u1"),
                    agent("w1", "idle", CONVERGE, 100), agent("w2", "idle", CONVERGE, 200, parent="w1"),
                    agent("x2", "idle", CONVERGE, 300), agent("x1", "idle", CONVERGE, 300, parent="x2")],
         "want": {"groups": {"r1": {"members": ["r1", "s1", "s2"], "form": "live", "score": 24.0, "agent": "s2"},
                             "t1": {"members": ["t1"], "form": "resting", "shown": 44, "agent": "t1"},
                             "u1": {"members": ["u1", "u2"], "form": "none", "score": None, "agent": None},
                             "w1": {"members": ["w1", "w2"], "form": "resting", "score": 92.5, "agent": "w2"},
                             "x2": {"members": ["x2", "x1"], "form": "resting", "score": 92.5, "agent": "x1"}},
                  "all": {"form": "live", "score": 24.0, "agent": "s2"}}},
    ]


# ----------------------------------------------------------------------------- predictions

def predictions():
    custom = {"success": {"coefficients": [0.0, 1.0, 0, 0, 0, 0, 0, 0], "calibrated": True, "tasks": 120},
              "stall": {"coefficients": [0.0, 0, 0, 0, 0, 0], "calibrated": True, "tasks": 120},
              "fix": {"coefficients": [1.0, 0, 0, 0, 0, 0], "calibrated": True, "tasks": 64}}
    return [
        {"name": "a passing task with the prior coefficients", "events": IDEAL,
         "want": {"successShown": 97, "stallShown": 5, "fix": None,
                  "features": {"passing": 1.0, "failing": 0.0, "conv": 1.0, "ig": 1.0, "loop": 0.0, "osc": 0.0,
                               "size": 2 / 9}}},
        {"name": "a failing task: P(fix fails) from its latest failing run", "events": CONVERGE, "nowMs": at(35),
         "want": {"fixShown": 37, "fixFeatures": {"attempts": 0.25, "trend": 0.4, "osc": 0.0, "ig": 1.0}}},
        {"name": "failed, then edited: the fix features stay those of the failing run",
         "events": CONVERGE[:5] + [edit(30, "src/p.rs", "p3", "p9")], "nowMs": at(35),
         "want": {"fixShown": 37, "fixFeatures": {"attempts": 0.25, "trend": 0.4}}},
        {"name": "no score, no predictions", "events": READING, "want": None},
        {"name": "calibrated coefficients are used as given", "events": CONVERGE, "nowMs": at(35), "models": custom,
         "want": {"success": 0.5, "stall": 0.5, "fixShown": 73, "successShown": 50}},
    ]


# ----------------------------------------------------------------------------- records

def records():
    stream = ([read(1, "spec.md"), edit(2, "f", "a", "b"), run(3, "pytest", suite="pytest")] +
              [prompt(100), edit(101, "g", "c", "d"),
               run(102, "pytest", suite="pytest", result="fail", fingerprints=fps("t1", "t2")),
               prompt(130, True), edit(131, "g", "d", "e"), run(132, "pytest", suite="pytest")] +
              [prompt(300)] +
              [prompt(400)] + [read(401 + i, "a" if i % 2 == 0 else "b") for i in range(9)])
    models = [[at(0), "gpt-5.5", "high"], [at(120), "gpt-5.5", "xhigh"]]
    return [
        {"name": "a whole stream: an implicit first task, a continuation, an empty prompt, a stall; the last task is "
                 "still open", "source": "codex", "events": stream, "models": models,
         "analysisMs": at(420), "lastLineMs": at(409.2),
         "want": [
             {"ordinal": 0, "startMs": at(1), "endMs": at(13), "complete": True, "model": "gpt-5.5", "effort": "high",
              "state": "passing", "score": 93.75, "success": True, "stalled": False, "firstPassMs": at(13),
              "fixRows": []},
             {"ordinal": 1, "startMs": at(100), "endMs": at(142), "complete": True, "model": "gpt-5.5",
              "effort": "xhigh", "state": "passing", "success": True, "stalled": False, "firstPassMs": at(142),
              "counts": {"fixAttempts": 1, "fixFailures": 0, "recovered": 1, "recoverAttempts": 1}},
             {"ordinal": 2, "startMs": at(400), "complete": False, "state": "none", "score": None, "success": False,
              "stalled": True, "snapshots": [], "fixRows": []}]},
        {"name": "the last task is complete once the agent has been quiet for 30 minutes", "source": "claudeCode",
         "events": IDEAL, "analysisMs": at(13) + 30 * MIN, "lastLineMs": at(13),
         "want": [{"ordinal": 0, "complete": True, "model": None, "effort": None, "score": 93.75,
                   "snapshots": [[2, 0, 0, 0, 1.0, 0, 0, 1.5849625007211563 / 9, 0]]}]},
    ]


# ----------------------------------------------------------------------------- calibration

class Lcg:
    def __init__(self, seed):
        self.x = seed

    def next(self):
        self.x = (1103515245 * self.x + 12345) % 2 ** 31
        return self.x / 2 ** 31

    def r3(self):
        return round(self.next(), 3)


def logistic(eta):
    import math
    return 1 / (1 + math.exp(-eta))


def synthetic(source, count, seed, success_bias=0.0):
    """Records with stored rows only (what calibration reads), labels drawn from a known logistic."""
    rng, recs = Lcg(seed), []
    for i in range(count):
        snaps, fixes = [], []
        ig, loop, osc = rng.r3(), round(rng.next() * 0.5, 3), round(rng.next() * 0.4, 3)
        passing = 1 if rng.next() < 0.5 else 0
        failing = 0 if passing else (1 if rng.next() < 0.6 else 0)
        conv = rng.r3() if not passing else 1.0
        success = rng.next() < logistic(-1 + 2.5 * passing + 1.2 * ig - 2 * loop + success_bias)
        for k in range(1 + int(rng.next() * 3)):
            size = round(min(1.0, (k + 2) / 9), 3)
            stall = 1 if rng.next() < logistic(-0.5 - 2 * ig + 3 * loop + 0.5 * failing) else 0
            snaps.append([2 ** k, passing, failing, conv, ig, loop, osc, size, stall])
        for k in range(int(rng.next() * 3)):
            attempts, errors, trend = round(min(1.0, k / 4), 3), rng.r3(), max(0.05, rng.r3())
            yfix = 1 if rng.next() < logistic(-0.3 + 1.5 * attempts + 1.2 * trend - ig) else 0
            fixes.append([attempts, errors, trend, osc, ig, yfix])
        recs.append({"agentId": "/logs/%s/agent-%02d.jsonl" % (source, i % 7), "ordinal": i // 7,
                     "startMs": at(i * 600), "endMs": at(i * 600 + 300), "complete": i % 13 != 12,
                     "success": success, "snapshots": snaps, "fixRows": fixes})
    return recs


def calibration():
    probes = [{"model": "success", "features": {"passing": 1, "failing": 0, "conv": 1, "ig": 0.9, "loop": 0.1,
                                               "osc": 0, "size": 0.4}},
              {"model": "stall", "features": {"ig": 0.3, "loop": 0.4, "osc": 0.1, "failing": 1, "size": 0.6}},
              {"model": "fix", "features": {"attempts": 0.5, "errors": 0.3, "trend": 0.8, "osc": 0.2, "ig": 0.5}}]
    one_class = synthetic("pi", 45, 7, success_bias=20.0)
    return [
        {"name": "a synthetic history: codex has enough tasks, claudeCode keeps the prior",
         "records": {"codex": synthetic("codex", 64, 20261028), "claudeCode": synthetic("claudeCode", 30, 99)},
         "probes": probes,
         "want": {"codex": {"models": {"success": {"calibrated": True, "tasks": 60},
                                       "stall": {"calibrated": True, "tasks": 60}}},
                  "claudeCode": {"models": {"success": {"calibrated": False, "tasks": 28,
                                                        "coefficients": [-1.5, 3.0, -0.5, 1.0, 1.0, -2.0, -1.5, -0.5]},
                                            "fix": {"calibrated": False}}}}},
        {"name": "one outcome only: the success model stays on the prior", "records": {"pi": one_class},
         "probes": probes[:1], "want": {"pi": {"models": {"success": {"calibrated": False}}}}},
    ]


# ----------------------------------------------------------------------------- comparison

def rec(source, model, day, score, success=False, stalled=False, complete=True, first_pass=None, fix=(0, 0),
        recovered=(0, 0)):
    return {"source": source, "model": model, "day": day, "complete": complete, "score": score, "success": success,
            "stalled": stalled, "startMs": at(0), "firstPassMs": None if first_pass is None else at(first_pass),
            "counts": {"fixAttempts": fix[0], "fixFailures": fix[1], "recovered": recovered[0],
                       "recoverAttempts": recovered[1]}}


def comparison():
    records = [
        rec("codex", "gpt-5.5", "2026-10-26", 93.75, success=True, first_pass=40, fix=(2, 1), recovered=(1, 2)),
        rec("codex", "gpt-5.5", "2026-10-27", 24.0, fix=(3, 3)),
        rec("codex", "gpt-5.5", "2026-10-27", None, stalled=True),
        rec("codex", "gpt-5.5-mini", "2026-10-28", 66.5, success=True, first_pass=100),
        rec("claudeCode", "claude-opus-5-5", "2026-10-26", 97.5, success=True, first_pass=60, fix=(1, 0),
            recovered=(1, 1)),
        rec("claudeCode", "claude-opus-5-5", "2026-10-28", 43.75, first_pass=10),
        rec("claudeCode", None, "2026-10-28", 5.0, stalled=True),
        rec("claudeCode", "claude-opus-5-5", "2026-10-28", 99.0, success=True, complete=False),
        rec("codex", "gpt-5.5", "2026-10-20", 10.0),
    ]
    return [
        {"name": "per source and per model over a range; open tasks and days outside are left out",
         "records": records, "first": "2026-10-26", "last": "2026-10-28",
         "want": {"bySource": [
             {"source": "codex", "tasks": 4, "scoredTasks": 3, "medianScore": 66.5, "successRate": 2 / 3,
              "stallRate": 0.25, "fixFailureRate": 0.8, "recoverAttempts": 2.0, "timeToFirstPassMs": 70 * S,
              "lowerQuartile": 45.25, "upperQuartile": 80.125, "histogram": [0, 0, 1, 0, 0, 0, 1, 0, 0, 1]},
             {"source": "claudeCode", "tasks": 3, "scoredTasks": 3, "medianScore": 43.75, "medianShown": 44,
              "successRate": 1 / 3, "stallRate": 1 / 3, "fixFailureRate": 0.0, "recoverAttempts": 1.0,
              "timeToFirstPassMs": 35 * S, "lowerQuartile": 24.375, "upperQuartile": 70.625,
              "histogram": [1, 0, 0, 0, 1, 0, 0, 0, 0, 1]}],
             "byModel": [
                 {"model": "gpt-5.5", "sources": ["codex"], "tasks": 3, "scoredTasks": 2, "medianScore": 58.875},
                 {"model": "claude-opus-5-5", "sources": ["claudeCode"], "tasks": 2, "medianScore": 70.625},
                 {"model": "", "sources": ["claudeCode"], "tasks": 1, "stallRate": 1.0, "fixFailureRate": None,
                  "timeToFirstPassMs": None},
                 {"model": "gpt-5.5-mini", "sources": ["codex"], "tasks": 1, "successRate": 1.0}]}},
        {"name": "one source chosen in the window's picker", "records": records, "first": "2026-10-26",
         "last": "2026-10-28", "source": "claudeCode",
         "want": {"bySource": [{"source": "claudeCode", "tasks": 3, "medianScore": 43.75}],
                  "byModel": [{"model": "claude-opus-5-5", "sources": ["claudeCode"], "tasks": 2},
                              {"model": "", "sources": ["claudeCode"], "tasks": 1}]}},
        {"name": "an empty range", "records": records, "first": "2026-11-01", "last": "2026-11-30",
         "want": {"bySource": [], "byModel": []}},
        {"name": "quartiles and histogram bins at their edges; a model across sources",
         "records": [rec("codex", "m1", "2026-10-28", v) for v in (100.0, 9.999, 90.0, 0.0, 89.99, 10.0)] +
                    [rec("openclaw", "m1", "2026-10-28", 50.0), rec("openclaw", "m1", "2026-10-28", None, stalled=True)],
         "first": "2026-10-28", "last": "2026-10-28",
         "want": {"bySource": [
             {"source": "codex", "scoredTasks": 6, "lowerQuartile": 9.999 + 0.25 * (10.0 - 9.999),
              "medianScore": 10.0 + 0.5 * (89.99 - 10.0), "upperQuartile": 89.99 + 0.75 * (90.0 - 89.99),
              "histogram": [2, 1, 0, 0, 0, 0, 0, 0, 1, 2]},
             {"source": "openclaw", "tasks": 2, "scoredTasks": 1, "lowerQuartile": 50.0, "medianScore": 50.0,
              "upperQuartile": 50.0, "histogram": [0, 0, 0, 0, 0, 1, 0, 0, 0, 0], "stallRate": 0.5}],
             "byModel": [{"model": "m1", "sources": ["codex", "openclaw"], "tasks": 8, "scoredTasks": 7,
                          "lowerQuartile": 9.999 + 0.5 * (10.0 - 9.999), "medianScore": 50.0,
                          "upperQuartile": 89.99 + 0.5 * (90.0 - 89.99),
                          "histogram": [2, 1, 0, 0, 0, 1, 0, 0, 1, 2]}]}},
        {"name": "no scored task: no quartiles, an empty histogram", "first": "2026-10-27", "last": "2026-10-27",
         "records": [rec("pi", "m2", "2026-10-27", None)],
         "want": {"bySource": [{"source": "pi", "tasks": 1, "scoredTasks": 0, "medianScore": None, "medianShown": None,
                                "lowerQuartile": None, "upperQuartile": None, "histogram": [0] * 10,
                                "successRate": None}]}},
    ]


def cases():
    return {"continuations": continuations(), "promptLines": prompt_lines(), "segmentation": segmentation(),
            "tasks": tasks(), "values": values(), "predictions": predictions(), "records": records(),
            "calibration": calibration(), "comparison": comparison(), "thinking": THINKING}
