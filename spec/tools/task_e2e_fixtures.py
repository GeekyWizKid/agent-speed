#!/usr/bin/env python3
"""End-to-end task score fixtures (METRICS 6.9 *Composing the stream*), used by generate_fixtures.py.

  python3 spec/tools/task_e2e_fixtures.py OUT_DIR

OUT_DIR/tasks holds hand-written sessions of every scored source, laid out as each tool writes them: Claude Code and
Claude Desktop (a sub-agent, a forked copy, a queued mid-turn prompt, a background test finished by a task notice, a
withdrawn command), Codex (code-mode scripts with a slow test finished through `write_stdin`, a function-call session
of the older format, a sub-agent rollout with copied history, a history of 60 short tasks that calibrates the success
and stall models), omp (copied entries, a nested sub-agent), Pi (a continuation) and OpenClaw (an embedded window, a
spawned window and a delegated window whose mirror adds nothing). Every text is invented; nothing comes from real
logs. `META` names the reading order, the instants and the sets; `WANT` holds the expectations written by hand from
METRICS 6.9 and 6.11, which `reference.py --write-task-fixture` checks before it writes
spec/fixtures/task-e2e-expected.json.
"""
import json
import os
import sqlite3
import sys
from datetime import datetime, timezone

from omp_fixtures import Session, iso, uuid7

DAY = datetime(2026, 10, 27, tzinfo=timezone.utc)
ROOT = "tasks"
WORK = "/Users/dev/work"


def at(clock):
    """'HH:MM:SS[.mmm]' on 2026-10-27 (UTC) -> Unix ms."""
    hms, _, frac = clock.partition(".")
    h, m, s = (int(x) for x in hms.split(":"))
    return int(DAY.timestamp() * 1000) + ((h * 60 + m) * 60 + s) * 1000 + int((frac or "0").ljust(3, "0"))


NOW = at("11:00:00")


def write_jsonl(path, rows):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        for row in rows:
            f.write(row if isinstance(row, str) else json.dumps(row, ensure_ascii=False, separators=(",", ":")))
            f.write("\n")


# ----------------------------------------------------------------------------- Claude Code / Claude Desktop

class Claude:
    def __init__(self, session, cwd, entrypoint="cli", sidechain=False):
        self.session, self.cwd, self.entrypoint, self.sidechain = session, cwd, entrypoint, sidechain
        self.rows, self.count, self.parent = [], 0, None

    def line(self, kind, clock, **fields):
        self.count += 1
        uid = "%s-%04d" % (self.session[:8], self.count)
        row = {"parentUuid": self.parent, "isSidechain": self.sidechain, "userType": "external", "cwd": self.cwd,
               "sessionId": self.session, "version": "2.1.31", "entrypoint": self.entrypoint, "type": kind,
               "uuid": uid, "timestamp": iso(at(clock))}
        row.update(fields)
        self.parent = uid
        self.rows.append(row)
        return row

    def user(self, clock, content, **fields):
        return self.line("user", clock, message={"role": "user", "content": content}, **fields)

    def result(self, clock, call_id, text, is_error=False):
        return self.user(clock, [{"type": "tool_result", "tool_use_id": call_id, "content": text, "is_error": is_error}])

    def assistant(self, clock, mid, content, stop):
        usage = {"input_tokens": 12, "cache_creation_input_tokens": 0, "cache_read_input_tokens": 4000,
                 "output_tokens": 80}
        return self.line("assistant", clock, requestId="req_" + mid, message={
            "id": "msg_" + mid, "type": "message", "role": "assistant", "model": "claude-opus-5-5", "content": content,
            "stop_reason": stop, "stop_sequence": None, "usage": usage})

    def tool(self, clock, mid, call_id, name, value):
        return self.assistant(clock, mid, [{"type": "tool_use", "id": call_id, "name": name, "input": value}],
                              "tool_use")

    def say(self, clock, mid, words):
        return self.assistant(clock, mid, [{"type": "text", "text": words}], "end_turn")

    def queued(self, clock, sent, prompt, mode="prompt"):
        return self.line("attachment", clock, attachment={"type": "queued_command", "prompt": prompt,
                                                          "commandMode": mode, "humanTurn": mode == "prompt",
                                                          "timestamp": iso(at(sent))})


CARGO_FAIL = ("test queue::tests::push_grows ... FAILED\n\nfailures:\n\n---- queue::tests::push_grows stdout ----\n"
              "thread 'queue::tests::push_grows' panicked at src/queue.rs:41:9:\nassertion `left == right` failed\n"
              "  left: 1\n right: 2\n\nfailures:\n    queue::tests::push_grows\n\ntest result: FAILED. 11 passed; 1 "
              "failed; 0 ignored; 0 measured; 0 filtered out; finished in 0.01s")
CARGO_PASS = ("running 12 tests\n............\ntest result: ok. 12 passed; 0 failed; 0 ignored; 0 measured; 0 filtered "
              "out; finished in 0.01s\n")


def claude_sessions(out):
    atlas = WORK + "/atlas"
    folder = os.path.join(out, ROOT, "claude", "projects", "-Users-dev-work-atlas")
    main_id, fork_id, sub = "5b1c7e2a-0d4f-4b8e-9a31-6c2f0e8d1a01", "9e2d4c61-7b3a-4f05-8e12-3d9c5b7a2f02", "a8f3c2e1"
    m = Claude(main_id, atlas)
    m.user("10:20:00", "Fix the failing queue test")
    m.tool("10:20:02", "a01", "toolu_a01", "Agent", {"description": "Find queue tests", "subagent_type": "Explore",
                                                    "prompt": "Find the tests for the queue module"})
    m.result("10:20:04", "toolu_a01", "The queue tests live in src/queue.rs (mod tests).")
    m.tool("10:20:05", "a02", "toolu_a02", "Read", {"file_path": atlas + "/src/queue.rs"})
    m.result("10:20:06", "toolu_a02", "pub struct Queue { items: Vec<u32> }")
    m.tool("10:20:10", "a03", "toolu_a03", "Bash", {"command": "cargo test 2>&1 | tail -15",
                                                   "description": "Run the tests"})
    m.result("10:20:30", "toolu_a03", CARGO_FAIL)
    m.tool("10:21:00", "a04", "toolu_a04", "Edit", {"file_path": atlas + "/src/queue.rs",
                                                   "old_string": "self.items.push(x)",
                                                   "new_string": "self.items.insert(0, x)"})
    m.result("10:21:01", "toolu_a04", "The file %s/src/queue.rs has been updated." % atlas)
    m.tool("10:21:10", "a05", "toolu_a05", "Bash", {"command": "cargo test", "description": "Run the tests"})
    m.result("10:21:25", "toolu_a05", CARGO_PASS)
    m.say("10:21:30", "a06", "Fixed: push now inserts at the front.")
    m.user("10:30:00", "continue")
    m.tool("10:30:05", "a07", "toolu_a07", "Edit", {"file_path": atlas + "/src/queue.rs",
                                                   "old_string": "fn len(&self) -> usize { 0 }",
                                                   "new_string": "fn len(&self) -> usize { self.items.len() }"})
    m.result("10:30:06", "toolu_a07", "The file %s/src/queue.rs has been updated." % atlas)
    m.tool("10:30:10", "a08", "toolu_a08", "Bash", {"command": "cargo test", "description": "Run the test suite",
                                                   "run_in_background": True})
    m.result("10:30:11", "toolu_a08", "Command running in background with ID: bq7k2. Output is being written to: "
                                      "/tmp/claude-501/atlas/tasks/bq7k2.output")
    m.queued("10:31:00", "10:30:40", "also add a len() benchmark")
    m.tool("10:31:05", "a09", "toolu_a09", "Write", {"file_path": atlas + "/benches/len.rs",
                                                    "content": "fn bench_len() {}\n"})
    m.result("10:31:06", "toolu_a09", "File created successfully at: %s/benches/len.rs" % atlas)
    m.tool("10:31:10", "a10", "toolu_a10", "Bash", {"command": "cargo build --benches 2>&1 | tail -3",
                                                   "description": "Build the benchmarks"})
    m.result("10:31:40", "toolu_a10", "   Compiling atlas v0.1.0 (%s)\n    Finished `bench` profile [optimized] "
                                      "target(s) in 28.01s" % atlas)
    m.queued("10:32:00", "10:31:58", "<task-notification>\n<task-id>bq7k2</task-id>\n<tool-use-id>toolu_a08"
                                     "</tool-use-id>\n<output-file>/tmp/claude-501/atlas/tasks/bq7k2.output"
                                     "</output-file>\n<status>completed</status>\n<summary>Background command \"Run the "
                                     "test suite\" completed (exit code 0)</summary>\n</task-notification>",
             mode="task-notification")
    m.tool("10:57:00", "a11", "toolu_a11", "Grep", {"pattern": "bench", "path": atlas + "/benches"})
    write_jsonl(os.path.join(folder, main_id + ".jsonl"), m.rows)

    s = Claude(main_id, atlas, sidechain=True)
    s.user("10:20:02.500", "Find the tests for the queue module")
    s.tool("10:20:03", "s01", "toolu_s01", "Grep", {"pattern": "mod tests", "path": atlas + "/src"})
    s.result("10:20:03.500", "toolu_s01", "src/queue.rs:30:mod tests {")
    s.say("10:20:04", "s02", "The queue tests live in src/queue.rs (mod tests).")
    write_jsonl(os.path.join(folder, main_id, "subagents", "agent-%s.jsonl" % sub), s.rows)

    f = Claude(fork_id, atlas)
    f.rows = [dict(r, sessionId=fork_id) for r in m.rows[:5]]
    f.parent = f.rows[-1]["uuid"]
    f.user("10:45:00", "Write the CHANGELOG entry")
    f.tool("10:45:05", "f01", "toolu_f01", "Edit", {"file_path": atlas + "/CHANGELOG.md", "old_string": "## Unreleased",
                                                   "new_string": "## Unreleased\n\n- Queue push order fixed"})
    f.result("10:45:06", "toolu_f01", "The file %s/CHANGELOG.md has been updated." % atlas)
    f.say("10:45:20", "f02", "Added the entry.")
    write_jsonl(os.path.join(folder, fork_id + ".jsonl"), f.rows)

    desk_id = "c4e8a2f0-3b1d-4e6a-8f27-5a9d1c3e7b03"
    d = Claude(desk_id, WORK + "/orbit", entrypoint="claude-desktop")
    d.user("10:54:00", "<command-name>/model</command-name>\n<command-message>model</command-message>\n"
                       "<command-args></command-args>")
    d.user("10:54:01", "<local-command-stdout>Set model to claude-opus-5-5</local-command-stdout>")
    d.user("10:54:10", "What does the scheduler do?")
    d.tool("10:54:15", "d01", "toolu_d01", "Read", {"file_path": WORK + "/orbit/src/scheduler.rs"})
    d.result("10:54:16", "toolu_d01", "pub fn tick() {}")
    d.say("10:54:30", "d02", "It advances the timers once per tick.")
    desk_folder = os.path.join(out, ROOT, "claude", "projects", "-Users-dev-work-orbit")
    write_jsonl(os.path.join(desk_folder, desk_id + ".jsonl"), d.rows)
    rel = lambda *p: "/".join((ROOT, "claude", "projects") + p)  # noqa: E731
    return {"main": rel("-Users-dev-work-atlas", main_id + ".jsonl"),
            "claudeSub": rel("-Users-dev-work-atlas", main_id, "subagents", "agent-%s.jsonl" % sub),
            "fork": rel("-Users-dev-work-atlas", fork_id + ".jsonl"),
            "desk": rel("-Users-dev-work-orbit", desk_id + ".jsonl")}


# ----------------------------------------------------------------------------- Codex

class Codex:
    def __init__(self):
        self.rows = []

    def line(self, clock, kind, payload):
        self.rows.append({"timestamp": iso(at(clock)), "type": kind, "payload": payload})

    def meta(self, clock, thread, cwd, parent=None):
        source = "cli" if parent is None else {"subagent": {"thread_spawn": {"parent_thread_id": parent, "depth": 1}}}
        self.line(clock, "session_meta", {"id": thread, "timestamp": iso(at(clock)), "cwd": cwd, "originator":
                                          "codex_cli_rs", "cli_version": "0.141.0", "source": source,
                                          "model_provider": "openai"})

    def turn(self, clock, cwd):
        self.line(clock, "turn_context", {"cwd": cwd, "model": "gpt-5.5", "effort": "high",
                                          "approval_policy": "on-request", "summary": "auto"})

    def message(self, clock, role, texts):
        self.line(clock, "response_item", {"type": "message", "role": role,
                                           "content": [{"type": "input_text" if role == "user" else "output_text",
                                                        "text": t} for t in texts]})

    def prompt(self, clock, text):
        self.line(clock, "event_msg", {"type": "task_started", "model_context_window": 400000})
        self.message(clock, "user", [text])

    def done(self, clock, words="Done."):
        self.message(clock, "assistant", [words])
        self.line(clock, "event_msg", {"type": "task_complete", "last_agent_message": None})

    def script(self, clock, call_id, source):
        self.line(clock, "response_item", {"type": "custom_tool_call", "status": "completed", "call_id": call_id,
                                           "name": "exec", "input": source})

    def printed(self, clock, call_id, items, wall="1.2"):
        out = [{"type": "input_text", "text": "Script completed\nWall time %s seconds\nOutput:\n" % wall}]
        out += [{"type": "input_text", "text": i if isinstance(i, str) else json.dumps(i)} for i in items]
        self.line(clock, "response_item", {"type": "custom_tool_call_output", "call_id": call_id, "output": out})

    def function(self, clock, call_id, name, arguments):
        self.line(clock, "response_item", {"type": "function_call", "name": name, "call_id": call_id,
                                           "arguments": json.dumps(arguments)})

    def function_output(self, clock, call_id, output):
        self.line(clock, "response_item", {"type": "function_call_output", "call_id": call_id, "output": output})

    def custom(self, clock, call_id, name, value):
        self.line(clock, "response_item", {"type": "custom_tool_call", "status": "completed", "call_id": call_id,
                                           "name": name, "input": value})

    def custom_output(self, clock, call_id, output):
        self.line(clock, "response_item", {"type": "custom_tool_call_output", "call_id": call_id, "output": output})


def status(output, exit_code=None, session=None, wall=1.0):
    """A unified-exec result as code mode prints it (a JSON object)."""
    obj = {"chunk_id": "c%x" % (len(output) * 7919 % 65536), "wall_time_seconds": wall}
    if exit_code is not None:
        obj["exit_code"] = exit_code
    if session is not None:
        obj["session_id"] = session
    obj["original_token_count"] = max(1, len(output) // 4)
    obj["output"] = output
    return obj


def patch(path, old, new):
    return "*** Begin Patch\n*** Update File: %s\n@@\n-%s\n+%s\n*** End Patch" % (path, old, new)


ORBIT_THREAD = "019a2b3c-4d5e-7f60-8a71-b2c3d4e5f601"


def codex_sessions(out):
    base = os.path.join(out, ROOT, "codex", "sessions", "2026", "10", "27")
    orbit = WORK + "/orbit"
    names = {}

    c = Codex()
    c.meta("10:29:58", ORBIT_THREAD, orbit)
    c.turn("10:29:58", orbit)
    c.message("10:29:59", "user", ["<environment_context>\n  <cwd>%s</cwd>\n  <shell>zsh</shell>\n"
                                   "</environment_context>" % orbit])
    c.prompt("10:30:00", "Make the parser tests pass")
    c.script("10:30:05", "s1", 'text(await tools.exec_command({cmd: "rg -n \\"fn parse\\" src", workdir: "%s", '
                               'max_output_tokens: 4000}));' % orbit)
    c.printed("10:30:06", "s1", [status("src/parse.rs:12:pub fn parse(input: &str) -> Ast {\n", 0)])
    c.script("10:30:10", "s2", 'text(await tools.exec_command({cmd: "cargo test", workdir: "%s", yield_time_ms: '
                               '10000}));' % orbit)
    c.printed("10:30:20", "s2", [status("running 12 tests\n", session=7, wall=10.0)], "10.1")
    c.script("10:30:25", "s3", "text(await tools.write_stdin({session_id: 7, chars: '', yield_time_ms: 30000}));")
    c.printed("10:30:40", "s3", [status(
        "test parse::tests::nested ... FAILED\n\nfailures:\n\n---- parse::tests::nested stdout ----\nthread "
        "'parse::tests::nested' panicked at src/parse.rs:40:9:\nassertion failed: depth < 8\n\nfailures:\n    "
        "parse::tests::nested\n\ntest result: FAILED. 11 passed; 1 failed; 0 ignored; 0 measured; 0 filtered out; "
        "finished in 0.02s\n", 101, wall=14.8)])
    c.script("10:31:00", "s4", "text(await tools.apply_patch(`%s`));" % patch("src/parse.rs", "if depth > 8 {",
                                                                              "if depth >= 8 {"))
    c.printed("10:31:01", "s4", ["Success. Updated the following files:\nM src/parse.rs\n"])
    c.script("10:31:10", "s5", 'const r = await tools.exec_command({cmd: "cargo test", workdir: "%s", '
                               'yield_time_ms: 30000});\ntext(r.output);' % orbit)
    c.printed("10:31:25", "s5", [CARGO_PASS])
    c.done("10:31:30", "The nested test passes now.")
    c.prompt("10:40:00", "繼續")
    c.script("10:40:05", "s6", "text(await tools.apply_patch(`%s`));" % patch("src/lex.rs", "let max = 8;",
                                                                              "let max = MAX_DEPTH;"))
    c.printed("10:40:06", "s6", ["Success. Updated the following files:\nM src/lex.rs\n"])
    c.script("10:40:20", "s7", "const [a, b] = await Promise.all([tools.exec_command({cmd: 'cargo test --quiet'}), "
                               "tools.exec_command({cmd: 'cargo clippy'})]);\ntext(a); text(b);")
    c.printed("10:40:50", "s7", [status(CARGO_PASS, 0), status("    Finished `dev` profile [unoptimized + debuginfo] "
                                                                "target(s) in 0.40s\n", 0)])
    c.done("10:41:00", "Done; both commands finished.")
    c.prompt("10:50:00", "Now run clippy and fix the warnings")
    c.script("10:50:10", "s8", 'text(await tools.exec_command({cmd: "cargo clippy --all-targets", yield_time_ms: '
                               '10000}));')
    c.printed("10:50:20", "s8", [status("    Checking orbit v0.3.0 (%s)\n    Finished `dev` profile [unoptimized + "
                                        "debuginfo] target(s) in 1.20s\n" % orbit, 0)])
    c.script("10:58:30", "s9", 'text(await tools.exec_command({cmd: "cargo test --release", yield_time_ms: '
                               '10000}));')
    c.printed("10:58:41", "s9", [status("   Compiling orbit v0.3.0\n", session=9, wall=10.0)], "10.1")
    names["orbit"] = "rollout-2026-10-27T10-29-58-%s.jsonl" % ORBIT_THREAD
    write_jsonl(os.path.join(base, names["orbit"]), c.rows)

    sub_thread = "019a2b3c-4d5e-7f60-8a71-b2c3d4e5f602"
    s = Codex()
    s.meta("10:52:00", sub_thread, orbit, parent=ORBIT_THREAD)
    s.rows += [dict(r) for r in c.rows[2:6]]
    s.turn("10:52:00", orbit)
    s.prompt("10:52:01", "Review the parser change for edge cases")
    s.script("10:52:05", "r1", "text(await tools.exec_command({cmd: \"sed -n '1,80p' src/parse.rs\"}));")
    s.printed("10:52:06", "r1", [status("pub fn parse(input: &str) -> Ast {\n", 0)])
    s.done("10:53:00", "No further edge cases found.")
    names["codexSub"] = "rollout-2026-10-27T10-52-00-%s.jsonl" % sub_thread
    write_jsonl(os.path.join(base, names["codexSub"]), s.rows)

    comet = WORK + "/comet"
    comet_thread = "019a2a10-0000-7000-8000-00000000c0e7"
    k = Codex()
    k.meta("09:00:00", comet_thread, comet)
    k.turn("09:00:00", comet)
    k.prompt("09:00:01", "Fix the flaky vitest suite")
    k.function("09:00:10", "f1", "exec_command", {"cmd": "npm test", "yield_time_ms": 10000})
    k.function_output("09:00:20", "f1", "Chunk ID: 1a2b\nWall time: 10.0 seconds\nProcess running with session ID 3\n"
                                        "Original token count: 6\nOutput:\n\n> comet@1.0.0 test\n> vitest run\n")
    k.function("09:00:25", "f2", "write_stdin", {"session_id": 3, "chars": "", "yield_time_ms": 30000})
    k.function_output("09:00:30", "f2", "Chunk ID: 3c4d\nWall time: 3.2 seconds\nProcess exited with code 1\n"
                                        "Original token count: 80\nOutput:\n FAIL  src/clock.test.ts > clock > ticks\n"
                                        "AssertionError: expected 2 to be 1\n\n Test Files  1 failed | 2 passed (3)\n"
                                        "      Tests  1 failed | 13 passed (14)\n")
    k.custom("09:01:00", "f3", "apply_patch", patch("src/clock.ts", "return ticks + 1;", "return ticks;"))
    k.custom_output("09:01:01", "f3", json.dumps({"output": "Success. Updated the following files:\nM src/clock.ts\n",
                                                  "metadata": {"exit_code": 0, "duration_seconds": 0.0}}))
    k.function("09:01:10", "f4", "exec_command", {"cmd": "npm test"})
    k.function_output("09:01:15", "f4", "Chunk ID: 5e6f\nWall time: 4.0 seconds\nProcess exited with code 0\n"
                                        "Original token count: 40\nOutput:\n Test Files  3 passed (3)\n"
                                        "      Tests  14 passed (14)\n")
    k.custom("09:02:00", "f5", "apply_patch", patch("src/clock.ts", "// TODO", "// ticks are monotonic"))
    k.custom_output("09:02:01", "f5", json.dumps({"output": "Success. Updated the following files:\nM src/clock.ts\n",
                                                  "metadata": {"exit_code": 0, "duration_seconds": 0.0}}))
    k.done("09:02:10")
    names["comet"] = "rollout-2026-10-27T09-00-00-%s.jsonl" % comet_thread
    write_jsonl(os.path.join(base, names["comet"]), k.rows)

    names["history"] = "rollout-2026-10-27T06-00-00-019a2900-0000-7000-8000-0000000a11ce.jsonl"
    write_jsonl(os.path.join(base, names["history"]), history_rows())
    return {key: "/".join((ROOT, "codex", "sessions", "2026", "10", "27", name)) for key, name in names.items()}


def history_rows():
    """60 short tasks of six shapes, two minutes apart from 06:00: enough complete tasks to calibrate (6.11)."""
    h = Codex()
    cwd = WORK + "/ledger"
    h.meta("06:00:00", "019a2900-0000-7000-8000-0000000a11ce", cwd)
    h.turn("06:00:00", cwd)
    fail = "F.\n=== FAILURES ===\n___ test_total ___\nE   assert 3 == 4\nFAILED tests/test_ledger.py::test_total - " \
           "assert 3 == 4\n1 failed, 1 passed in 0.03s\n"
    passed = "..\n2 passed in 0.02s\n"
    for i in range(60):
        start = 6 * 3600 + i * 120
        clock = lambda s: "%02d:%02d:%02d" % ((start + s) // 3600, (start + s) // 60 % 60, (start + s) % 60)  # noqa
        shape = i % 6
        h.prompt(clock(0), "Task %d: adjust the ledger rounding" % i)
        n = [0]

        def script(source, items, when):
            n[0] += 1
            call = "h%d-%d" % (i, n[0])
            h.script(clock(when), call, source)
            h.printed(clock(when + 1), call, items)

        def edit(when, tag):
            script("text(await tools.apply_patch(`%s`));" % patch("ledger.py", "round(x, %d)" % i,
                                                                  "round(x, %d) # %s" % (i, tag)),
                   ["Success. Updated the following files:\nM ledger.py\n"], when)

        def test(when, ok):
            script('text(await tools.exec_command({cmd: "pytest -q"}));', [status(passed if ok else fail, 0 if ok else 1)],
                   when)
        if shape in (0, 1, 2, 3, 4):
            edit(5, "a")
        if shape == 0:
            test(15, True)
        elif shape == 1:
            test(15, False)
        elif shape == 2:
            test(15, False)
            edit(30, "b")
            test(40, True)
        elif shape == 4:
            for r in range(9):
                script("text(await tools.exec_command({cmd: \"cat ledger.py\"}));", [status("x = 1\n", 0)], 10 + r * 5)
        elif shape == 5:
            script("text(await tools.exec_command({cmd: \"ls\"}));", [status("ledger.py\n", 0)], 5)
        h.done(clock(100))
    return h.rows


# ----------------------------------------------------------------------------- omp and Pi

OPUS = ("anthropic-messages", "anthropic", "claude-opus-5-5")
PI_GPT = ("openai-responses", "openai", "gpt-5.5")


def omp_usage(out_tokens):
    return {"input": 20, "output": out_tokens, "cacheRead": 3000, "cacheWrite": 0, "totalTokens": 3020 + out_tokens,
            "cost": {"input": 0, "output": 0, "cacheRead": 0, "cacheWrite": 0, "total": 0}}


def call(call_id, name, arguments):
    return {"type": "toolCall", "id": call_id, "name": name, "arguments": arguments}


def omp_pi_sessions(out):
    files = {}
    ember = WORK + "/ember"
    created = at("10:10:00")
    sid = uuid7(created, 41)
    o = Session("e1a0")
    o.header(created, sid, ember)
    copied = Session("c0p1")
    copied.user(at("09:00:00"), "An older request copied with a fork")
    copied.assistant(at("09:00:05"), OPUS, [call("tc_old", "read", {"path": "src/date.py"})], omp_usage(30),
                     "toolUse", at("09:00:03"), duration=2000)
    o.copy(copied.entries())
    o.user(at("10:10:05"), "Make the date parser accept ISO weeks")
    o.assistant(at("10:10:10"), OPUS, [call("tc1", "edit", {"path": "src/date.py", "oldText": "    return None",
                                                            "newText": "    return iso_week(s)"})],
                omp_usage(60), "toolUse", at("10:10:07"), duration=3000)
    o.tool_result(at("10:10:11"), "tc1", "edit", "Edited src/date.py")
    o.assistant(at("10:10:15"), OPUS, [call("tc2", "bash", {"command": "pytest -q tests/test_date.py"})],
                omp_usage(40), "toolUse", at("10:10:12"), duration=3000)
    o.tool_result(at("10:10:30"), "tc2", "bash", "F.\n=================================== FAILURES "
                  "===================================\n________________________________ test_iso_week "
                  "________________________________\n    def test_iso_week():\n>       assert parse('2026-W43') == 7\n"
                  "E       AssertionError: assert None == 7\n\ntests/test_date.py:9: AssertionError\n"
                  "=========================== short test summary info ============================\nFAILED "
                  "tests/test_date.py::test_iso_week - AssertionError: assert None == 7\n1 failed, 1 passed in "
                  "0.05s\n\nCommand exited with code 1", is_error=True)
    o.assistant(at("10:10:40"), OPUS, [{"type": "text", "text": "The ISO week test still fails."}], omp_usage(20),
                "stop", at("10:10:35"), duration=5000)
    stem = os.path.join(out, ROOT, "omp", "agent", "sessions", "-Users-dev-work-ember")
    name = "2026-10-27T10-10-00-000Z_%s" % sid
    o.write(os.path.join(stem, name + ".jsonl"))
    r = Session("e2b0")
    r.header(at("10:10:20"), uuid7(at("10:10:20"), 42), ember)
    r.user(at("10:10:21"), "Review the date parser diff")
    r.assistant(at("10:10:25"), OPUS, [call("tr1", "read", {"path": "src/date.py"})], omp_usage(30), "toolUse",
                at("10:10:22"), duration=3000)
    r.tool_result(at("10:10:26"), "tr1", "read", "def parse(s):")
    r.assistant(at("10:10:29"), OPUS, [{"type": "text", "text": "Looks fine."}], omp_usage(10), "stop",
                at("10:10:27"), duration=2000)
    r.write(os.path.join(stem, name, "Reviewer.jsonl"))
    files["omp"] = "/".join((ROOT, "omp", "agent", "sessions", "-Users-dev-work-ember", name + ".jsonl"))
    files["ompSub"] = "/".join((ROOT, "omp", "agent", "sessions", "-Users-dev-work-ember", name, "Reviewer.jsonl"))

    halo = WORK + "/halo"
    created = at("10:50:00")
    sid = uuid7(created, 43)
    p = Session("p1a0", runtime=None)
    p.header(created, sid, halo)
    p.user(at("10:50:05"), "Add a timeout to the HTTP client")
    p.assistant(at("10:50:10"), PI_GPT, [call("pc1", "edit", {"path": "client.go", "edits": [
        {"oldText": "http.Client{}", "newText": "http.Client{Timeout: 5 * time.Second}"}]})], omp_usage(50),
        "toolUse", at("10:50:07"), durationMs=3000)
    p.tool_result(at("10:50:11"), "pc1", "edit", "Successfully replaced 1 block in client.go.")
    p.assistant(at("10:50:15"), PI_GPT, [call("pc2", "bash", {"command": "go test ./..."})], omp_usage(30), "toolUse",
                at("10:50:12"), durationMs=3000)
    p.tool_result(at("10:50:30"), "pc2", "bash", "ok  \texample.com/halo\t0.120s\nok  \texample.com/halo/client\t"
                                                 "0.050s")
    p.assistant(at("10:51:00"), PI_GPT, [call("pc3", "edit", {"path": "client.go", "edits": [
        {"oldText": "// client", "newText": "// client with a 5 s timeout"}]})], omp_usage(30), "toolUse",
        at("10:50:57"), durationMs=3000)
    p.tool_result(at("10:51:01"), "pc3", "edit", "Successfully replaced 1 block in client.go.")
    p.assistant(at("10:51:10"), PI_GPT, [{"type": "text", "text": "Added the timeout."}], omp_usage(10), "stop",
                at("10:51:05"), durationMs=5000)
    p.user(at("10:56:00"), "ok")
    folder = "--Users-dev-work-halo--"
    name = "2026-10-27T10-50-00-000Z_%s.jsonl" % sid
    p.write(os.path.join(out, ROOT, "pi", "agent", "sessions", folder, name))
    files["pi"] = "/".join((ROOT, "pi", "agent", "sessions", folder, name))
    return files


# ----------------------------------------------------------------------------- OpenClaw

OC_SCHEMA = """
CREATE TABLE session_nodes (session_key TEXT NOT NULL PRIMARY KEY, current_session_id TEXT NOT NULL,
  entry_json TEXT NOT NULL, updated_at INTEGER NOT NULL, parent_session_key TEXT, spawned_by TEXT) STRICT;
CREATE TABLE session_windows (session_id TEXT NOT NULL PRIMARY KEY, session_key TEXT NOT NULL,
  previous_session_id TEXT, reason TEXT, created_at INTEGER NOT NULL, updated_at INTEGER NOT NULL,
  transcript_updated_at INTEGER DEFAULT NULL, acp_owned INTEGER NOT NULL DEFAULT 0, model_provider TEXT, model TEXT,
  agent_harness_id TEXT, parent_session_key TEXT, spawned_by TEXT, display_name TEXT) STRICT;
CREATE TABLE transcript_events (session_id TEXT NOT NULL, seq INTEGER NOT NULL, event_json TEXT NOT NULL,
  created_at INTEGER NOT NULL, PRIMARY KEY (session_id, seq)) STRICT;
CREATE TABLE transcript_rewrite_watermarks (session_id TEXT NOT NULL PRIMARY KEY, generation TEXT NOT NULL,
  updated_at INTEGER NOT NULL) STRICT;
"""


class Window:
    def __init__(self, session_id, key, clock, cwd, prefix, harness=None, spawned_by=None):
        self.session_id, self.key, self.prefix = session_id, key, prefix
        self.created, self.harness, self.spawned_by = at(clock), harness, spawned_by
        self.rows, self.count, self.last = [], 0, None
        self.rows.append(({"type": "session", "version": 4, "id": session_id, "timestamp": iso(at(clock)),
                           "cwd": cwd}, at(clock)))

    def entry(self, clock, message):
        self.count += 1
        entry_id = "%s%04x" % (self.prefix, self.count)
        self.rows.append(({"type": "message", "id": entry_id, "parentId": self.last, "timestamp": iso(at(clock)),
                           "message": dict(message, timestamp=at(clock))}, at(clock)))
        self.last = entry_id

    def user(self, clock, words):
        self.entry(clock, {"role": "user", "content": [{"type": "text", "text": words}]})

    def assistant(self, clock, content, stop, started, **extra):
        msg = {"role": "assistant", "content": content, "api": "anthropic-messages", "provider": "anthropic",
               "model": "claude-opus-5-5", "usage": omp_usage(40), "stopReason": stop}
        msg.update(extra)
        self.entry(clock, msg)
        self.rows[-1][0]["message"]["timestamp"] = at(started)

    def result(self, clock, call_id, name, words, details=None, is_error=False):
        msg = {"role": "toolResult", "toolCallId": call_id, "toolName": name,
               "content": [{"type": "text", "text": words}], "isError": is_error}
        if details is not None:
            msg["details"] = details
        self.entry(clock, msg)


def openclaw_database(out):
    rel = "/".join((ROOT, "openclaw", ".openclaw", "agents", "main", "agent", "openclaw-agent.sqlite"))
    path = os.path.join(out, *rel.split("/"))
    for stale in (path, path + "-wal", path + "-shm"):
        if os.path.exists(stale):
            os.remove(stale)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    space = "/Users/dev/.openclaw/workspace"
    main = Window("w-main-0001", "agent:main:main", "10:40:00", space, "m1a0")
    main.user("10:40:05", "Build the landing page")
    main.assistant("10:40:10", [call("oc1", "read", {"path": "README.md"})], "toolUse", "10:40:07")
    main.result("10:40:11", "oc1", "read", "# Landing")
    main.assistant("10:40:15", [call("oc2", "write", {"path": "index.html", "content": "<h1>Hi</h1>\n"})], "toolUse",
                   "10:40:12")
    main.result("10:40:16", "oc2", "write", "Wrote index.html")
    main.assistant("10:40:20", [call("oc3", "exec", {"command": "npm run build"})], "toolUse", "10:40:17")
    main.result("10:41:00", "oc3", "exec", "vite v5.4.0 building for production...\n✓ 12 modules transformed.\n"
                                           "✓ built in 1.20s\n\n(Command exited with code 0)",
                details={"status": "completed", "exitCode": 0})
    main.assistant("10:41:10", [call("oc4", "sessions_spawn", {"task": "Check the page for broken links"})],
                   "toolUse", "10:41:07")
    main.result("10:41:11", "oc4", "sessions_spawn", "Spawned agent:main:subagent:7f1c")
    main.assistant("10:41:30", [{"type": "text", "text": "The page builds."}], "stop", "10:41:25")
    sub = Window("w-sub-0002", "agent:main:subagent:7f1c", "10:55:00", space, "s2b0", spawned_by="agent:main:main")
    sub.user("10:55:01", "Check the page for broken links")
    sub.assistant("10:55:10", [call("os1", "read", {"path": "index.html"})], "toolUse", "10:55:07")
    sub.result("10:55:11", "os1", "read", "<h1>Hi</h1>")
    sub.assistant("10:55:30", [{"type": "text", "text": "No broken links."}], "stop", "10:55:25")
    mirror = Window("w-codex-0003", "agent:main:codex", "10:45:00", space, "x3c0", harness="codex")
    mirror.user("10:45:01", "Refactor the build script")
    mirror.assistant("10:47:00", [call("om1", "exec", {"command": "npm run build"}),
                                  {"type": "text", "text": "Refactored."}], "stop", "10:45:02",
                     __openclaw={"mirrorOrigin": "codex-app-server"})
    db = sqlite3.connect(path)
    db.executescript(OC_SCHEMA)
    for w in (main, sub, mirror):
        updated = w.rows[-1][1]
        db.execute("INSERT INTO session_windows (session_id, session_key, reason, created_at, updated_at, "
                   "transcript_updated_at, acp_owned, agent_harness_id, parent_session_key, spawned_by) "
                   "VALUES (?,?,?,?,?,?,?,?,?,?)", (w.session_id, w.key, "initial", w.created, updated, updated, 0,
                                                    w.harness, w.spawned_by, w.spawned_by))
        db.execute("INSERT INTO session_nodes VALUES (?,?,?,?,?,?)", (w.key, w.session_id, json.dumps(
            {"sessionId": w.session_id}), updated, w.spawned_by, w.spawned_by))
        for seq, (entry, created) in enumerate(w.rows, start=1):
            db.execute("INSERT INTO transcript_events VALUES (?,?,?,?)",
                       (w.session_id, seq, json.dumps(entry, separators=(",", ":"), ensure_ascii=False), created))
    db.commit()
    db.close()
    return {"openclaw": rel, "ocMain": rel + "#w-main-0001", "ocSub": rel + "#w-sub-0002",
            "ocMirror": rel + "#w-codex-0003"}


# ----------------------------------------------------------------------------- expectations

def write(out):
    files = {}
    files.update(codex_sessions(out))
    files.update(claude_sessions(out))
    files.update(omp_pi_sessions(out))
    files.update(openclaw_database(out))
    return files


def meta(files):
    """Reading order (startup order: the copies come after their originals), instants, sets and the range."""
    order = [("codex", files["history"]), ("codex", files["comet"]), ("omp", files["omp"]),
             ("omp", files["ompSub"]), ("claudeCode", files["claudeSub"]), ("claudeCode", files["main"]),
             ("codex", files["orbit"]), ("openclaw", files["openclaw"]), ("claudeCode", files["fork"]),
             ("pi", files["pi"]), ("codex", files["codexSub"]), ("claudeCode", files["desk"])]
    return {"nowMs": NOW, "analysisMs": NOW, "timeZone": "UTC",
            "roots": {"omp": "/".join((ROOT, "omp", "agent", "sessions")), "pi": "/".join((ROOT, "pi", "agent",
                                                                                          "sessions"))},
            "order": [{"source": s, "path": p} for s, p in order],
            "sets": [None, ["codex"], ["claudeCode"], ["claudeDesktop"], ["omp"], ["pi"], ["openclaw"],
                     ["codex", "pi"], ["omp", "openclaw"]],
            "comparison": {"first": "2026-10-27", "last": "2026-10-27"}}


def want(files):
    """Expectations written by hand from METRICS 6.9 and 6.11; each score is V + C + R + E + K as derived beside it."""
    f = files
    return {
        "agents": {
            # the queued prompt's task: Write, piped build read as passed (cargo Finished), Grep without result:
            # V20 C25 R15, E = 10 * 3/8, K4
            f["main"]: {"form": "live", "state": "passing", "score": 67.75, "taskStartMs": at("10:30:40")},
            f["claudeSub"]: {"form": "none", "state": "none", "parentId": f["main"]},
            # the copied prompt is withdrawn; the fork's own edit: E = 10 * 1/8
            f["fork"]: {"form": "resting", "state": "unverified", "score": 1.25},
            f["desk"]: {"source": "claudeDesktop", "form": "none", "state": "none"},
            # clippy passed (check only): V20 C25 R15, E = 10 * 2/8 (clippy, the release test still running), K4
            f["orbit"]: {"form": "live", "state": "passing", "score": 66.5,
                         "lastPass": {"suite": "cargo clippy", "class": "check", "buildOnly": True}},
            f["codexSub"]: {"form": "none", "state": "none", "parentId": f["orbit"]},
            f["comet"]: {"form": "resting", "state": "pending", "vHeld": 40},
            # the ISO-week test fails: V0 C0 R0, E = 10 * 2/8, K4 (a test ran in the block)
            f["omp"]: {"form": "resting", "state": "failing", "score": 6.5,
                       "openFailure": {"class": "test", "errors": 1, "peak": 1, "counted": True, "edited": False}},
            f["ompSub"]: {"form": "none", "state": "none", "parentId": f["omp"]},
            # go test passed, then an edit; "ok" continues: V0 C25 R15, E = 10 * 3/8, K0
            f["pi"]: {"form": "live", "state": "pending", "score": 43.75, "vHeld": 40},
            # build only (vite "built in"): V20 C25 R15, E = 10 * 4/8 (read, write, exec, spawn), K4
            f["ocMain"]: {"form": "resting", "state": "passing", "score": 69.0},
            f["ocSub"]: {"form": "none", "state": "none", "parentId": f["ocMain"]},
            # the delegated window's mirror adds no actions: a task without a score
            f["ocMirror"]: {"form": "none", "state": "none", "taskStartMs": at("10:45:01")},
        },
        "records": {
            # task 1 with its continuation: piped fail, edit, pass, edit, background pass stitched by the notice:
            # V40 C25 R15, E = 10 * 7/8, K10
            f["main"]: [{"state": "passing", "score": 98.75, "complete": True, "startMs": at("10:20:00")},
                        {"state": "passing", "score": 67.75, "complete": False, "startMs": at("10:30:40")}],
            f["fork"]: [{"state": "unverified", "startMs": at("10:45:00")}],
            # task 1 with "繼續": fail stitched through write_stdin, patch, pass, patch, Promise.all without
            # results = pending: V0 C25 R15, E = 10 * 7/8, K0
            f["orbit"]: [{"state": "pending", "score": 48.75, "complete": True,
                          "counts": {"runs": 2, "failingRuns": 1, "recovered": 1, "edits": 2}},
                         {"state": "passing", "score": 66.5, "complete": False}],
            f["comet"]: [{"state": "pending", "complete": True, "counts": {"runs": 2, "failingRuns": 1}}],
            f["omp"]: [{"state": "failing", "score": 6.5}],
            f["pi"]: [{"state": "pending", "score": 43.75, "complete": False}],
            f["ocMain"]: [{"state": "passing", "score": 69.0}],
            f["ocMirror"]: [],
        },
        "sets": [
            {"sources": None, "form": "live", "score": 67.75, "agent": f["main"]},
            {"sources": ["codex"], "form": "live", "score": 66.5, "agent": f["orbit"]},
            {"sources": ["claudeCode"], "form": "live", "score": 67.75},
            {"sources": ["claudeDesktop"], "form": "none"},
            {"sources": ["omp"], "form": "none"},
            {"sources": ["pi"], "form": "live", "score": 43.75},
            {"sources": ["openclaw"], "form": "resting", "score": 69.0, "agent": f["ocMain"]},
            {"sources": ["codex", "pi"], "form": "live", "score": 66.5},
            {"sources": ["omp", "openclaw"], "form": "resting", "score": 69.0},
        ],
        "groups": {
            f["orbit"]: {"form": "live", "score": 66.5, "members": [f["orbit"], f["codexSub"]]},
            f["ocMain"]: {"form": "resting", "score": 69.0, "members": [f["ocMain"], f["ocSub"]]},
            f["main"]: {"form": "live", "score": 67.75, "members": [f["main"]]},
        },
        "calibration": {"codex": {"success": {"calibrated": True}, "stall": {"calibrated": True},
                                  "fix": {"calibrated": False}},
                        "claudeCode": {"success": {"calibrated": False}}},
        "stitched": {f["orbit"]: 1, f["comet"]: 1, f["main"]: 1},
    }


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..",
                                                                "fixtures")
    files = write(out)
    print(json.dumps(meta(files), indent=1))


if __name__ == "__main__":
    main()
