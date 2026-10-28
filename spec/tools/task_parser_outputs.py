"""Hand-written tool outputs for the task-score parsers (spec/TASK-PARSERS.md 5), read by task_parser_corpus.py.

Every case is fictional: written from knowledge of each tool's output format (2025-2026 versions), never copied from
real logs. `output` is the combined stdout and stderr an agent sees through a pipe; colour codes appear only where the
command forces them. `expect` holds the author's own reading of the output, written before the reference existed:
`failedCount` / `passedCount` as the summary line states them and the failing tests and errors as printed. The corpus
checks the reference against it (task_parser_corpus.hand_check), with the documented deviations listed there.
"""

# ---- part A: Python, Rust, Swift, Go, C and C++, CTest, .NET


_PART_A = []

# ---------------------------------------------------------------------------
# pytest
# ---------------------------------------------------------------------------

_PART_A += [
    {
        "id": "pytest-pass",
        "framework": "pytest",
        "command": "python -m pytest",
        "exitCode": 0,
        "output": """\
============================= test session starts ==============================
platform darwin -- Python 3.12.7, pytest-8.3.4, pluggy-1.5.0
rootdir: /Users/dev/work/orbit
configfile: pyproject.toml
plugins: cov-6.0.0
collected 24 items

tests/test_ledger.py ........                                            [ 33%]
tests/test_parser.py ......                                              [ 58%]
tests/test_queue.py ..........                                           [100%]

============================== 24 passed in 0.41s ==============================
""",
        "expect": {
            "failedCount": 0,
            "passedCount": 24,
            "failingTests": [],
            "errors": [],
        },
        "note": "Summary has no 'failed' term; failedCount 0 is implied by a present summary line.",
    },
    {
        "id": "pytest-fail-summary",
        "framework": "pytest",
        "command": "python -m pytest tests/",
        "exitCode": 1,
        "output": """\
============================= test session starts ==============================
platform linux -- Python 3.12.3, pytest-8.3.4, pluggy-1.5.0
rootdir: /home/dev/orbit
configfile: pyproject.toml
collected 12 items

tests/test_ledger.py ....F.                                              [ 50%]
tests/test_queue.py ..F...                                               [100%]

=================================== FAILURES ===================================
____________________________ test_balance_rounding _____________________________

    def test_balance_rounding():
        ledger = Ledger()
        ledger.post("cash", Decimal("10.005"))
>       assert ledger.balance("cash") == Decimal("10.01")
E       AssertionError: assert Decimal('10.00') == Decimal('10.01')
E        +  where Decimal('10.00') = balance('cash')
E        +    where balance = <orbit.ledger.Ledger object at 0x7f3a2c1e5d90>.balance
E        +  and   Decimal('10.01') = Decimal('10.01')

tests/test_ledger.py:41: AssertionError
________________________________ test_pop_empty ________________________________

    def test_pop_empty():
        q = Queue()
>       with pytest.raises(QueueEmpty):
E       Failed: DID NOT RAISE <class 'orbit.queue.QueueEmpty'>

tests/test_queue.py:27: Failed
=========================== short test summary info ============================
FAILED tests/test_ledger.py::test_balance_rounding - AssertionError: assert D...
FAILED tests/test_queue.py::test_pop_empty - Failed: DID NOT RAISE <class 'or...
========================= 2 failed, 10 passed in 1.23s =========================
""",
        "expect": {
            "failedCount": 2,
            "passedCount": 10,
            "failingTests": [
                "tests/test_ledger.py::test_balance_rounding",
                "tests/test_queue.py::test_pop_empty",
            ],
            "errors": [],
        },
        "note": "Non-TTY width is 80, so short-summary messages are trimmed with '...' (pytest does this unless CI is set or -vv).",
    },
    {
        "id": "pytest-param-collection-error",
        "framework": "pytest",
        "command": "python -m pytest --continue-on-collection-errors",
        "exitCode": 1,
        "output": """\
============================= test session starts ==============================
platform darwin -- Python 3.13.1, pytest-8.3.4, pluggy-1.5.0
rootdir: /Users/dev/work/parser
configfile: pyproject.toml
collected 17 items / 1 error

tests/test_lexer.py ......                                               [ 35%]
tests/test_parse_numbers.py ..F.F..                                      [ 76%]
tests/test_tokens.py ....                                                [100%]

==================================== ERRORS ====================================
____________________ ERROR collecting tests/test_grammar.py ____________________
ImportError while importing test module '/Users/dev/work/parser/tests/test_grammar.py'.
Hint: make sure your test modules/packages have valid Python names.
Traceback:
/opt/homebrew/Cellar/python@3.13/3.13.1/Frameworks/Python.framework/Versions/3.13/lib/python3.13/importlib/__init__.py:88: in import_module
    return _bootstrap._gcd_import(name[level:], package, level)
tests/test_grammar.py:3: in <module>
    from parser.grammar import Rule, compile_rules
E   ImportError: cannot import name 'compile_rules' from 'parser.grammar' (/Users/dev/work/parser/src/parser/grammar.py)
=================================== FAILURES ===================================
__________________________ test_parse_number[case-1] ___________________________

text = '1_000', expected = 1000

    @pytest.mark.parametrize("text,expected", CASES, ids=CASE_IDS)
    def test_parse_number(text, expected):
>       assert parse_number(text) == expected

tests/test_parse_numbers.py:18:
_ _ _ _ _ _ _ _ _ _ _ _ _ _ _ _ _ _ _ _ _ _ _ _ _ _ _ _ _ _ _ _ _ _ _ _ _ _ _ _ 

text = '1_000'

    def parse_number(text: str) -> int | float:
        if text.lower().startswith("0x"):
            return int(text[2:], 16)
>       return int(text) if text.isdigit() else float(text)
E       ValueError: could not convert string to float: '1_000'

src/parser/numbers.py:9: ValueError
_________________________ test_parse_number[hex-0x1F] __________________________

text = '0x1F', expected = 31

    @pytest.mark.parametrize("text,expected", CASES, ids=CASE_IDS)
    def test_parse_number(text, expected):
>       assert parse_number(text) == expected
E       AssertionError: assert 25 == 31
E        +  where 25 = parse_number('0x1F')

tests/test_parse_numbers.py:18: AssertionError
=========================== short test summary info ============================
FAILED tests/test_parse_numbers.py::test_parse_number[case-1] - ValueError: c...
FAILED tests/test_parse_numbers.py::test_parse_number[hex-0x1F] - AssertionEr...
ERROR tests/test_grammar.py
==================== 2 failed, 15 passed, 1 error in 0.38s =====================
""",
        "expect": {
            "failedCount": 2,
            "passedCount": 15,
            "failingTests": [
                "tests/test_parse_numbers.py::test_parse_number[case-1]",
                "tests/test_parse_numbers.py::test_parse_number[hex-0x1F]",
            ],
            "errors": [
                {"file": "tests/test_grammar.py", "code": None},
            ],
        },
        "note": "Collection ERROR is a module import failure, listed under errors (not failingTests); its short-summary line has no message. '_ _ _' frame separator line ends with a trailing space.",
    },
    {
        "id": "pytest-quiet-rerun-flaky",
        "framework": "pytest",
        "command": "python -m pytest -q --reruns 2 --tb=short tests/integration",
        "exitCode": 1,
        "output": """\
..............RRF...........................                             [100%]
=================================== FAILURES ===================================
__________________________ test_sync_after_reconnect ___________________________
tests/integration/test_sync.py:58: in test_sync_after_reconnect
    assert client.pending() == 0
E   AssertionError: assert 3 == 0
E    +  where 3 = pending()
E    +    where pending = <orbit.sync.Client object at 0x104b2e6d0>.pending
=========================== short test summary info ============================
FAILED tests/integration/test_sync.py::test_sync_after_reconnect - AssertionE...
1 failed, 41 passed, 2 rerun in 3.07s
""",
        "expect": {
            "failedCount": 1,
            "passedCount": 41,
            "failingTests": [
                "tests/integration/test_sync.py::test_sync_after_reconnect",
            ],
            "errors": [],
        },
        "note": "-q prints no session header and the final stats line has no '=' padding; 'R' progress marks and '2 rerun' are reruns, not extra failures.",
    },
    {
        "id": "pytest-color-tb-line",
        "framework": "pytest",
        "command": "python -m pytest --color=yes --tb=line tests/test_queue.py",
        "exitCode": 1,
        "output": """\
\x1b[1m============================= test session starts ==============================\x1b[0m
platform darwin -- Python 3.12.7, pytest-8.3.4, pluggy-1.5.0
rootdir: /Users/dev/work/orbit
configfile: pyproject.toml
plugins: anyio-4.6.2
collected 6 items

tests/test_queue.py \x1b[32m.\x1b[0m\x1b[32m.\x1b[0m\x1b[31mF\x1b[0m\x1b[32m.\x1b[0m\x1b[32m.\x1b[0m\x1b[32m.\x1b[0m\x1b[31m                                               [100%]\x1b[0m

=================================== FAILURES ===================================
/Users/dev/work/orbit/tests/test_queue.py:33: AssertionError: assert [3, 1, 2] == [1, 2, 3]
\x1b[36m\x1b[1m=========================== short test summary info ============================\x1b[0m
\x1b[31mFAILED\x1b[0m tests/test_queue.py::\x1b[1mtest_drain_order\x1b[0m - AssertionError: assert [3, 1, 2] == [1, 2, 3]
\x1b[31m========================= \x1b[31m\x1b[1m1 failed\x1b[0m, \x1b[32m5 passed\x1b[0m\x1b[31m in 0.09s\x1b[0m\x1b[31m ==========================\x1b[0m
""",
        "expect": {
            "failedCount": 1,
            "passedCount": 5,
            "failingTests": ["tests/test_queue.py::test_drain_order"],
            "errors": [],
        },
        "note": "ANSI codes split the node id ('::' then bold test name); --tb=line gives no per-test header, so the test id appears only in the short summary. Colour also disables summary trimming.",
    },
]

# ---------------------------------------------------------------------------
# unittest
# ---------------------------------------------------------------------------

_PART_A += [
    {
        "id": "unittest-ok",
        "framework": "unittest",
        "command": "python -m unittest discover -s tests",
        "exitCode": 0,
        "output": """\
......................
----------------------------------------------------------------------
Ran 22 tests in 0.014s

OK
""",
        "expect": {
            "failedCount": 0,
            "passedCount": 22,
            "failingTests": [],
            "errors": [],
        },
        "note": "unittest never prints a passed count; passedCount is derived from 'Ran 22 tests' + 'OK'.",
    },
    {
        "id": "unittest-fail-error-py312",
        "framework": "unittest",
        "command": "python -m unittest",
        "exitCode": 1,
        "output": """\
..F.E....
======================================================================
ERROR: test_load_missing_file (tests.test_ledger.LedgerIOTests.test_load_missing_file)
----------------------------------------------------------------------
Traceback (most recent call last):
  File "/home/dev/orbit/tests/test_ledger.py", line 48, in test_load_missing_file
    ledger = Ledger.load(self.tmp / "missing.json")
             ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
  File "/home/dev/orbit/orbit/ledger.py", line 31, in load
    with open(path, encoding="utf-8") as fh:
         ^^^^^^^^^^^^^^^^^^^^^^^^^^^^
FileNotFoundError: [Errno 2] No such file or directory: '/tmp/tmpk2v9x1qa/missing.json'

======================================================================
FAIL: test_push_grows (tests.test_queue.QueueTests.test_push_grows)
----------------------------------------------------------------------
Traceback (most recent call last):
  File "/home/dev/orbit/tests/test_queue.py", line 19, in test_push_grows
    self.assertEqual(len(q), 3)
AssertionError: 2 != 3

----------------------------------------------------------------------
Ran 9 tests in 0.006s

FAILED (failures=1, errors=1)
""",
        "expect": {
            "failedCount": 2,
            "passedCount": 7,
            "failingTests": [
                "tests.test_ledger.LedgerIOTests.test_load_missing_file",
                "tests.test_queue.QueueTests.test_push_grows",
            ],
            "errors": [],
        },
        "note": "failedCount = failures + errors; ERROR block is printed before FAIL. Test ids use the dotted name inside the parentheses (3.11+ format).",
    },
    {
        "id": "unittest-django-manage-test",
        "framework": "unittest",
        "command": "python manage.py test ledger",
        "exitCode": 1,
        "output": """\
Found 14 test(s).
Creating test database for alias 'default'...
System check identified no issues (0 silenced).
.....F........
======================================================================
FAIL: test_invoice_total_includes_tax (ledger.tests.test_invoices.InvoiceTotalsTests.test_invoice_total_includes_tax)
----------------------------------------------------------------------
Traceback (most recent call last):
  File "/home/dev/ledger/ledger/tests/test_invoices.py", line 37, in test_invoice_total_includes_tax
    self.assertEqual(invoice.total, Decimal("107.50"))
AssertionError: Decimal('100.00') != Decimal('107.50')

----------------------------------------------------------------------
Ran 14 tests in 0.231s

FAILED (failures=1)
Destroying test database for alias 'default'...
""",
        "expect": {
            "failedCount": 1,
            "passedCount": 13,
            "failingTests": [
                "ledger.tests.test_invoices.InvoiceTotalsTests.test_invoice_total_includes_tax",
            ],
            "errors": [],
        },
        "note": "Django wraps unittest; summary is not the last line (database teardown follows).",
    },
]

# ---------------------------------------------------------------------------
# cargo-test
# ---------------------------------------------------------------------------

_PART_A += [
    {
        "id": "cargo-test-workspace-pass",
        "framework": "cargo-test",
        "command": "cargo test --workspace",
        "exitCode": 0,
        "output": """\
   Compiling orbit-core v0.4.2 (/Users/dev/work/orbit/crates/orbit-core)
   Compiling orbit-cli v0.4.2 (/Users/dev/work/orbit/crates/orbit-cli)
    Finished `test` profile [unoptimized + debuginfo] target(s) in 3.41s
     Running unittests src/main.rs (target/debug/deps/orbit-8c21d0e9a7b6f514)

running 2 tests
test args::tests::rejects_unknown_flag ... ok
test args::tests::parses_verbose_flag ... ok

test result: ok. 2 passed; 0 failed; 0 ignored; 0 measured; 0 filtered out; finished in 0.00s

     Running unittests src/lib.rs (target/debug/deps/orbit_core-3f9a1c2b7d4e5f60)

running 6 tests
test ledger::tests::post_balances ... ok
test queue::tests::new_is_empty ... ok
test ledger::tests::rejects_negative ... ok
test queue::tests::pop_returns_fifo ... ok
test queue::tests::push_grows ... ok
test ledger::tests::round_half_even ... ok

test result: ok. 6 passed; 0 failed; 0 ignored; 0 measured; 0 filtered out; finished in 0.00s

     Running tests/integration.rs (target/debug/deps/integration-1b7e9a2c4d6f8031)

running 3 tests
test survives_restart ... ignored, needs fixture data
test drains_in_order ... ok
test roundtrip_json ... ok

test result: ok. 2 passed; 0 failed; 1 ignored; 0 measured; 0 filtered out; finished in 0.02s

   Doc-tests orbit_core

running 2 tests
test crates/orbit-core/src/lib.rs - (line 3) ... ok
test crates/orbit-core/src/queue.rs - queue::Queue::push (line 27) ... ok

test result: ok. 2 passed; 0 failed; 0 ignored; 0 measured; 0 filtered out; finished in 0.18s

""",
        "expect": {
            "failedCount": 0,
            "passedCount": 12,
            "failingTests": [],
            "errors": [],
        },
        "note": "Four 'test result:' lines must be summed (2+6+2+2); one test is ignored. Doc-test paths are relative to the workspace root (assumed).",
    },
    {
        "id": "cargo-test-fail-panics",
        "framework": "cargo-test",
        "command": "cargo test",
        "exitCode": 101,
        "output": """\
   Compiling orbit v0.3.0 (/home/dev/orbit)
    Finished `test` profile [unoptimized + debuginfo] target(s) in 1.87s
     Running unittests src/lib.rs (target/debug/deps/orbit-a41c9e07b2d35f18)

running 8 tests
test ledger::tests::post_balances ... ok
test queue::tests::new_is_empty ... ok
test ledger::tests::rejects_negative ... FAILED
test queue::tests::pop_returns_fifo ... ok
test queue::tests::push_grows ... FAILED
test parser::tests::parses_empty ... ok
test parser::tests::parses_nested ... ok
test parser::tests::rejects_trailing_comma ... ok

failures:

---- ledger::tests::rejects_negative stdout ----

thread 'ledger::tests::rejects_negative' panicked at src/ledger.rs:88:9:
expected Err(NegativeAmount), got Ok(())
note: run with `RUST_BACKTRACE=1` environment variable to display a backtrace

---- queue::tests::push_grows stdout ----

thread 'queue::tests::push_grows' panicked at src/queue.rs:54:9:
assertion `left == right` failed
  left: 2
 right: 3


failures:
    ledger::tests::rejects_negative
    queue::tests::push_grows

test result: FAILED. 6 passed; 2 failed; 0 ignored; 0 measured; 0 filtered out; finished in 0.01s

error: test failed, to rerun pass `--lib`
""",
        "expect": {
            "failedCount": 2,
            "passedCount": 6,
            "failingTests": [
                "ledger::tests::rejects_negative",
                "queue::tests::push_grows",
            ],
            "errors": [],
        },
        "note": "Each failing name appears three times (progress line, '---- stdout ----' header, failures list); the backtrace note is printed only once per process.",
    },
    {
        "id": "cargo-test-doctest-fail-color",
        "framework": "cargo-test",
        "command": "CARGO_TERM_COLOR=always cargo test",
        "exitCode": 101,
        "output": """\
\x1b[1m\x1b[92m   Compiling\x1b[0m ledger v0.2.1 (/Users/dev/work/ledger)
\x1b[1m\x1b[92m    Finished\x1b[0m `test` profile [unoptimized + debuginfo] target(s) in 0.94s
\x1b[1m\x1b[92m     Running\x1b[0m unittests src/lib.rs (target/debug/deps/ledger-0d9c3b5a7e1f2468)

running 4 tests
test tests::add_zero ... ok
test tests::add_overflow_saturates ... ok
test tests::mul_basic ... ok
test tests::sub_basic ... ok

test result: ok. 4 passed; 0 failed; 0 ignored; 0 measured; 0 filtered out; finished in 0.00s

\x1b[1m\x1b[92m   Doc-tests\x1b[0m ledger

running 3 tests
test src/lib.rs - Ledger (line 41) ... ok
test src/lib.rs - add (line 12) ... FAILED
test src/lib.rs - sub (line 27) ... ok

failures:

---- src/lib.rs - add (line 12) stdout ----
Test executable failed (exit status: 101).

stderr:

thread 'main' panicked at src/lib.rs:15:1:
assertion `left == right` failed
  left: 5
 right: 4
note: run with `RUST_BACKTRACE=1` environment variable to display a backtrace



failures:
    src/lib.rs - add (line 12)

test result: FAILED. 2 passed; 1 failed; 0 ignored; 0 measured; 0 filtered out; finished in 0.21s

\x1b[1m\x1b[91merror\x1b[0m\x1b[1m:\x1b[0m doctest failed, to rerun pass `--doc`
""",
        "expect": {
            "failedCount": 1,
            "passedCount": 6,
            "failingTests": ["src/lib.rs - add (line 12)"],
            "errors": [],
        },
        "note": "CARGO_TERM_COLOR colours only cargo's own status lines; libtest output stays plain because stdout is a pipe. Doc-test names contain spaces and parentheses. Edition 2021 (unmerged) doctest failure layout.",
    },
    {
        "id": "cargo-nextest-fail",
        "framework": "cargo-test",
        "command": "cargo nextest run",
        "exitCode": 100,
        "output": """\
   Compiling orbit v0.3.0 (/Users/dev/work/orbit)
    Finished `test` profile [unoptimized + debuginfo] target(s) in 2.05s
────────────
 Nextest run ID 6f0b3c1e-2a4d-4e8f-9b7a-1c2d3e4f5a6b with nextest profile: default
    Starting 10 tests across 2 binaries
        PASS [   0.004s] orbit ledger::tests::post_balances
        PASS [   0.004s] orbit ledger::tests::rejects_negative
        PASS [   0.005s] orbit parser::tests::parses_empty
        PASS [   0.005s] orbit parser::tests::parses_nested
        FAIL [   0.006s] orbit queue::tests::push_grows
  stdout ───

    running 1 test
    test queue::tests::push_grows ... FAILED

    failures:

    failures:
        queue::tests::push_grows

    test result: FAILED. 0 passed; 1 failed; 0 ignored; 0 measured; 6 filtered out; finished in 0.00s

  stderr ───

    thread 'queue::tests::push_grows' panicked at src/queue.rs:54:9:
    assertion `left == right` failed
      left: 2
     right: 3
    note: run with `RUST_BACKTRACE=1` environment variable to display a backtrace

        PASS [   0.004s] orbit queue::tests::new_is_empty
        PASS [   0.005s] orbit queue::tests::pop_returns_fifo
        PASS [   0.003s] orbit::integration drains_in_order
        PASS [   0.004s] orbit::integration roundtrip_json
        PASS [   0.011s] orbit::integration survives_restart
────────────
     Summary [   0.031s] 10 tests run: 9 passed, 1 failed, 0 skipped
        FAIL [   0.006s] orbit queue::tests::push_grows
error: test run failed
""",
        "expect": {
            "failedCount": 1,
            "passedCount": 9,
            "failingTests": ["queue::tests::push_grows"],
            "errors": [],
        },
        "note": "Captured libtest output embeds a decoy 'test result: FAILED. 0 passed; 1 failed' line; the real totals are on the 'Summary' line. failingTests drops the binary-id column ('orbit'). Indented 'stdout ───' capture headers follow nextest 0.9.8x+ (older versions differ).",
    },
    {
        "id": "cargo-test-nocapture-interleaved",
        "framework": "cargo-test",
        "command": "cargo test -- --nocapture",
        "exitCode": 101,
        "output": """\
    Finished `test` profile [unoptimized + debuginfo] target(s) in 0.11s
     Running unittests src/lib.rs (target/debug/deps/orbit-a41c9e07b2d35f18)

running 5 tests
seeding queue with 3 items
test queue::tests::new_is_empty ... ok
seeding queue with 3 items
connecting to 127.0.0.1:7411
test queue::tests::pop_returns_fifo ... seeding queue with 2 items
ok

thread 'sync::tests::reconnects' panicked at src/sync.rs:112:13:
timed out waiting for ack after 50ms
note: run with `RUST_BACKTRACE=1` environment variable to display a backtrace
test queue::tests::push_grows ... ok
connecting to 127.0.0.1:7412
test sync::tests::reconnects ... FAILED
test sync::tests::handshake ... ok

failures:

failures:
    sync::tests::reconnects

test result: FAILED. 4 passed; 1 failed; 0 ignored; 0 measured; 0 filtered out; finished in 0.06s

error: test failed, to rerun pass `--lib`
""",
        "expect": {
            "failedCount": 1,
            "passedCount": 4,
            "failingTests": ["sync::tests::reconnects"],
            "errors": [],
        },
        "note": "Parallel test threads print with --nocapture: a println splits 'test ... ok' across two lines and the panic message appears far from its test; no '---- stdout ----' blocks.",
    },
]

# ---------------------------------------------------------------------------
# rustc (via cargo)
# ---------------------------------------------------------------------------

_PART_A += [
    {
        "id": "rustc-build-e0308-e0425",
        "framework": "rustc",
        "command": "cargo build",
        "exitCode": 101,
        "output": """\
   Compiling orbit v0.3.0 (/Users/dev/work/orbit)
error[E0425]: cannot find value `capacity` in this scope
  --> src/queue.rs:21:42
   |
21 |         Self { items: Vec::with_capacity(capacity), head: 0 }
   |                                          ^^^^^^^^ not found in this scope

warning: unused import: `std::collections::HashMap`
 --> src/ledger.rs:3:5
  |
3 | use std::collections::HashMap;
  |     ^^^^^^^^^^^^^^^^^^^^^^^^^
  |
  = note: `#[warn(unused_imports)]` on by default

error[E0308]: mismatched types
  --> src/ledger.rs:42:9
   |
40 |     pub fn balance(&self, account: &str) -> i64 {
   |                                             --- expected `i64` because of return type
41 |         let total: u64 = self.entries(account).map(|e| e.amount).sum();
42 |         total
   |         ^^^^^ expected `i64`, found `u64`
   |
help: you can convert a `u64` to an `i64` and panic if the converted value doesn't fit
   |
42 |         total.try_into().unwrap()
   |              ++++++++++++++++++++

Some errors have detailed explanations: E0308, E0425.
For more information about an error, try `rustc --explain E0308`.
error: could not compile `orbit` (lib) due to 2 previous errors; 1 warning emitted
""",
        "expect": {
            "failedCount": 2,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "src/queue.rs", "code": "E0425"},
                {"file": "src/ledger.rs", "code": "E0308"},
            ],
        },
        "note": "The unused-import warning must not be counted; 'error: could not compile' is a summary, not a diagnostic.",
    },
    {
        "id": "rustc-clippy-deny-warnings",
        "framework": "rustc",
        "command": "cargo clippy -- -D warnings",
        "exitCode": 101,
        "output": """\
    Checking orbit v0.3.0 (/home/dev/orbit)
error: unneeded `return` statement
  --> src/queue.rs:33:9
   |
33 |         return self.items.is_empty();
   |         ^^^^^^^^^^^^^^^^^^^^^^^^^^^^
   |
   = help: for further information visit https://rust-lang.github.io/rust-clippy/master/index.html#needless_return
   = note: `-D clippy::needless-return` implied by `-D warnings`
   = help: to override `-D warnings` add `#[allow(clippy::needless_return)]`
help: remove `return`
   |
33 -         return self.items.is_empty();
33 +         self.items.is_empty()
   |

error: manual `RangeInclusive::contains` implementation
  --> src/ledger.rs:71:12
   |
71 |         if amount >= MIN_AMOUNT && amount <= MAX_AMOUNT {
   |            ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^ help: use: `(MIN_AMOUNT..=MAX_AMOUNT).contains(&amount)`
   |
   = help: for further information visit https://rust-lang.github.io/rust-clippy/master/index.html#manual_range_contains
   = note: `-D clippy::manual-range-contains` implied by `-D warnings`
   = help: to override `-D warnings` add `#[allow(clippy::manual_range_contains)]`

error: could not compile `orbit` (lib) due to 2 previous errors
""",
        "expect": {
            "failedCount": 2,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "src/queue.rs", "code": None},
                {"file": "src/ledger.rs", "code": None},
            ],
        },
        "note": "Clippy lints have no bracketed code; the lint name only appears in the '= note'/'= help' lines, so code is None here.",
    },
    {
        "id": "rustc-cargo-test-compile-fail",
        "framework": "rustc",
        "command": "cargo test",
        "exitCode": 101,
        "output": """\
   Compiling orbit v0.3.0 (/Users/dev/work/orbit)
error[E0433]: failed to resolve: use of undeclared type `Ledgr`
 --> tests/integration.rs:9:17
  |
9 |     let mut l = Ledgr::new();
  |                 ^^^^^
  |                 |
  |                 use of undeclared type `Ledgr`
  |                 help: a struct with a similar name exists: `Ledger`

For more information about this error, try `rustc --explain E0433`.
error: could not compile `orbit` (test "integration") due to 1 previous error
warning: build failed, waiting for other jobs to finish...
error[E0599]: no method named `peek_front` found for struct `Queue<i32>` in the current scope
  --> src/queue.rs:88:22
   |
12 | pub struct Queue<T> {
   | ------------------- method `peek_front` not found for this struct
...
88 |         assert_eq!(q.peek_front(), Some(&1));
   |                      ^^^^^^^^^^
   |
help: there is a method `peek` with a similar name
   |
88 -         assert_eq!(q.peek_front(), Some(&1));
88 +         assert_eq!(q.peek(), Some(&1));
   |

For more information about this error, try `rustc --explain E0599`.
error: could not compile `orbit` (lib test) due to 1 previous error
""",
        "expect": {
            "failedCount": 2,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "tests/integration.rs", "code": "E0433"},
                {"file": "src/queue.rs", "code": "E0599"},
            ],
        },
        "note": "No test ran: two crates fail to compile in parallel, so there are two 'could not compile' summaries (1 + 1) and no 'test result:' line.",
    },
]

# ---------------------------------------------------------------------------
# xctest
# ---------------------------------------------------------------------------

_PART_A += [
    {
        "id": "xctest-macos-fail",
        "framework": "xctest",
        "command": "swift test",
        "exitCode": 1,
        "output": """\
Building for debugging...
[1/6] Write sources
[2/6] Write swift-version--58304C5D6DBC2206.txt
[4/8] Compiling OrbitTests QueueTests.swift
[5/8] Emitting module OrbitTests
[6/8] Linking OrbitPackageTests
Build complete! (2.84s)
Test Suite 'All tests' started at 2026-10-27 14:03:11.482.
Test Suite 'OrbitPackageTests.xctest' started at 2026-10-27 14:03:11.483.
Test Suite 'LedgerTests' started at 2026-10-27 14:03:11.483.
Test Case '-[OrbitTests.LedgerTests testPostBalances]' started.
Test Case '-[OrbitTests.LedgerTests testPostBalances]' passed (0.001 seconds).
Test Case '-[OrbitTests.LedgerTests testRejectsNegative]' started.
Test Case '-[OrbitTests.LedgerTests testRejectsNegative]' passed (0.000 seconds).
Test Suite 'LedgerTests' passed at 2026-10-27 14:03:11.485.
\t Executed 2 tests, with 0 failures (0 unexpected) in 0.001 (0.002) seconds
Test Suite 'QueueTests' started at 2026-10-27 14:03:11.485.
Test Case '-[OrbitTests.QueueTests testPeekEmpty]' started.
/Users/dev/work/orbit/Tests/OrbitTests/QueueTests.swift:27: error: -[OrbitTests.QueueTests testPeekEmpty] : XCTAssertNil failed: "0"
Test Case '-[OrbitTests.QueueTests testPeekEmpty]' failed (0.001 seconds).
Test Case '-[OrbitTests.QueueTests testPopReturnsFIFO]' started.
Test Case '-[OrbitTests.QueueTests testPopReturnsFIFO]' passed (0.000 seconds).
Test Case '-[OrbitTests.QueueTests testPush]' started.
/Users/dev/work/orbit/Tests/OrbitTests/QueueTests.swift:12: error: -[OrbitTests.QueueTests testPush] : XCTAssertEqual failed: ("2") is not equal to ("3") - count after three pushes
Test Case '-[OrbitTests.QueueTests testPush]' failed (0.002 seconds).
Test Suite 'QueueTests' failed at 2026-10-27 14:03:11.489.
\t Executed 3 tests, with 2 failures (0 unexpected) in 0.003 (0.004) seconds
Test Suite 'OrbitPackageTests.xctest' failed at 2026-10-27 14:03:11.489.
\t Executed 5 tests, with 2 failures (0 unexpected) in 0.004 (0.006) seconds
Test Suite 'All tests' failed at 2026-10-27 14:03:11.489.
\t Executed 5 tests, with 2 failures (0 unexpected) in 0.004 (0.007) seconds
◇ Test run started.
↳ Testing Library Version: 6.2 (3fdabe5392108d8)
↳ Target Platform: arm64e-apple-macos14.0
✔ Test run with 0 tests in 0 suites passed after 0.001 seconds.
""",
        "expect": {
            "failedCount": 2,
            "passedCount": 3,
            "failingTests": [
                "-[OrbitTests.QueueTests testPeekEmpty]",
                "-[OrbitTests.QueueTests testPush]",
            ],
            "errors": [],
        },
        "note": "Nested 'Executed' summaries (suite, bundle, All tests) must not be summed; a trailing empty swift-testing run reports 'passed' even though XCTest failed. passedCount derived from Executed minus failures.",
    },
    {
        "id": "xctest-macos-pass-filter",
        "framework": "xctest",
        "command": "swift test --filter LedgerTests",
        "exitCode": 0,
        "output": """\
Building for debugging...
[1/3] Write swift-version--58304C5D6DBC2206.txt
Build complete! (0.41s)
Test Suite 'Selected tests' started at 2026-10-27 14:10:02.117.
Test Suite 'OrbitPackageTests.xctest' started at 2026-10-27 14:10:02.118.
Test Suite 'LedgerTests' started at 2026-10-27 14:10:02.118.
Test Case '-[OrbitTests.LedgerTests testExportCSV]' started.
Test Case '-[OrbitTests.LedgerTests testExportCSV]' passed (0.002 seconds).
Test Case '-[OrbitTests.LedgerTests testPostBalances]' started.
Test Case '-[OrbitTests.LedgerTests testPostBalances]' passed (0.000 seconds).
Test Case '-[OrbitTests.LedgerTests testRejectsNegative]' started.
Test Case '-[OrbitTests.LedgerTests testRejectsNegative]' passed (0.000 seconds).
Test Case '-[OrbitTests.LedgerTests testRoundHalfEven]' started.
Test Case '-[OrbitTests.LedgerTests testRoundHalfEven]' passed (0.001 seconds).
Test Suite 'LedgerTests' passed at 2026-10-27 14:10:02.122.
\t Executed 4 tests, with 0 failures (0 unexpected) in 0.003 (0.004) seconds
Test Suite 'OrbitPackageTests.xctest' passed at 2026-10-27 14:10:02.122.
\t Executed 4 tests, with 0 failures (0 unexpected) in 0.003 (0.004) seconds
Test Suite 'Selected tests' passed at 2026-10-27 14:10:02.122.
\t Executed 4 tests, with 0 failures (0 unexpected) in 0.003 (0.005) seconds
◇ Test run started.
↳ Testing Library Version: 6.2 (3fdabe5392108d8)
↳ Target Platform: arm64e-apple-macos14.0
✔ Test run with 0 tests in 0 suites passed after 0.001 seconds.
""",
        "expect": {
            "failedCount": 0,
            "passedCount": 4,
            "failingTests": [],
            "errors": [],
        },
        "note": "--filter renames the root suite to 'Selected tests'.",
    },
    {
        "id": "xctest-linux-fail",
        "framework": "xctest",
        "command": "swift test",
        "exitCode": 1,
        "output": """\
[1/1] Planning build
Building for debugging...
[3/7] Compiling OrbitTests QueueTests.swift
[4/7] Emitting module OrbitTests
[5/7] Wrapping AST for OrbitTests for debugging
[6/7] Linking OrbitPackageTests.xctest
Build complete! (6.27s)
Test Suite 'All tests' started at 2026-10-27 06:21:44.902
Test Suite 'OrbitPackageTests.xctest' started at 2026-10-27 06:21:44.903
Test Suite 'QueueTests' started at 2026-10-27 06:21:44.903
Test Case 'QueueTests.testPopReturnsFIFO' started at 2026-10-27 06:21:44.903
Test Case 'QueueTests.testPopReturnsFIFO' passed (0.0 seconds)
Test Case 'QueueTests.testPush' started at 2026-10-27 06:21:44.903
/home/dev/orbit/Tests/OrbitTests/QueueTests.swift:12: error: QueueTests.testPush : XCTAssertEqual failed: ("2") is not equal to ("3") - count after three pushes
Test Case 'QueueTests.testPush' failed (0.001 seconds)
Test Case 'QueueTests.testPeekEmpty' started at 2026-10-27 06:21:44.904
Test Case 'QueueTests.testPeekEmpty' passed (0.0 seconds)
Test Suite 'QueueTests' failed at 2026-10-27 06:21:44.904
\t Executed 3 tests, with 1 failure (0 unexpected) in 0.001 (0.001) seconds
Test Suite 'OrbitPackageTests.xctest' failed at 2026-10-27 06:21:44.904
\t Executed 3 tests, with 1 failure (0 unexpected) in 0.001 (0.001) seconds
Test Suite 'All tests' failed at 2026-10-27 06:21:44.904
\t Executed 3 tests, with 1 failure (0 unexpected) in 0.001 (0.001) seconds
◇ Test run started.
↳ Testing Library Version: 6.2 (3fdabe5392108d8)
↳ Target Platform: aarch64-unknown-linux-gnu
✔ Test run with 0 tests in 0 suites passed after 0.001 seconds.
""",
        "expect": {
            "failedCount": 1,
            "passedCount": 2,
            "failingTests": ["QueueTests.testPush"],
            "errors": [],
        },
        "note": "swift-corelibs-xctest format: 'Class.method' ids without '-[...]', timestamps on 'started at' lines, no trailing period, durations like '0.0'.",
    },
]

# ---------------------------------------------------------------------------
# swift-testing
# ---------------------------------------------------------------------------

_PART_A += [
    {
        "id": "swift-testing-fail",
        "framework": "swift-testing",
        "command": "swift test",
        "exitCode": 1,
        "output": """\
Building for debugging...
[1/5] Write sources
[3/5] Compiling OrbitTests QueueTests.swift
[4/5] Emitting module OrbitTests
[5/5] Linking OrbitPackageTests
Build complete! (1.96s)
Test Suite 'All tests' started at 2026-10-27 14:20:31.004.
Test Suite 'All tests' passed at 2026-10-27 14:20:31.005.
\t Executed 0 tests, with 0 failures (0 unexpected) in 0.000 (0.001) seconds
◇ Test run started.
↳ Testing Library Version: 6.2 (3fdabe5392108d8)
↳ Target Platform: arm64e-apple-macos14.0
◇ Suite QueueTests started.
◇ Suite LedgerTests started.
◇ Test push() started.
◇ Test popReturnsFIFO() started.
◇ Test peekEmpty() started.
◇ Test postBalances() started.
◇ Test rejectsNegative() started.
✔ Test popReturnsFIFO() passed after 0.001 seconds.
✔ Test peekEmpty() passed after 0.001 seconds.
✘ Test push() recorded an issue at QueueTests.swift:12:5: Expectation failed: (queue.count → 2) == 3
✘ Test push() failed after 0.001 seconds with 1 issue.
✔ Test postBalances() passed after 0.002 seconds.
✘ Suite QueueTests failed after 0.002 seconds with 1 issue.
✔ Test rejectsNegative() passed after 0.002 seconds.
✔ Suite LedgerTests passed after 0.003 seconds.
✘ Test run with 5 tests in 2 suites failed after 0.010 seconds with 1 issue.
""",
        "expect": {
            "failedCount": 1,
            "passedCount": 4,
            "failingTests": ["push()"],
            "errors": [],
        },
        "note": "Empty XCTest run prints 'passed' first; the swift-testing summary counts issues, not failed tests (equal here). Issue location is a bare file name, not a path.",
    },
    {
        "id": "swift-testing-pass-parameterized",
        "framework": "swift-testing",
        "command": "swift test --disable-xctest",
        "exitCode": 0,
        "output": """\
Building for debugging...
[1/1] Write swift-version--58304C5D6DBC2206.txt
Build complete! (0.38s)
◇ Test run started.
↳ Testing Library Version: 6.2 (3fdabe5392108d8)
↳ Target Platform: arm64e-apple-macos14.0
◇ Suite ParserTests started.
◇ Test "Parses empty input" started.
◇ Test parsesNested() started.
◇ Test rejectsTrailingComma() started.
◇ Test parseNumber(_:) started.
◇ Test "Keeps comments" started.
◇ Test tokenCount() started.
✔ Test "Parses empty input" passed after 0.001 seconds.
✔ Test rejectsTrailingComma() passed after 0.001 seconds.
✔ Test parsesNested() passed after 0.002 seconds.
✔ Test tokenCount() passed after 0.002 seconds.
✔ Test "Keeps comments" passed after 0.002 seconds.
✔ Test parseNumber(_:) with 4 test cases passed after 0.003 seconds.
✔ Suite ParserTests passed after 0.003 seconds.
✔ Test run with 6 tests in 1 suite passed after 0.004 seconds.
""",
        "expect": {
            "failedCount": 0,
            "passedCount": 6,
            "failingTests": [],
            "errors": [],
        },
        "note": "Display-named tests print quoted names; a parameterized test counts as one test ('with 4 test cases' wording assumed).",
    },
    {
        "id": "swift-testing-mixed-xctest",
        "framework": "swift-testing",
        "command": "swift test",
        "exitCode": 1,
        "output": """\
Building for debugging...
[1/6] Write sources
[3/8] Compiling OrbitTests LegacyLedgerTests.swift
[4/8] Compiling OrbitTests QueueTests.swift
[5/8] Emitting module OrbitTests
[7/8] Linking OrbitPackageTests
Build complete! (2.31s)
Test Suite 'All tests' started at 2026-10-27 15:02:44.118.
Test Suite 'OrbitPackageTests.xctest' started at 2026-10-27 15:02:44.119.
Test Suite 'LegacyLedgerTests' started at 2026-10-27 15:02:44.119.
Test Case '-[OrbitTests.LegacyLedgerTests testExportCSV]' started.
Test Case '-[OrbitTests.LegacyLedgerTests testExportCSV]' passed (0.002 seconds).
Test Case '-[OrbitTests.LegacyLedgerTests testImportCSV]' started.
/Users/dev/work/orbit/Tests/OrbitTests/LegacyLedgerTests.swift:44: error: -[OrbitTests.LegacyLedgerTests testImportCSV] : XCTAssertEqual failed: ("11") is not equal to ("12") - row count
Test Case '-[OrbitTests.LegacyLedgerTests testImportCSV]' failed (0.004 seconds).
Test Suite 'LegacyLedgerTests' failed at 2026-10-27 15:02:44.125.
\t Executed 2 tests, with 1 failure (0 unexpected) in 0.006 (0.006) seconds
Test Suite 'OrbitPackageTests.xctest' failed at 2026-10-27 15:02:44.125.
\t Executed 2 tests, with 1 failure (0 unexpected) in 0.006 (0.007) seconds
Test Suite 'All tests' failed at 2026-10-27 15:02:44.125.
\t Executed 2 tests, with 1 failure (0 unexpected) in 0.006 (0.007) seconds
◇ Test run started.
↳ Testing Library Version: 6.2 (3fdabe5392108d8)
↳ Target Platform: arm64e-apple-macos14.0
◇ Suite QueueTests started.
◇ Test push() started.
◇ Test "Drains in FIFO order" started.
◇ Test peek() started.
✔ Test peek() passed after 0.001 seconds.
✘ Test "Drains in FIFO order" recorded an issue at QueueTests.swift:31:9: Expectation failed: (drained → [3, 1, 2]) == [1, 2, 3]
✘ Test "Drains in FIFO order" failed after 0.002 seconds with 1 issue.
✔ Test push() passed after 0.002 seconds.
✘ Suite QueueTests failed after 0.003 seconds with 1 issue.
✘ Test run with 3 tests in 1 suite failed after 0.003 seconds with 1 issue.
""",
        "expect": {
            "failedCount": 2,
            "passedCount": 3,
            "failingTests": [
                "-[OrbitTests.LegacyLedgerTests testImportCSV]",
                "\"Drains in FIFO order\"",
            ],
            "errors": [],
        },
        "note": "Two runners in one command: totals are XCTest (1 failed / 1 passed) plus swift-testing (1 failed / 2 passed). The display-named test id keeps its quotes as printed.",
    },
]

# ---------------------------------------------------------------------------
# swiftc (via SwiftPM)
# ---------------------------------------------------------------------------

_PART_A += [
    {
        "id": "swiftc-build-swift6-duplicated",
        "framework": "swiftc",
        "command": "swift build",
        "exitCode": 1,
        "output": """\
Building for debugging...
[0/3] Write sources
[1/3] Write swift-version--58304C5D6DBC2206.txt
error: emit-module command failed with exit code 1 (use -v to see invocation)
[3/5] Emitting module Orbit
/Users/dev/work/orbit/Sources/Orbit/Queue.swift:10:22: error: cannot find type 'Elemnt' in scope
 8 |     private var storage: [Element] = []
 9 |     public private(set) var count = 0
10 |     public var last: Elemnt? { storage.last }
   |                      `- error: cannot find type 'Elemnt' in scope
11 |
12 |     public mutating func push(_ element: Element) {

[4/5] Compiling Orbit Queue.swift
/Users/dev/work/orbit/Sources/Orbit/Queue.swift:10:22: error: cannot find type 'Elemnt' in scope
 8 |     private var storage: [Element] = []
 9 |     public private(set) var count = 0
10 |     public var last: Elemnt? { storage.last }
   |                      `- error: cannot find type 'Elemnt' in scope
11 |
12 |     public mutating func push(_ element: Element) {

/Users/dev/work/orbit/Sources/Orbit/Queue.swift:14:9: error: cannot find 'x' in scope
12 |     public mutating func push(_ element: Element) {
13 |         storage.append(element)
14 |         x += 1
   |         `- error: cannot find 'x' in scope
15 |     }
16 |

""",
        "expect": {
            "failedCount": None,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "/Users/dev/work/orbit/Sources/Orbit/Queue.swift", "code": None},
                {"file": "/Users/dev/work/orbit/Sources/Orbit/Queue.swift", "code": None},
            ],
        },
        "note": "Swift 6 snippet style ('`- error:'); the declaration error is printed by both the emit-module and compile jobs and must be de-duplicated (2 distinct errors). The 'emit-module command failed' line is not a diagnostic. No error-count summary exists.",
    },
    {
        "id": "swiftc-classic-warnings-errors",
        "framework": "swiftc",
        "command": "swift build",
        "exitCode": 1,
        "output": """\
Building for debugging...
[0/4] Write swift-version-24593BA9C3E375BF.txt
[2/7] Compiling Ledger Ledger.swift
/home/dev/orbit/Sources/Ledger/Ledger.swift:7:9: warning: variable 'total' was never mutated; consider changing to 'let' constant
    var total = entries.reduce(0, +)
    ~~~ ^
    let
/home/dev/orbit/Sources/Ledger/Ledger.swift:19:22: error: value of type 'Entry' has no member 'ammount'
        return entry.ammount > 0
               ~~~~~ ^~~~~~~
/home/dev/orbit/Sources/Ledger/Ledger.swift:31:13: warning: initialization of immutable value 'unused' was never used; consider replacing with assignment to '_' or removing it
        let unused = formatter.string(from: date)
        ~~~~^~~~~~
        _
[3/7] Compiling Ledger Account.swift
/home/dev/orbit/Sources/Ledger/Account.swift:12:38: error: missing argument for parameter 'currency' in call
        let opening = Money(amount: 0)
                                     ^
                                     , currency: <#Currency#>
/home/dev/orbit/Sources/Ledger/Money.swift:4:12: note: 'init(amount:currency:)' declared here
    public init(amount: Decimal, currency: Currency) {
           ^
""",
        "expect": {
            "failedCount": None,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "/home/dev/orbit/Sources/Ledger/Ledger.swift", "code": None},
                {"file": "/home/dev/orbit/Sources/Ledger/Account.swift", "code": None},
            ],
        },
        "note": "Swift 5.10 caret/fix-it style; warnings and the trailing 'note:' (in another file) are not errors; fix-it lines ('let', '_', ', currency:') look like source.",
    },
    {
        "id": "swiftc-swift-test-compile-fail",
        "framework": "swiftc",
        "command": "swift test",
        "exitCode": 1,
        "output": """\
Building for debugging...
[0/4] Write sources
[1/4] Write swift-version--58304C5D6DBC2206.txt
[3/7] Compiling OrbitTests QueueTests.swift
/Users/dev/work/orbit/Tests/OrbitTests/QueueTests.swift:9:11: error: value of type 'Queue<Int>' has no member 'pushAll'
 7 |     func testPushAll() {
 8 |         var q = Queue<Int>()
 9 |         q.pushAll([1, 2, 3])
   |           `- error: value of type 'Queue<Int>' has no member 'pushAll'
10 |         XCTAssertEqual(q.count, 3)
11 |     }

/Users/dev/work/orbit/Tests/OrbitTests/QueueTests.swift:15:34: error: cannot convert value of type 'String' to expected argument type 'Int'
13 |     func testPeek() {
14 |         let q = Queue<Int>([1])
15 |         XCTAssertEqual(q.peek(), "1")
   |                                  `- error: cannot convert value of type 'String' to expected argument type 'Int'
16 |     }
17 | }

""",
        "expect": {
            "failedCount": None,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "/Users/dev/work/orbit/Tests/OrbitTests/QueueTests.swift", "code": None},
                {"file": "/Users/dev/work/orbit/Tests/OrbitTests/QueueTests.swift", "code": None},
            ],
        },
        "note": "swift test stops at the build step: no XCTest or swift-testing output at all.",
    },
]

# ---------------------------------------------------------------------------
# xcodebuild
# ---------------------------------------------------------------------------

_PART_A += [
    {
        "id": "xcodebuild-test-compile-error-only-testing",
        "framework": "xcodebuild",
        "command": "xcodebuild test -scheme Orbit -destination 'platform=macOS' -only-testing:OrbitTests/QueueTests",
        "exitCode": 65,
        "output": """\
Command line invocation:
    /Applications/Xcode.app/Contents/Developer/usr/bin/xcodebuild test -scheme Orbit -destination platform=macOS -only-testing:OrbitTests/QueueTests

Resolve Package Graph


Resolved source packages:
  swift-collections: https://github.com/apple/swift-collections.git @ 1.1.4

ComputePackagePrebuildTargetDependencyGraph

Prepare packages

CreateBuildRequest

SendProjectDescription

CreateBuildOperation

ComputeTargetDependencyGraph
note: Building targets in dependency order
note: Target dependency graph (3 targets)
    Target 'OrbitTests' in project 'Orbit'
        ➜ Explicit dependency on target 'Orbit' in project 'Orbit'
    Target 'Orbit' in project 'Orbit'
        ➜ Explicit dependency on target 'Collections' in project 'swift-collections'
    Target 'Collections' in project 'swift-collections' (no dependencies)

GatherProvisioningInputs

CreateBuildDescription

SwiftDriver OrbitTests normal arm64 com.apple.xcode.tools.swift.compiler (in target 'OrbitTests' from project 'Orbit')
    cd /Users/dev/work/orbit
    builtin-SwiftDriver -- /Applications/Xcode.app/Contents/Developer/Toolchains/XcodeDefault.xctoolchain/usr/bin/swiftc -module-name OrbitTests -Onone -enforce-exclusivity\\=checked @/Users/dev/Library/Developer/Xcode/DerivedData/Orbit-bqfhzkxjdlwmnoaexgtyrcpvuism/Build/Intermediates.noindex/Orbit.build/Debug/OrbitTests.build/Objects-normal/arm64/OrbitTests.SwiftFileList -enable-testing -g -target arm64-apple-macos14.0 -sdk /Applications/Xcode.app/Contents/Developer/Platforms/MacOSX.platform/Developer/SDKs/MacOSX15.5.sdk

SwiftCompile normal arm64 Compiling\\ QueueTests.swift /Users/dev/work/orbit/OrbitTests/QueueTests.swift (in target 'OrbitTests' from project 'Orbit')
    cd /Users/dev/work/orbit
    builtin-swiftTaskExecution -- /Applications/Xcode.app/Contents/Developer/Toolchains/XcodeDefault.xctoolchain/usr/bin/swift-frontend -frontend -c -primary-file /Users/dev/work/orbit/OrbitTests/QueueTests.swift -emit-dependencies-path /Users/dev/Library/Developer/Xcode/DerivedData/Orbit-bqfhzkxjdlwmnoaexgtyrcpvuism/Build/Intermediates.noindex/Orbit.build/Debug/OrbitTests.build/Objects-normal/arm64/QueueTests.d -target arm64-apple-macos14.0 -module-name OrbitTests -o /Users/dev/Library/Developer/Xcode/DerivedData/Orbit-bqfhzkxjdlwmnoaexgtyrcpvuism/Build/Intermediates.noindex/Orbit.build/Debug/OrbitTests.build/Objects-normal/arm64/QueueTests.o
/Users/dev/work/orbit/OrbitTests/QueueTests.swift:9:11: error: value of type 'Queue<Int>' has no member 'pushAll'
        q.pushAll([1, 2, 3])
        ~ ^~~~~~~

Testing failed:
\tValue of type 'Queue<Int>' has no member 'pushAll'
\tTesting cancelled because the build failed.

** TEST FAILED **


The following build commands failed:
\tSwiftCompile normal arm64 Compiling\\ QueueTests.swift /Users/dev/work/orbit/OrbitTests/QueueTests.swift (in target 'OrbitTests' from project 'Orbit')
\tTesting project Orbit with scheme Orbit
(2 failures)
""",
        "expect": {
            "failedCount": 2,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "/Users/dev/work/orbit/OrbitTests/QueueTests.swift", "code": None},
            ],
        },
        "note": "Build fails inside the test action so no test runs; the error is repeated (capitalised, without location) under 'Testing failed:'. '(2 failures)' counts failed build commands, including the synthetic 'Testing project ...' entry.",
    },
    {
        "id": "xcodebuild-test-pass-tail",
        "framework": "xcodebuild",
        "command": "xcodebuild test -scheme Orbit -destination 'platform=macOS' 2>&1 | tail -n 20",
        "exitCode": 0,
        "output": """\
Test Case '-[OrbitTests.QueueTests testPeekEmpty]' passed (0.001 seconds).
Test Case '-[OrbitTests.QueueTests testPopReturnsFIFO]' started.
Test Case '-[OrbitTests.QueueTests testPopReturnsFIFO]' passed (0.000 seconds).
Test Case '-[OrbitTests.QueueTests testPush]' started.
Test Case '-[OrbitTests.QueueTests testPush]' passed (0.001 seconds).
Test Suite 'QueueTests' passed at 2026-10-27 16:52:40.206.
\t Executed 4 tests, with 0 failures (0 unexpected) in 0.003 (0.004) seconds
Test Suite 'OrbitTests.xctest' passed at 2026-10-27 16:52:40.207.
\t Executed 9 tests, with 0 failures (0 unexpected) in 0.012 (0.018) seconds
Test Suite 'All tests' passed at 2026-10-27 16:52:40.207.
\t Executed 9 tests, with 0 failures (0 unexpected) in 0.012 (0.019) seconds
2026-10-27 16:52:40.388 xcodebuild[51734:1302215] [MT] IDETestOperationsObserverDebug: 2.901 elapsed -- Testing started completed.
2026-10-27 16:52:40.388 xcodebuild[51734:1302215] [MT] IDETestOperationsObserverDebug: 0.000 sec, +0.000 sec -- start
2026-10-27 16:52:40.388 xcodebuild[51734:1302215] [MT] IDETestOperationsObserverDebug: 2.901 sec, +2.901 sec -- end

Test session results, code coverage, and logs:
\t/Users/dev/Library/Developer/Xcode/DerivedData/Orbit-bqfhzkxjdlwmnoaexgtyrcpvuism/Logs/Test/Test-Orbit-2026.10.27_16-52-35-+0800.xcresult

** TEST SUCCEEDED **

""",
        "expect": {
            "failedCount": 0,
            "passedCount": 9,
            "failingTests": [],
            "errors": [],
        },
        "note": "Head cut off by tail: output starts mid-suite, so the per-test 'passed' lines are incomplete; only the outermost 'Executed 9 tests' line gives the total.",
    },
    {
        "id": "xcodebuild-build-failed",
        "framework": "xcodebuild",
        "command": "xcodebuild build -scheme Orbit -destination 'platform=macOS'",
        "exitCode": 65,
        "output": """\
Command line invocation:
    /Applications/Xcode.app/Contents/Developer/usr/bin/xcodebuild build -scheme Orbit -destination platform=macOS

Prepare packages

ComputeTargetDependencyGraph
note: Building targets in dependency order
note: Target dependency graph (1 target)
    Target 'Orbit' in project 'Orbit' (no dependencies)

GatherProvisioningInputs

CreateBuildDescription

ProcessInfoPlistFile /Users/dev/Library/Developer/Xcode/DerivedData/Orbit-bqfhzkxjdlwmnoaexgtyrcpvuism/Build/Products/Debug/Orbit.app/Contents/Info.plist /Users/dev/work/orbit/Orbit/Info.plist (in target 'Orbit' from project 'Orbit')
    cd /Users/dev/work/orbit
    builtin-infoPlistUtility /Users/dev/work/orbit/Orbit/Info.plist -producttype com.apple.product-type.application -genpkginfo /Users/dev/Library/Developer/Xcode/DerivedData/Orbit-bqfhzkxjdlwmnoaexgtyrcpvuism/Build/Products/Debug/Orbit.app/Contents/PkgInfo -expandbuildsettings -platform macosx -o /Users/dev/Library/Developer/Xcode/DerivedData/Orbit-bqfhzkxjdlwmnoaexgtyrcpvuism/Build/Products/Debug/Orbit.app/Contents/Info.plist

SwiftCompile normal arm64 Compiling\\ Queue.swift /Users/dev/work/orbit/Orbit/Queue.swift (in target 'Orbit' from project 'Orbit')
    cd /Users/dev/work/orbit
    builtin-swiftTaskExecution -- /Applications/Xcode.app/Contents/Developer/Toolchains/XcodeDefault.xctoolchain/usr/bin/swift-frontend -frontend -c -primary-file /Users/dev/work/orbit/Orbit/Queue.swift /Users/dev/work/orbit/Orbit/Ledger.swift /Users/dev/work/orbit/Orbit/OrbitApp.swift -target arm64-apple-macos14.0 -module-name Orbit -o /Users/dev/Library/Developer/Xcode/DerivedData/Orbit-bqfhzkxjdlwmnoaexgtyrcpvuism/Build/Intermediates.noindex/Orbit.build/Debug/Orbit.build/Objects-normal/arm64/Queue.o
/Users/dev/work/orbit/Orbit/Queue.swift:14:9: error: cannot find 'x' in scope
        x += 1
        ^
/Users/dev/work/orbit/Orbit/Queue.swift:22:16: error: cannot convert return expression of type 'Int' to return type 'Element?'
        return storage.startIndex
               ^~~~~~~~~~~~~~~~~~
/Users/dev/work/orbit/Orbit/Queue.swift:5:9: warning: variable 'tmp' was never used; consider replacing with '_' or removing it
    var tmp = 0
        ^~~
        _

SwiftCompile normal arm64 /Users/dev/work/orbit/Orbit/Queue.swift (in target 'Orbit' from project 'Orbit')
    cd /Users/dev/work/orbit


** BUILD FAILED **


The following build commands failed:
\tSwiftCompile normal arm64 Compiling\\ Queue.swift /Users/dev/work/orbit/Orbit/Queue.swift (in target 'Orbit' from project 'Orbit')
\tSwiftCompile normal arm64 /Users/dev/work/orbit/Orbit/Queue.swift (in target 'Orbit' from project 'Orbit')
\tBuilding project Orbit with scheme Orbit
(3 failures)
""",
        "expect": {
            "failedCount": 3,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "/Users/dev/work/orbit/Orbit/Queue.swift", "code": None},
                {"file": "/Users/dev/work/orbit/Orbit/Queue.swift", "code": None},
            ],
        },
        "note": "Xcode 16-style failure list: one source file yields two SwiftCompile entries plus 'Building project', so '(3 failures)' != 2 errors. Older Xcode lists a single command '(1 failure)'.",
    },
    {
        "id": "xcodebuild-xcode16-parallel-fail-tail",
        "framework": "xcodebuild",
        "command": "xcodebuild test -scheme Orbit -destination 'platform=macOS' 2>&1 | tail -n 30",
        "exitCode": 0,
        "output": """\
RegisterWithLaunchServices /Users/dev/Library/Developer/Xcode/DerivedData/Orbit-bqfhzkxjdlwmnoaexgtyrcpvuism/Build/Products/Debug/Orbit.app (in target 'Orbit' from project 'Orbit')
    cd /Users/dev/work/orbit
    /System/Library/Frameworks/CoreServices.framework/Versions/Current/Frameworks/LaunchServices.framework/Versions/Current/Support/lsregister -f -R -trusted /Users/dev/Library/Developer/Xcode/DerivedData/Orbit-bqfhzkxjdlwmnoaexgtyrcpvuism/Build/Products/Debug/Orbit.app

Testing started
Test suite 'LedgerTests' started on 'My Mac - Orbit (52231)'
Test suite 'QueueTests' started on 'My Mac - Orbit (52232)'
Test case 'LedgerTests.testExportCSV()' passed on 'My Mac - Orbit (52231)' (0.002 seconds)
Test case 'QueueTests.testPeekEmpty()' passed on 'My Mac - Orbit (52232)' (0.001 seconds)
Test case 'LedgerTests.testPostBalances()' passed on 'My Mac - Orbit (52231)' (0.000 seconds)
/Users/dev/work/orbit/OrbitTests/QueueTests.swift:12: error: -[OrbitTests.QueueTests testPush] : XCTAssertEqual failed: ("2") is not equal to ("3") - count after three pushes
Test case 'QueueTests.testPush()' failed on 'My Mac - Orbit (52232)' (0.002 seconds)
Test case 'LedgerTests.testRejectsNegative()' passed on 'My Mac - Orbit (52231)' (0.000 seconds)
/Users/dev/work/orbit/OrbitTests/QueueTests.swift:41: error: -[OrbitTests.QueueTests testDrainOrder] : XCTAssertEqual failed: ("[3, 1, 2]") is not equal to ("[1, 2, 3]")
Test case 'QueueTests.testDrainOrder()' failed on 'My Mac - Orbit (52232)' (0.001 seconds)
Test case 'QueueTests.testPopReturnsFIFO()' passed on 'My Mac - Orbit (52232)' (0.000 seconds)
2026-10-27 17:02:36.881 xcodebuild[52218:1311904] [MT] IDETestOperationsObserverDebug: 4.102 elapsed -- Testing started completed.
2026-10-27 17:02:36.881 xcodebuild[52218:1311904] [MT] IDETestOperationsObserverDebug: 0.000 sec, +0.000 sec -- start
2026-10-27 17:02:36.881 xcodebuild[52218:1311904] [MT] IDETestOperationsObserverDebug: 4.102 sec, +4.102 sec -- end

Test session results, code coverage, and logs:
\t/Users/dev/Library/Developer/Xcode/DerivedData/Orbit-bqfhzkxjdlwmnoaexgtyrcpvuism/Logs/Test/Test-Orbit-2026.10.27_17-02-31-+0800.xcresult

Failing tests:
\tOrbitTests:
\t\tQueueTests.testDrainOrder()
\t\tQueueTests.testPush()

** TEST FAILED **

""",
        "expect": {
            "failedCount": None,
            "passedCount": None,
            "failingTests": [
                "QueueTests.testDrainOrder()",
                "QueueTests.testPush()",
            ],
            "errors": [],
        },
        "note": "Exit code 0 comes from tail, not xcodebuild; head is missing. Parallel runner lines ('Test case ... failed on ...') interleave two worker processes and there is no 'Executed N tests' count.",
    },
]

# ---------------------------------------------------------------------------
# go
# ---------------------------------------------------------------------------

_PART_A += [
    {
        "id": "go-test-all-mixed",
        "framework": "go",
        "command": "go test ./...",
        "exitCode": 1,
        "output": """\
# example.com/orbit/sync [example.com/orbit/sync.test]
sync/client_test.go:22:9: undefined: newTestServer
sync/client_test.go:40:15: too many arguments in call to c.Dial
\thave (string, time.Duration)
\twant (string)
?   \texample.com/orbit/cmd/orbit\t[no test files]
ok  \texample.com/orbit/ledger\t0.012s
--- FAIL: TestParse (0.00s)
    --- FAIL: TestParse/empty (0.00s)
        parser_test.go:31: Parse("") error = <nil>, want ErrEmpty
    --- FAIL: TestParse/trailing_comma (0.00s)
        parser_test.go:31: Parse("[1,2,]") error = <nil>, want ErrSyntax
FAIL
FAIL\texample.com/orbit/parser\t0.004s
--- FAIL: TestPush (0.00s)
    queue_test.go:18: Len() = 2, want 3
FAIL
FAIL\texample.com/orbit/queue\t0.003s
FAIL\texample.com/orbit/sync [build failed]
FAIL
""",
        "expect": {
            "failedCount": None,
            "passedCount": None,
            "failingTests": [
                "TestParse",
                "TestParse/empty",
                "TestParse/trailing_comma",
                "TestPush",
            ],
            "errors": [
                {"file": "sync/client_test.go", "code": None},
                {"file": "sync/client_test.go", "code": None},
            ],
        },
        "note": "Build errors for one test package print first, out of package order; 'have/want' are tab-indented continuation lines. Parent tests fail because of subtests; both are listed. No pass/fail counts without -v/-json.",
    },
    {
        "id": "go-test-verbose-run",
        "framework": "go",
        "command": "go test -v -run TestParse ./parser",
        "exitCode": 1,
        "output": """\
=== RUN   TestParse
=== RUN   TestParse/empty
=== RUN   TestParse/number
=== RUN   TestParse/nested
=== RUN   TestParse/trailing_comma
    parser_test.go:31: Parse("[1,2,]") error = <nil>, want ErrSyntax
--- FAIL: TestParse (0.00s)
    --- PASS: TestParse/empty (0.00s)
    --- PASS: TestParse/number (0.00s)
    --- PASS: TestParse/nested (0.00s)
    --- FAIL: TestParse/trailing_comma (0.00s)
=== RUN   TestParseStream
=== PAUSE TestParseStream
=== CONT  TestParseStream
--- PASS: TestParseStream (0.01s)
FAIL
FAIL\texample.com/orbit/parser\t0.015s
FAIL
""",
        "expect": {
            "failedCount": None,
            "passedCount": None,
            "failingTests": ["TestParse", "TestParse/trailing_comma"],
            "errors": [],
        },
        "note": "-run is a regex so TestParseStream also runs; the failure log line precedes its '--- FAIL' line and subtest results are indented under the parent.",
    },
    {
        "id": "go-build-errors",
        "framework": "go",
        "command": "go build ./...",
        "exitCode": 1,
        "output": """\
# example.com/orbit/queue
queue/queue.go:12:5: undefined: foo
queue/queue.go:27:9: cannot use q.items[0] (variable of type int) as string value in return statement
# example.com/orbit/ledger
ledger/ledger.go:44:2: declared and not used: total
""",
        "expect": {
            "failedCount": None,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "queue/queue.go", "code": None},
                {"file": "queue/queue.go", "code": None},
                {"file": "ledger/ledger.go", "code": None},
            ],
        },
        "note": "Package header lines start with '#'; no summary or count.",
    },
    {
        "id": "go-vet-findings",
        "framework": "go",
        "command": "go vet ./...",
        "exitCode": 1,
        "output": """\
# example.com/orbit/ledger
# [example.com/orbit/ledger]
ledger/format.go:18:2: fmt.Sprintf format %d has arg amount of wrong type float64
ledger/ledger.go:61:3: unreachable code
# example.com/orbit/sync
# [example.com/orbit/sync]
sync/client.go:31:9: assignment copies lock value to c2: example.com/orbit/sync.Client contains sync.Mutex
""",
        "expect": {
            "failedCount": None,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "ledger/format.go", "code": None},
                {"file": "ledger/ledger.go", "code": None},
                {"file": "sync/client.go", "code": None},
            ],
        },
        "note": "vet prints a second '# [pkg]' header line; analyzer names (printf, unreachable, copylocks) are not printed, so code is None.",
    },
]

# ---------------------------------------------------------------------------
# cc (gcc / clang / ld / make)
# ---------------------------------------------------------------------------

_PART_A += [
    {
        "id": "cc-clang-two-units",
        "framework": "cc",
        "command": "clang -std=c17 -Wall -Werror -Iinclude -c src/main.c src/ledger.c",
        "exitCode": 1,
        "output": """\
src/main.c:12:5: error: call to undeclared function 'queue_drain'; ISO C99 and later do not support implicit function declarations [-Wimplicit-function-declaration]
   12 |     queue_drain(q, out);
      |     ^
src/main.c:18:9: error: unused variable 'total' [-Werror,-Wunused-variable]
   18 |     int total = 0;
      |         ^~~~~
2 errors generated.
src/ledger.c:3:10: fatal error: 'orbit/money.h' file not found
    3 | #include "orbit/money.h"
      |          ^~~~~~~~~~~~~~~
1 error generated.
""",
        "expect": {
            "failedCount": 3,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "src/main.c", "code": "-Wimplicit-function-declaration"},
                {"file": "src/main.c", "code": "-Wunused-variable"},
                {"file": "src/ledger.c", "code": None},
            ],
        },
        "note": "Two translation units give two 'N errors generated.' lines (2 + 1). code is the bracketed warning flag without the '-Werror,' prefix; a fatal error has no flag.",
    },
    {
        "id": "cc-gcc-make-utf8-quotes",
        "framework": "cc",
        "command": "make",
        "exitCode": 2,
        "output": """\
mkdir -p build
cc -std=c17 -Wall -Wextra -O2 -Iinclude -c src/queue.c -o build/queue.o
cc -std=c17 -Wall -Wextra -O2 -Iinclude -c src/main.c -o build/main.o
src/main.c: In function \u2018main\u2019:
src/main.c:12:5: error: implicit declaration of function \u2018queue_drain\u2019; did you mean \u2018queue_drop\u2019? [-Wimplicit-function-declaration]
   12 |     queue_drain(q, out);
      |     ^~~~~~~~~~~
      |     queue_drop
src/main.c:19:9: warning: unused variable \u2018total\u2019 [-Wunused-variable]
   19 |     int total = 0;
      |         ^~~~~
src/main.c:27:12: error: \u2018cnt\u2019 undeclared (first use in this function); did you mean \u2018cont\u2019?
   27 |     return cnt;
      |            ^~~
      |            cont
src/main.c:27:12: note: each undeclared identifier is reported only once for each function it appears in
make: *** [Makefile:12: build/main.o] Error 1
""",
        "expect": {
            "failedCount": None,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "src/main.c", "code": "-Wimplicit-function-declaration"},
                {"file": "src/main.c", "code": None},
            ],
        },
        "note": "GCC 14 in a UTF-8 locale uses curly quotes; implicit declaration is an error by default. Warning and note lines are excluded; gcc prints no error count, and make's 'Error 1' is the recipe exit status.",
    },
    {
        "id": "cc-gnu-ld-undefined-reference",
        "framework": "cc",
        "command": "make",
        "exitCode": 2,
        "output": """\
cc -std=c17 -Wall -O2 -Iinclude -c src/main.c -o build/main.o
cc -std=c17 -Wall -O2 -Iinclude -c src/queue.c -o build/queue.o
cc -o build/orbit build/main.o build/queue.o -lm
/usr/bin/ld: build/main.o: in function `main':
main.c:(.text.startup+0x4c): undefined reference to `queue_drain'
/usr/bin/ld: main.c:(.text.startup+0x8a): undefined reference to `ledger_post'
collect2: error: ld returned 1 exit status
make: *** [Makefile:9: build/orbit] Error 1
""",
        "expect": {
            "failedCount": None,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "main.c", "code": None},
                {"file": "main.c", "code": None},
            ],
        },
        "note": "Linker errors: the location is 'main.c:(.text.startup+0x4c)' (section offset, no line); one entry per undefined symbol. 'collect2: error:' is a summary, not a separate error.",
    },
    {
        "id": "cc-macos-ld-undefined-symbols",
        "framework": "cc",
        "command": "clang -o build/orbit build/main.o build/queue.o",
        "exitCode": 1,
        "output": """\
Undefined symbols for architecture arm64:
  "_ledger_post", referenced from:
      _apply_entry in main.o
      _replay in queue.o
  "_queue_drain", referenced from:
      _main in main.o
ld: symbol(s) not found for architecture arm64
clang: error: linker command failed with exit code 1 (use -v to see invocation)
""",
        "expect": {
            "failedCount": None,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "main.o", "code": None},
                {"file": "main.o", "code": None},
            ],
        },
        "note": "Apple ld64 style: no source path at all; one entry per undefined symbol, file taken from the first 'referenced from' object. Symbols carry a leading underscore.",
    },
]

# ---------------------------------------------------------------------------
# ctest
# ---------------------------------------------------------------------------

_PART_A += [
    {
        "id": "ctest-pass",
        "framework": "ctest",
        "command": "ctest --test-dir build",
        "exitCode": 0,
        "output": """\
Internal ctest changing into directory: /home/dev/orbit/build
Test project /home/dev/orbit/build
    Start 1: queue_basic
1/4 Test #1: queue_basic ......................   Passed    0.01 sec
    Start 2: queue_wraparound
2/4 Test #2: queue_wraparound .................   Passed    0.01 sec
    Start 3: ledger_post
3/4 Test #3: ledger_post ......................   Passed    0.02 sec
    Start 4: parser_fuzz_smoke
4/4 Test #4: parser_fuzz_smoke ................   Passed    0.35 sec

100% tests passed, 0 tests failed out of 4

Total Test time (real) =   0.40 sec
""",
        "expect": {
            "failedCount": 0,
            "passedCount": 4,
            "failingTests": [],
            "errors": [],
        },
    },
    {
        "id": "ctest-parallel-fail-segfault",
        "framework": "ctest",
        "command": "ctest --test-dir build -j4",
        "exitCode": 8,
        "output": """\
Internal ctest changing into directory: /Users/dev/work/orbit/build
Test project /Users/dev/work/orbit/build
    Start 1: queue_basic
    Start 2: queue_wraparound
    Start 3: ledger_post
    Start 4: ledger_replay
1/5 Test #1: queue_basic ......................   Passed    0.01 sec
    Start 5: parser_fuzz_smoke
2/5 Test #3: ledger_post ......................   Passed    0.01 sec
3/5 Test #2: queue_wraparound .................***Failed    0.02 sec
4/5 Test #4: ledger_replay ....................***Exception: SegFault  0.12 sec
5/5 Test #5: parser_fuzz_smoke ................   Passed    0.33 sec

60% tests passed, 2 tests failed out of 5

Total Test time (real) =   0.36 sec

The following tests FAILED:
\t  2 - queue_wraparound (Failed)
\t  4 - ledger_replay (SEGFAULT)
Errors while running CTest
Output from these tests are in: /Users/dev/work/orbit/build/Testing/Temporary/LastTest.log
Use "--rerun-failed --output-on-failure" to re-run the failed cases verbosely.
""",
        "expect": {
            "failedCount": 2,
            "passedCount": 3,
            "failingTests": ["queue_wraparound", "ledger_replay"],
            "errors": [],
        },
        "note": "-j4 finishes tests out of order (progress index != test number); a crash shows as '***Exception: SegFault' and '(SEGFAULT)'.",
    },
    {
        "id": "ctest-output-on-failure-gtest",
        "framework": "ctest",
        "command": "ctest --test-dir build -R queue --output-on-failure",
        "exitCode": 8,
        "output": """\
Internal ctest changing into directory: /home/dev/orbit/build
Test project /home/dev/orbit/build
    Start 1: queue_basic
1/2 Test #1: queue_basic ......................   Passed    0.01 sec
    Start 2: queue_wraparound
2/2 Test #2: queue_wraparound .................***Failed    0.02 sec
Running main() from /home/dev/orbit/build/_deps/googletest-src/googletest/src/gtest_main.cc
[==========] Running 3 tests from 1 test suite.
[----------] Global test environment set-up.
[----------] 3 tests from QueueTest
[ RUN      ] QueueTest.PushPop
[       OK ] QueueTest.PushPop (0 ms)
[ RUN      ] QueueTest.WrapAround
/home/dev/orbit/tests/queue_test.cpp:41: Failure
Expected equality of these values:
  q.front()
    Which is: 7
  3

[  FAILED  ] QueueTest.WrapAround (0 ms)
[ RUN      ] QueueTest.Clear
[       OK ] QueueTest.Clear (0 ms)
[----------] 3 tests from QueueTest (0 ms total)

[----------] Global test environment tear-down
[==========] 3 tests from 1 test suite ran. (0 ms total)
[  PASSED  ] 2 tests.
[  FAILED  ] 1 test, listed below:
[  FAILED  ] QueueTest.WrapAround

 1 FAILED TEST


50% tests passed, 1 tests failed out of 2

Total Test time (real) =   0.03 sec

The following tests FAILED:
\t  2 - queue_wraparound (Failed)
Errors while running CTest
""",
        "expect": {
            "failedCount": 1,
            "passedCount": 1,
            "failingTests": ["queue_wraparound"],
            "errors": [],
        },
        "note": "Embedded googletest output has its own counts ('[  PASSED  ] 2 tests', '1 FAILED TEST') that must not override ctest's '1 tests failed out of 2'. failingTests lists ctest test names only.",
    },
]

# ---------------------------------------------------------------------------
# dotnet
# ---------------------------------------------------------------------------

_PART_A += [
    {
        "id": "dotnet-build-errors",
        "framework": "dotnet",
        "command": "dotnet build",
        "exitCode": 1,
        "output": """\
MSBuild version 17.11.9+a69bbaaf5 for .NET
  Determining projects to restore...
  All projects are up-to-date for restore.
  Orbit.Abstractions -> /Users/dev/work/orbit/src/Orbit.Abstractions/bin/Debug/net8.0/Orbit.Abstractions.dll
/Users/dev/work/orbit/src/Orbit/Queue.cs(12,17): error CS0103: The name 'x' does not exist in the current context [/Users/dev/work/orbit/src/Orbit/Orbit.csproj]
/Users/dev/work/orbit/src/Orbit/Ledger.cs(40,20): error CS0029: Cannot implicitly convert type 'decimal' to 'int' [/Users/dev/work/orbit/src/Orbit/Orbit.csproj]

Build FAILED.

/Users/dev/work/orbit/src/Orbit/Queue.cs(12,17): error CS0103: The name 'x' does not exist in the current context [/Users/dev/work/orbit/src/Orbit/Orbit.csproj]
/Users/dev/work/orbit/src/Orbit/Ledger.cs(40,20): error CS0029: Cannot implicitly convert type 'decimal' to 'int' [/Users/dev/work/orbit/src/Orbit/Orbit.csproj]
    0 Warning(s)
    2 Error(s)

Time Elapsed 00:00:01.84

Workload updates are available. Run `dotnet workload list` for more information.
""",
        "expect": {
            "failedCount": 2,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "/Users/dev/work/orbit/src/Orbit/Queue.cs", "code": "CS0103"},
                {"file": "/Users/dev/work/orbit/src/Orbit/Ledger.cs", "code": "CS0029"},
            ],
        },
        "note": "Classic console logger (stdout is not a TTY, so no terminal logger): every diagnostic is repeated after 'Build FAILED.'; the trailing [project] suffix is not part of the message.",
    },
    {
        "id": "dotnet-test-vstest-fail",
        "framework": "dotnet",
        "command": "dotnet test",
        "exitCode": 1,
        "output": """\
  Determining projects to restore...
  All projects are up-to-date for restore.
  Orbit -> /Users/dev/work/orbit/src/Orbit/bin/Debug/net8.0/Orbit.dll
  Orbit.Tests -> /Users/dev/work/orbit/tests/Orbit.Tests/bin/Debug/net8.0/Orbit.Tests.dll
Test run for /Users/dev/work/orbit/tests/Orbit.Tests/bin/Debug/net8.0/Orbit.Tests.dll (.NETCoreApp,Version=v8.0)
VSTest version 17.11.1 (arm64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.31]     Orbit.Tests.QueueTests.Push_Grows [FAIL]
  Failed Orbit.Tests.QueueTests.Push_Grows [12 ms]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: 3
Actual:   2
  Stack Trace:
     at Orbit.Tests.QueueTests.Push_Grows() in /Users/dev/work/orbit/tests/Orbit.Tests/QueueTests.cs:line 21
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     1, Passed:    11, Skipped:     0, Total:    12, Duration: 45 ms - Orbit.Tests.dll (net8.0)
""",
        "expect": {
            "failedCount": 1,
            "passedCount": 11,
            "failingTests": ["Orbit.Tests.QueueTests.Push_Grows"],
            "errors": [],
        },
        "note": "The failing test appears twice (xUnit '[FAIL]' line and VSTest '  Failed ... [12 ms]'); assertion text continues on unindented lines.",
    },
    {
        "id": "dotnet-test-pass-two-assemblies",
        "framework": "dotnet",
        "command": "dotnet test Orbit.sln",
        "exitCode": 0,
        "output": """\
  Determining projects to restore...
  All projects are up-to-date for restore.
  Orbit -> /home/dev/orbit/src/Orbit/bin/Debug/net8.0/Orbit.dll
  Orbit.Tests -> /home/dev/orbit/tests/Orbit.Tests/bin/Debug/net8.0/Orbit.Tests.dll
  Orbit.Integration.Tests -> /home/dev/orbit/tests/Orbit.Integration.Tests/bin/Debug/net8.0/Orbit.Integration.Tests.dll
Test run for /home/dev/orbit/tests/Orbit.Tests/bin/Debug/net8.0/Orbit.Tests.dll (.NETCoreApp,Version=v8.0)
Test run for /home/dev/orbit/tests/Orbit.Integration.Tests/bin/Debug/net8.0/Orbit.Integration.Tests.dll (.NETCoreApp,Version=v8.0)
VSTest version 17.11.1 (x64)

VSTest version 17.11.1 (x64)

Starting test execution, please wait...
Starting test execution, please wait...
A total of 1 test files matched the specified pattern.
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    12, Skipped:     0, Total:    12, Duration: 38 ms - Orbit.Tests.dll (net8.0)

Passed!  - Failed:     0, Passed:     5, Skipped:     1, Total:     6, Duration: 1 s - Orbit.Integration.Tests.dll (net8.0)
""",
        "expect": {
            "failedCount": 0,
            "passedCount": 17,
            "failingTests": [],
            "errors": [],
        },
        "note": "Two test projects run in parallel, so their header lines interleave; two 'Passed!' summaries must be summed (12 + 5, one skipped).",
    },
    {
        "id": "dotnet-build-windows-crlf-multitarget",
        "framework": "dotnet",
        "command": "dotnet build",
        "exitCode": 1,
        "output": r"""  Determining projects to restore...
  All projects are up-to-date for restore.
  Orbit.Abstractions -> C:\Users\dev\src\orbit\src\Orbit.Abstractions\bin\Debug\netstandard2.0\Orbit.Abstractions.dll
C:\Users\dev\src\orbit\src\Orbit\Ledger.cs(18,17): warning CS0168: The variable 'ex' is declared but never used [C:\Users\dev\src\orbit\src\Orbit\Orbit.csproj::TargetFramework=net8.0]
C:\Users\dev\src\orbit\src\Orbit\Ledger.cs(27,22): error CS1061: 'Entry' does not contain a definition for 'Ammount' and no accessible extension method 'Ammount' accepting a first argument of type 'Entry' could be found (are you missing a using directive or an assembly reference?) [C:\Users\dev\src\orbit\src\Orbit\Orbit.csproj::TargetFramework=net8.0]
C:\Users\dev\src\orbit\src\Orbit\Ledger.cs(18,17): warning CS0168: The variable 'ex' is declared but never used [C:\Users\dev\src\orbit\src\Orbit\Orbit.csproj::TargetFramework=netstandard2.0]
C:\Users\dev\src\orbit\src\Orbit\Ledger.cs(27,22): error CS1061: 'Entry' does not contain a definition for 'Ammount' and no accessible extension method 'Ammount' accepting a first argument of type 'Entry' could be found (are you missing a using directive or an assembly reference?) [C:\Users\dev\src\orbit\src\Orbit\Orbit.csproj::TargetFramework=netstandard2.0]

Build FAILED.

C:\Users\dev\src\orbit\src\Orbit\Ledger.cs(18,17): warning CS0168: The variable 'ex' is declared but never used [C:\Users\dev\src\orbit\src\Orbit\Orbit.csproj::TargetFramework=net8.0]
C:\Users\dev\src\orbit\src\Orbit\Ledger.cs(27,22): error CS1061: 'Entry' does not contain a definition for 'Ammount' and no accessible extension method 'Ammount' accepting a first argument of type 'Entry' could be found (are you missing a using directive or an assembly reference?) [C:\Users\dev\src\orbit\src\Orbit\Orbit.csproj::TargetFramework=net8.0]
C:\Users\dev\src\orbit\src\Orbit\Ledger.cs(18,17): warning CS0168: The variable 'ex' is declared but never used [C:\Users\dev\src\orbit\src\Orbit\Orbit.csproj::TargetFramework=netstandard2.0]
C:\Users\dev\src\orbit\src\Orbit\Ledger.cs(27,22): error CS1061: 'Entry' does not contain a definition for 'Ammount' and no accessible extension method 'Ammount' accepting a first argument of type 'Entry' could be found (are you missing a using directive or an assembly reference?) [C:\Users\dev\src\orbit\src\Orbit\Orbit.csproj::TargetFramework=netstandard2.0]
    2 Warning(s)
    2 Error(s)

Time Elapsed 00:00:02.37
""".replace("\n", "\r\n"),
        "expect": {
            "failedCount": 2,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "C:\\Users\\dev\\src\\orbit\\src\\Orbit\\Ledger.cs", "code": "CS1061"},
            ],
        },
        "note": "Windows paths (drive letter colon before the '(line,col)' suffix) and CRLF endings. Multi-targeting reports the same error once per TargetFramework, so '2 Error(s)' covers 1 distinct error.",
    },
]


# ---- part B: JavaScript, TypeScript, Python checkers, JVM, Ruby, PHP, generic



def _vbar(title, cols=30):
    # Vitest divider: non-TTY stdout has no columns, Vitest falls back to 30.
    text = f" {title} "
    left = max((cols - len(text)) // 2, 0)
    right = max(cols - len(text) - left, 0)
    return "⎯" * left + text + "⎯" * right


def _vend(i, n, cols=30):
    text = f"[{i}/{n}]"
    return "⎯" * (cols - len(text) - 1) + text + "⎯"


def _lines(*rows):
    return "\n".join(rows) + "\n"


_PART_B = []

# ---------------------------------------------------------------- jest ----

_JEST_PASS = "\x1b[0m\x1b[7m\x1b[1m\x1b[32m PASS \x1b[39m\x1b[22m\x1b[27m\x1b[0m"
_JEST_FAIL = "\x1b[0m\x1b[7m\x1b[1m\x1b[31m FAIL \x1b[39m\x1b[22m\x1b[27m\x1b[0m"


def _jpath(d, f):
    return f"\x1b[2m{d}\x1b[22m\x1b[1m{f}\x1b[22m"


_JEST_COLOR_BLOCK = [
    "\x1b[1m\x1b[31m  \x1b[1m● \x1b[22m\x1b[1mQueue › push › grows the queue\x1b[39m\x1b[22m",
    "",
    "    \x1b[2mexpect(\x1b[22m\x1b[31mreceived\x1b[39m\x1b[2m).\x1b[22mtoBe\x1b[2m(\x1b[22m\x1b[32mexpected\x1b[39m\x1b[2m) // Object.is equality\x1b[22m",
    "",
    "    Expected: \x1b[32m2\x1b[39m",
    "    Received: \x1b[31m1\x1b[39m",
    "",
    "    \x1b[0m \x1b[90m 10 |\x1b[39m     \x1b[36mconst\x1b[39m q \x1b[33m=\x1b[39m \x1b[36mnew\x1b[39m \x1b[33mQueue\x1b[39m\x1b[33m<\x1b[39m\x1b[33mnumber\x1b[39m\x1b[33m>\x1b[39m()\x1b[33m;\x1b[39m",
    "     \x1b[90m 11 |\x1b[39m     q\x1b[33m.\x1b[39mpush(\x1b[35m1\x1b[39m)\x1b[33m;\x1b[39m",
    "    \x1b[31m\x1b[1m>\x1b[22m\x1b[39m\x1b[90m 12 |\x1b[39m     expect(q\x1b[33m.\x1b[39msize)\x1b[33m.\x1b[39mtoBe(\x1b[35m2\x1b[39m)\x1b[33m;\x1b[39m",
    "     \x1b[90m    |\x1b[39m                    \x1b[31m\x1b[1m^\x1b[22m\x1b[39m",
    "     \x1b[90m 13 |\x1b[39m   })\x1b[33m;\x1b[39m",
    "     \x1b[90m 14 |\x1b[39m",
    "     \x1b[90m 15 |\x1b[39m   it(\x1b[32m'keeps insertion order'\x1b[39m\x1b[33m,\x1b[39m () \x1b[33m=>\x1b[39m {\x1b[0m",
    "",
    "\x1b[2m      at Object.<anonymous> (\x1b[22m\x1b[2m\x1b[0m\x1b[36msrc/queue.test.ts\x1b[39m\x1b[0m\x1b[2m:12:20)\x1b[22m",
    "",
]

_PART_B += [
    {
        "id": "jest-pass-three-suites",
        "framework": "jest",
        "command": "npx jest",
        "exitCode": 0,
        "output": """\
 PASS  src/parser/lexer.test.ts
 PASS  src/queue.test.ts
 PASS  src/ledger/post.test.ts

Test Suites: 3 passed, 3 total
Tests:       12 passed, 12 total
Snapshots:   0 total
Time:        1.482 s, estimated 2 s
Ran all test suites.
""",
        "expect": {"failedCount": 0, "passedCount": 12, "failingTests": [], "errors": []},
    },
    {
        "id": "jest-fail-two-tests-console-noise",
        "framework": "jest",
        "command": "npx jest",
        "exitCode": 1,
        "output": """\
 PASS  src/parser/lexer.test.ts
 PASS  src/ledger/post.test.ts
  console.warn
    ledger: falling back to in-memory store

      at Ledger.open (src/ledger/store.ts:44:13)

 FAIL  src/queue.test.ts
  ● Queue › push › grows the queue

    expect(received).toBe(expected) // Object.is equality

    Expected: 2
    Received: 1

      10 |     const q = new Queue<number>();
      11 |     q.push(1);
    > 12 |     expect(q.size).toBe(2);
         |                    ^
      13 |   });
      14 |
      15 |   it('keeps insertion order', () => {

      at Object.<anonymous> (src/queue.test.ts:12:20)

  ● Queue › drain › resolves pending waiters

    expect(received).toEqual(expected) // deep equality

    - Expected  - 1
    + Received  + 1

      Array [
        1,
    -   2,
    +   3,
      ]

      31 |     q.push(3);
      32 |     const out = await q.drain();
    > 33 |     expect(out).toEqual([1, 2]);
         |                 ^
      34 |   });
      35 | });
      36 |

      at Object.<anonymous> (src/queue.test.ts:33:17)

Test Suites: 1 failed, 2 passed, 3 total
Tests:       2 failed, 10 passed, 12 total
Snapshots:   0 total
Time:        2.104 s
Ran all test suites.
""",
        "expect": {
            "failedCount": 2,
            "passedCount": 10,
            "failingTests": [
                "Queue › push › grows the queue",
                "Queue › drain › resolves pending waiters",
            ],
            "errors": [],
        },
        "note": "console.warn block with an 'at' frame sits in a PASS suite and must not count as a failure.",
    },
    {
        "id": "jest-suite-failed-to-run-syntax",
        "framework": "jest",
        "command": "npx jest src/parser.test.js",
        "exitCode": 1,
        "output": """\
 FAIL  src/parser.test.js
  ● Test suite failed to run

    SyntaxError: /Users/dev/work/orbit/src/parser.test.js: Unexpected token, expected "," (14:55)

      12 |   it('tokenizes binary expressions', () => {
      13 |     const tokens = tokenize('a + b');
    > 14 |     expect(tokens.map((t) => t.kind)).toEqual(['ident' 'op', 'ident']);
         |                                                        ^
      15 |   });
      16 | });
      17 |

      at constructor (node_modules/@babel/parser/src/parse-error.ts:95:45)
      at Parser.toParseError (node_modules/@babel/parser/src/tokenizer/index.ts:1504:19)
      at Parser.raise (node_modules/@babel/parser/src/tokenizer/index.ts:1564:19)
      at Parser.unexpected (node_modules/@babel/parser/src/tokenizer/index.ts:1604:16)

Test Suites: 1 failed, 1 total
Tests:       0 total
Snapshots:   0 total
Time:        0.874 s
Ran all test suites matching /src\\/parser.test.js/i.
""",
        "expect": {
            "failedCount": 0,
            "passedCount": 0,
            "failingTests": [],
            "errors": [{"file": "/Users/dev/work/orbit/src/parser.test.js", "code": None}],
        },
        "note": "Suite-level failure: no test ids, 'Tests: 0 total'; error path is absolute in the SyntaxError but relative in the FAIL header. 'Ran all ... matching' is the Jest 29 form (Jest 30 prints the pattern without slashes).",
    },
    {
        "id": "jest-npm-color-summary-tail",
        "framework": "jest",
        "command": "FORCE_COLOR=1 npm test 2>&1 | tail -n 48",
        "exitCode": 0,
        "output": _lines(
            _JEST_PASS + " " + _jpath("src/parser/", "grammar.test.ts"),
            _JEST_FAIL + " " + _jpath("src/", "queue.test.ts"),
            *_JEST_COLOR_BLOCK,
            _JEST_PASS + " " + _jpath("src/ledger/", "post.test.ts"),
            _JEST_PASS + " " + _jpath("src/ledger/", "balance.test.ts"),
            _JEST_PASS + " " + _jpath("src/cli/", "args.test.ts"),
            "",
            "\x1b[1mSummary of all failing tests\x1b[22m",
            _JEST_FAIL + " " + _jpath("src/", "queue.test.ts"),
            *_JEST_COLOR_BLOCK,
            "",
            "\x1b[1mTest Suites: \x1b[22m\x1b[1m\x1b[31m1 failed\x1b[39m\x1b[22m, \x1b[1m\x1b[32m23 passed\x1b[39m\x1b[22m, 24 total",
            "\x1b[1mTests:       \x1b[22m\x1b[1m\x1b[31m1 failed\x1b[39m\x1b[22m, \x1b[1m\x1b[32m88 passed\x1b[39m\x1b[22m, 89 total",
            "\x1b[1mSnapshots:   \x1b[22m0 total",
            "\x1b[1mTime:\x1b[22m        4.312 s",
            "\x1b[2mRan all test suites\x1b[22m\x1b[2m.\x1b[22m",
        ),
        "expect": {
            "failedCount": 1,
            "passedCount": 88,
            "failingTests": ["Queue › push › grows the queue"],
            "errors": [],
        },
        "note": "Head cut by tail (npm banner and first suites missing); exit code is tail's 0; the failure is printed twice (inline + 'Summary of all failing tests', shown because >20 suites) and must be deduped.",
    },
]

# -------------------------------------------------------------- vitest ----

_VC_FRAME = [
    " \x1b[36m \x1b[2m❯\x1b[22m src/queue.test.ts:\x1b[2m12:20\x1b[22m\x1b[39m",
    "    \x1b[90m 10|\x1b[39m     const q = new Queue<number>();",
    "    \x1b[90m 11|\x1b[39m     q.push(1);",
    "    \x1b[90m 12|\x1b[39m     expect(q.size).toBe(2);",
    "    \x1b[90m   |\x1b[39m                    \x1b[31m^\x1b[39m",
    "    \x1b[90m 13|\x1b[39m   });",
    "    \x1b[90m 14|\x1b[39m ",
]

_PART_B += [
    {
        "id": "vitest-pass-three-files",
        "framework": "vitest",
        "command": "npx vitest run",
        "exitCode": 0,
        "output": """\

 RUN  v3.2.4 /Users/dev/work/orbit

 ✓ src/parser/lexer.test.ts (5 tests) 6ms
 ✓ src/ledger/post.test.ts (4 tests) 3ms
 ✓ src/queue.test.ts (3 tests) 4ms

 Test Files  3 passed (3)
      Tests  12 passed (12)
   Start at  10:42:17
   Duration  412ms (transform 88ms, setup 0ms, collect 131ms, tests 13ms, environment 1ms, prepare 245ms)

""",
        "expect": {"failedCount": 0, "passedCount": 12, "failingTests": [], "errors": []},
    },
    {
        "id": "vitest-fail-one-assertion",
        "framework": "vitest",
        "command": "pnpm vitest run",
        "exitCode": 1,
        "output": """\

 RUN  v3.2.4 /Users/dev/work/orbit

 ✓ src/parser/lexer.test.ts (5 tests) 7ms
 ✓ src/ledger/post.test.ts (4 tests) 3ms
 ❯ src/queue.test.ts (3 tests | 1 failed) 9ms
   × Queue > push > grows the queue 5ms
     → expected 1 to be 2 // Object.is equality

@@BAR_FT1@@

 FAIL  src/queue.test.ts > Queue > push > grows the queue
AssertionError: expected 1 to be 2 // Object.is equality

- Expected
+ Received

- 2
+ 1

 ❯ src/queue.test.ts:12:20
     10|     const q = new Queue<number>();
     11|     q.push(1);
     12|     expect(q.size).toBe(2);
       |                    ^
     13|   });
     14| 

@@END_1_1@@


 Test Files  1 failed | 2 passed (3)
      Tests  1 failed | 11 passed (12)
   Start at  10:44:03
   Duration  438ms (transform 91ms, setup 0ms, collect 140ms, tests 19ms, environment 0ms, prepare 251ms)

""".replace("@@BAR_FT1@@", _vbar("Failed Tests 1")).replace("@@END_1_1@@", _vend(1, 1)),
        "expect": {
            "failedCount": 1,
            "passedCount": 11,
            "failingTests": ["src/queue.test.ts > Queue > push > grows the queue"],
            "errors": [],
        },
        "note": "Failing test appears twice: '× Queue > push > grows the queue 5ms' (no file, trailing duration) and the FAIL line (canonical id). Divider width 30 = Vitest's fallback when stdout has no columns.",
    },
    {
        "id": "vitest-suite-import-failure",
        "framework": "vitest",
        "command": "npx vitest run",
        "exitCode": 1,
        "output": """\

 RUN  v3.2.4 /Users/dev/work/orbit

 ✓ src/ledger/post.test.ts (4 tests) 3ms
 ✓ src/queue.test.ts (3 tests) 4ms
 ❯ src/broken.test.ts (0 test)

@@BAR_FS1@@

 FAIL  src/broken.test.ts [ src/broken.test.ts ]
Error: Failed to resolve import "../lib/tokenizer" from "src/broken.test.ts". Does the file exist?
  Plugin: vite:import-analysis
  File: /Users/dev/work/orbit/src/broken.test.ts:2:26
  1  |  import { describe, it, expect } from "vitest";
  2  |  import { tokenize } from "../lib/tokenizer";
     |                           ^
  3  |  
  4  |  describe("tokenize", () => {
 ❯ TransformPluginContext._formatLog node_modules/vite/dist/node/chunks/dep-D_zLpgQd.js:42528:41
 ❯ TransformPluginContext.error node_modules/vite/dist/node/chunks/dep-D_zLpgQd.js:42525:16
 ❯ normalizeUrl node_modules/vite/dist/node/chunks/dep-D_zLpgQd.js:40504:23

@@END_1_1@@


 Test Files  1 failed | 2 passed (3)
      Tests  7 passed (7)
   Start at  10:47:30
   Duration  377ms (transform 64ms, setup 0ms, collect 98ms, tests 7ms, environment 0ms, prepare 233ms)

""".replace("@@BAR_FS1@@", _vbar("Failed Suites 1")).replace("@@END_1_1@@", _vend(1, 1)),
        "expect": {
            "failedCount": 0,
            "passedCount": 7,
            "failingTests": [],
            "errors": [{"file": "src/broken.test.ts", "code": None}],
        },
        "note": "Load failure: 'Failed Suites' section, no test ids, Tests line has no failed count; failure only visible in Test Files. The vite code frame uses a different gutter ('2  |  ') than Vitest's own frames.",
    },
    {
        "id": "vitest-color-verbose-two-failures",
        "framework": "vitest",
        "command": "FORCE_COLOR=1 npx vitest run --reporter=verbose",
        "exitCode": 1,
        "output": _lines(
            "",
            "\x1b[7m\x1b[1m\x1b[36m RUN \x1b[39m\x1b[22m\x1b[27m \x1b[36mv3.2.4 \x1b[39m\x1b[90m/Users/dev/work/orbit\x1b[39m",
            "",
            " \x1b[32m✓\x1b[39m src/ledger/post.test.ts \x1b[2m(\x1b[22m\x1b[2m4 tests\x1b[22m\x1b[2m)\x1b[22m\x1b[32m 3\x1b[2mms\x1b[22m\x1b[39m",
            "   \x1b[32m✓\x1b[39m Ledger\x1b[2m > \x1b[22mpost\x1b[2m > \x1b[22mrecords a debit\x1b[32m 1\x1b[2mms\x1b[22m\x1b[39m",
            "   \x1b[32m✓\x1b[39m Ledger\x1b[2m > \x1b[22mpost\x1b[2m > \x1b[22mrecords a credit\x1b[32m 0\x1b[2mms\x1b[22m\x1b[39m",
            "   \x1b[32m✓\x1b[39m Ledger\x1b[2m > \x1b[22mpost\x1b[2m > \x1b[22mrejects zero amounts\x1b[32m 0\x1b[2mms\x1b[22m\x1b[39m",
            "   \x1b[32m✓\x1b[39m Ledger\x1b[2m > \x1b[22mpost\x1b[2m > \x1b[22mrounds to cents\x1b[32m 1\x1b[2mms\x1b[22m\x1b[39m",
            " \x1b[31m❯\x1b[39m src/parser/lexer.test.ts \x1b[2m(\x1b[22m\x1b[2m5 tests\x1b[22m\x1b[2m | \x1b[22m\x1b[31m1 failed\x1b[39m\x1b[2m)\x1b[22m\x1b[32m 8\x1b[2mms\x1b[22m\x1b[39m",
            "   \x1b[32m✓\x1b[39m lexer\x1b[2m > \x1b[22mreads numbers\x1b[32m 1\x1b[2mms\x1b[22m\x1b[39m",
            "   \x1b[31m×\x1b[39m lexer\x1b[2m > \x1b[22msplits identifiers\x1b[32m 4\x1b[2mms\x1b[22m\x1b[39m",
            "     \x1b[31m→ expected [ 'a', '+b' ] to deeply equal [ 'a', '+', 'b' ]\x1b[39m",
            "   \x1b[32m✓\x1b[39m lexer\x1b[2m > \x1b[22mskips comments\x1b[32m 0\x1b[2mms\x1b[22m\x1b[39m",
            "   \x1b[32m✓\x1b[39m lexer\x1b[2m > \x1b[22mtracks line numbers\x1b[32m 1\x1b[2mms\x1b[22m\x1b[39m",
            "   \x1b[32m✓\x1b[39m lexer\x1b[2m > \x1b[22mreports unterminated strings\x1b[32m 1\x1b[2mms\x1b[22m\x1b[39m",
            " \x1b[31m❯\x1b[39m src/queue.test.ts \x1b[2m(\x1b[22m\x1b[2m3 tests\x1b[22m\x1b[2m | \x1b[22m\x1b[31m1 failed\x1b[39m\x1b[2m)\x1b[22m\x1b[32m 9\x1b[2mms\x1b[22m\x1b[39m",
            "   \x1b[32m✓\x1b[39m Queue\x1b[2m > \x1b[22mpush\x1b[2m > \x1b[22mkeeps insertion order\x1b[32m 1\x1b[2mms\x1b[22m\x1b[39m",
            "   \x1b[31m×\x1b[39m Queue\x1b[2m > \x1b[22mpush\x1b[2m > \x1b[22mgrows the queue\x1b[32m 5\x1b[2mms\x1b[22m\x1b[39m",
            "     \x1b[31m→ expected 1 to be 2 // Object.is equality\x1b[39m",
            "   \x1b[32m✓\x1b[39m Queue\x1b[2m > \x1b[22mpop\x1b[2m > \x1b[22mshrinks the queue\x1b[32m 0\x1b[2mms\x1b[22m\x1b[39m",
            "",
            "\x1b[31m" + "⎯" * 7 + "\x1b[1m\x1b[41m Failed Tests 2 \x1b[49m\x1b[22m" + "⎯" * 7 + "\x1b[39m",
            "",
            " \x1b[31m\x1b[1m\x1b[7m FAIL \x1b[27m\x1b[22m\x1b[39m src/parser/lexer.test.ts\x1b[2m > \x1b[22mlexer\x1b[2m > \x1b[22msplits identifiers",
            "\x1b[31m\x1b[1mAssertionError\x1b[22m: expected [ 'a', '+b' ] to deeply equal [ 'a', '+', 'b' ]\x1b[39m",
            "",
            "\x1b[32m- Expected\x1b[39m",
            "\x1b[31m+ Received\x1b[39m",
            "",
            "\x1b[2m  [\x1b[22m",
            "\x1b[2m    \"a\",\x1b[22m",
            "\x1b[32m-   \"+\",\x1b[39m",
            "\x1b[32m-   \"b\",\x1b[39m",
            "\x1b[31m+   \"+b\",\x1b[39m",
            "\x1b[2m  ]\x1b[22m",
            "",
            " \x1b[36m \x1b[2m❯\x1b[22m src/parser/lexer.test.ts:\x1b[2m22:36\x1b[22m\x1b[39m",
            "    \x1b[90m 20|\x1b[39m   it(\"splits identifiers\", () => {",
            "    \x1b[90m 21|\x1b[39m     const out = lex(\"a +b\");",
            "    \x1b[90m 22|\x1b[39m     expect(out.map((t) => t.text)).toEqual([\"a\", \"+\", \"b\"]);",
            "    \x1b[90m   |\x1b[39m                                    \x1b[31m^\x1b[39m",
            "    \x1b[90m 23|\x1b[39m   });",
            "    \x1b[90m 24|\x1b[39m });",
            "",
            "\x1b[31m\x1b[2m" + _vend(1, 2) + "\x1b[22m\x1b[39m",
            "",
            " \x1b[31m\x1b[1m\x1b[7m FAIL \x1b[27m\x1b[22m\x1b[39m src/queue.test.ts\x1b[2m > \x1b[22mQueue\x1b[2m > \x1b[22mpush\x1b[2m > \x1b[22mgrows the queue",
            "\x1b[31m\x1b[1mAssertionError\x1b[22m: expected 1 to be 2 // Object.is equality\x1b[39m",
            "",
            "\x1b[32m- Expected\x1b[39m",
            "\x1b[31m+ Received\x1b[39m",
            "",
            "\x1b[32m- 2\x1b[39m",
            "\x1b[31m+ 1\x1b[39m",
            "",
            *_VC_FRAME,
            "",
            "\x1b[31m\x1b[2m" + _vend(2, 2) + "\x1b[22m\x1b[39m",
            "",
            "",
            "\x1b[2m Test Files \x1b[22m \x1b[1m\x1b[31m2 failed\x1b[39m\x1b[22m\x1b[2m | \x1b[22m\x1b[1m\x1b[32m1 passed\x1b[39m\x1b[22m\x1b[90m (3)\x1b[39m",
            "\x1b[2m      Tests \x1b[22m \x1b[1m\x1b[31m2 failed\x1b[39m\x1b[22m\x1b[2m | \x1b[22m\x1b[1m\x1b[32m10 passed\x1b[39m\x1b[22m\x1b[90m (12)\x1b[39m",
            "\x1b[2m   Start at \x1b[22m 10:51:09",
            "\x1b[2m   Duration \x1b[22m 512ms\x1b[2m (transform 97ms, setup 0ms, collect 151ms, tests 24ms, environment 0ms, prepare 262ms)\x1b[22m",
            "",
        ),
        "expect": {
            "failedCount": 2,
            "passedCount": 10,
            "failingTests": [
                "src/parser/lexer.test.ts > lexer > splits identifiers",
                "src/queue.test.ts > Queue > push > grows the queue",
            ],
            "errors": [],
        },
        "note": "ANSI everywhere: ' > ' separators are wrapped in dim codes, so ids only match after stripping escapes. Exact tinyrainbow sequences are approximated.",
    },
    {
        "id": "vitest-pnpm-recursive-interleaved",
        "framework": "vitest",
        "command": "pnpm -r test",
        "exitCode": 1,
        "output": """\
Scope: 2 of 3 workspace projects
packages/ledger test$ vitest run
packages/queue test$ vitest run
packages/ledger test: 
packages/ledger test:  RUN  v3.2.4 /Users/dev/work/orbit/packages/ledger
packages/ledger test: 
packages/queue test: 
packages/queue test:  RUN  v3.2.4 /Users/dev/work/orbit/packages/queue
packages/queue test: 
packages/ledger test:  ✓ src/post.test.ts (3 tests) 2ms
packages/queue test:  ❯ src/queue.test.ts (3 tests | 1 failed) 8ms
packages/queue test:    × Queue > push > grows the queue 4ms
packages/queue test:      → expected 1 to be 2 // Object.is equality
packages/ledger test:  ✓ src/balance.test.ts (4 tests) 3ms
packages/ledger test: 
packages/ledger test:  Test Files  2 passed (2)
packages/ledger test:       Tests  7 passed (7)
packages/queue test: 
packages/queue test: @@BAR_FT1@@
packages/ledger test:    Start at  10:58:40
packages/ledger test:    Duration  301ms (transform 52ms, setup 0ms, collect 61ms, tests 5ms, environment 0ms, prepare 188ms)
packages/queue test: 
packages/queue test:  FAIL  src/queue.test.ts > Queue > push > grows the queue
packages/ledger test: 
packages/ledger test: Done
packages/queue test: AssertionError: expected 1 to be 2 // Object.is equality
packages/queue test: 
packages/queue test: - Expected
packages/queue test: + Received
packages/queue test: 
packages/queue test: - 2
packages/queue test: + 1
packages/queue test: 
packages/queue test:  ❯ src/queue.test.ts:12:20
packages/queue test:      10|     const q = new Queue<number>();
packages/queue test:      11|     q.push(1);
packages/queue test:      12|     expect(q.size).toBe(2);
packages/queue test:        |                    ^
packages/queue test:      13|   });
packages/queue test:      14| 
packages/queue test: 
packages/queue test: @@END_1_1@@
packages/queue test: 
packages/queue test: 
packages/queue test:  Test Files  1 failed (1)
packages/queue test:       Tests  1 failed | 2 passed (3)
packages/queue test:    Start at  10:58:40
packages/queue test:    Duration  356ms (transform 60ms, setup 0ms, collect 70ms, tests 8ms, environment 0ms, prepare 201ms)
packages/queue test: 
packages/queue test: Failed
/Users/dev/work/orbit/packages/queue:
 ERR_PNPM_RECURSIVE_RUN_FIRST_FAIL  @orbit/queue@0.3.0 test: `vitest run`
Exit status 1
""".replace("@@BAR_FT1@@", _vbar("Failed Tests 1")).replace("@@END_1_1@@", _vend(1, 1)),
        "expect": {
            "failedCount": 1,
            "passedCount": 9,
            "failingTests": ["src/queue.test.ts > Queue > push > grows the queue"],
            "errors": [],
        },
        "note": "Two Vitest runs interleaved line by line behind pnpm prefixes; counts are the sum of both per-package summaries (ledger 7 passed, queue 1 failed | 2 passed). Test file path is relative to packages/queue.",
    },
]

# --------------------------------------------------------------- mocha ----

_PART_B += [
    {
        "id": "mocha-pass-spec",
        "framework": "mocha",
        "command": "npx mocha",
        "exitCode": 0,
        "output": """\

  Ledger
    \u2714 rounds to cents
    \u2714 rejects negative amounts (41ms)

  Queue
    push
      \u2714 grows the queue
      \u2714 keeps insertion order
    pop
      \u2714 shrinks on pop


  5 passing (63ms)

""",
        "expect": {"failedCount": 0, "passedCount": 5, "failingTests": [], "errors": []},
    },
    {
        "id": "mocha-fail-pending-diff",
        "framework": "mocha",
        "command": "npm test",
        "exitCode": 1,
        "output": """\

> orbit@1.4.0 test
> mocha 'test/**/*.spec.js'



  Ledger
    \u2714 rounds to cents
    - rejects negative amounts

  Queue
    push
      \u2714 grows the queue
      - keeps insertion order
    pop
      1) shrinks on pop
      \u2714 returns undefined when empty
      \u2714 drains in batches


  4 passing (18ms)
  2 pending
  1 failing

  1) Queue
       pop
         shrinks on pop:

      AssertionError [ERR_ASSERTION]: Expected values to be strictly equal:

1 !== 0

      + expected - actual

      -1
      +0
      
      at Context.<anonymous> (test/queue.spec.js:24:12)
      at process.processImmediate (node:internal/timers:485:21)



""",
        "expect": {
            "failedCount": 1,
            "passedCount": 4,
            "failingTests": ["Queue pop shrinks on pop"],
            "errors": [],
        },
        "note": "Id is Mocha's fullTitle (titlePath joined by spaces); the detail block splits it over three indented lines and the inline marker shows only 'shrinks on pop'. '1 !== 0' is unindented because only the first message line gets the indent.",
    },
    {
        "id": "mocha-before-each-hook-failure",
        "framework": "mocha",
        "command": "npx mocha 'test/**/*.spec.js'",
        "exitCode": 1,
        "output": """\

  Ledger
    \u2714 rounds to cents
    \u2714 rejects negative amounts

  Queue
    push
      1) "before each" hook for "grows"


  2 passing (9ms)
  1 failing

  1) Queue
       push
         "before each" hook for "grows":
     Error: ENOENT: no such file or directory, open '/home/dev/orbit/test/fixtures/queue.json'
      at Object.openSync (node:fs:562:18)
      at Object.readFileSync (node:fs:446:35)
      at Context.<anonymous> (test/queue.spec.js:9:22)
      at process.processImmediate (node:internal/timers:485:21)



""",
        "expect": {
            "failedCount": 1,
            "passedCount": 2,
            "failingTests": ['Queue push "before each" hook for "grows"'],
            "errors": [],
        },
        "note": "Hook failure: the remaining tests in 'push' are silently skipped (not counted as pending); the failing id is the hook title, which contains double quotes.",
    },
]

# ----------------------------------------------------------------- tsc ----

_PART_B += [
    {
        "id": "tsc-plain-three-errors",
        "framework": "tsc",
        "command": "npx tsc --noEmit",
        "exitCode": 2,
        "output": """\
src/ledger/entry.ts(8,3): error TS2741: Property 'amount' is missing in type '{ id: string; }' but required in type 'Entry'.
src/queue.ts(12,5): error TS2322: Type 'string' is not assignable to type 'number'.
src/queue.ts(27,17): error TS2339: Property 'peek' does not exist on type 'Queue<T>'.
""",
        "expect": {
            "failedCount": None,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "src/ledger/entry.ts", "code": "TS2741"},
                {"file": "src/queue.ts", "code": "TS2322"},
                {"file": "src/queue.ts", "code": "TS2339"},
            ],
        },
        "note": "Non-pretty output (stdout is a pipe) has no 'Found N errors' summary.",
    },
    {
        "id": "tsc-pretty-summary-table",
        "framework": "tsc",
        "command": "npx tsc --noEmit --pretty",
        "exitCode": 2,
        "output": _lines(
            "\x1b[96msrc/parser/lexer.ts\x1b[0m:\x1b[93m44\x1b[0m:\x1b[93m11\x1b[0m - \x1b[91merror\x1b[0m\x1b[90m TS7006: \x1b[0mParameter 'ch' implicitly has an 'any' type.",
            "",
            "\x1b[7m44\x1b[0m   isDigit(ch) {",
            "\x1b[7m  \x1b[0m \x1b[91m          ~~\x1b[0m",
            "",
            "\x1b[96msrc/queue.ts\x1b[0m:\x1b[93m12\x1b[0m:\x1b[93m5\x1b[0m - \x1b[91merror\x1b[0m\x1b[90m TS2322: \x1b[0mType 'string' is not assignable to type 'number'.",
            "",
            "\x1b[7m12\x1b[0m     this.size = label;",
            "\x1b[7m  \x1b[0m \x1b[91m    ~~~~~~~~~\x1b[0m",
            "",
            "\x1b[96msrc/queue.ts\x1b[0m:\x1b[93m27\x1b[0m:\x1b[93m17\x1b[0m - \x1b[91merror\x1b[0m\x1b[90m TS2339: \x1b[0mProperty 'peek' does not exist on type 'Queue<T>'.",
            "",
            "\x1b[7m27\x1b[0m     return this.peek();",
            "\x1b[7m  \x1b[0m \x1b[91m                ~~~~\x1b[0m",
            "",
            "",
            "Found 3 errors in 2 files.",
            "",
            "Errors  Files",
            "     1  src/parser/lexer.ts\x1b[90m:44\x1b[0m",
            "     2  src/queue.ts\x1b[90m:12\x1b[0m",
        ),
        "expect": {
            "failedCount": 3,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "src/parser/lexer.ts", "code": "TS7006"},
                {"file": "src/queue.ts", "code": "TS2322"},
                {"file": "src/queue.ts", "code": "TS2339"},
            ],
        },
        "note": "--pretty forces colour even through a pipe; the 'Errors  Files' table repeats file:line and must not create extra errors.",
    },
    {
        "id": "tsc-clean",
        "framework": "tsc",
        "command": "npx tsc --noEmit",
        "exitCode": 0,
        "output": "",
        "expect": {"failedCount": None, "passedCount": None, "failingTests": [], "errors": []},
        "note": "Success is silent.",
    },
    {
        "id": "tsc-windows-crlf-multiline",
        "framework": "tsc",
        "command": "npx tsc --noEmit -p tsconfig.json",
        "exitCode": 2,
        "output": """\
src/ledger/post.ts(31,22): error TS2345: Argument of type '{ id: string; amount: string; }' is not assignable to parameter of type 'Entry'.
  Types of property 'amount' are incompatible.
    Type 'string' is not assignable to type 'number'.
src/ledger/post.ts(44,9): error TS2322: Type 'number | undefined' is not assignable to type 'number'.
  Type 'undefined' is not assignable to type 'number'.
src/parser/index.ts(3,10): error TS2724: '"./lexer"' has no exported member named 'Tokenizer'. Did you mean 'tokenize'?
""".replace("\n", "\r\n"),
        "expect": {
            "failedCount": None,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "src/ledger/post.ts", "code": "TS2345"},
                {"file": "src/ledger/post.ts", "code": "TS2322"},
                {"file": "src/parser/index.ts", "code": "TS2724"},
            ],
        },
        "note": "Run on Windows (C:\\Users\\dev\\src\\orbit): CRLF line endings; indented elaboration lines belong to the error above and are not new errors.",
    },
    {
        "id": "tsc-head-c-cut-mid-line",
        "framework": "tsc",
        "command": "npx tsc --noEmit 2>&1 | head -c 409",
        "exitCode": 0,
        "output": (
            "src/cli/args.ts(14,10): error TS2305: Module '\"./options\"' has no exported member 'parseFlags'.\n"
            "src/cli/args.ts(29,3): error TS2353: Object literal may only specify known properties, and 'verbos' does not exist in type 'Flags'.\n"
            "src/ledger/post.ts(31,22): error TS2345: Argument of type 'string' is not assignable to parameter of type 'number'.\n"
            "src/parser/lexer.ts(61,7): error TS2552: Cannot find name 'peekCh"
        ),
        "expect": {
            "failedCount": None,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "src/cli/args.ts", "code": "TS2305"},
                {"file": "src/cli/args.ts", "code": "TS2353"},
                {"file": "src/ledger/post.ts", "code": "TS2345"},
                {"file": "src/parser/lexer.ts", "code": "TS2552"},
            ],
        },
        "note": "Byte-truncated by head: last line cut mid-message with no trailing newline (its file and code are still intact); exit code is head's 0 despite errors.",
    },
]

# -------------------------------------------------------------- eslint ----

_PART_B += [
    {
        "id": "eslint-stylish-errors-warning-fixable",
        "framework": "eslint",
        "command": "npx eslint .",
        "exitCode": 1,
        "output": """\

/Users/dev/work/orbit/src/ledger/post.ts
  8:7  error  'total' is never reassigned. Use 'const' instead  prefer-const

/Users/dev/work/orbit/src/queue.ts
  12:5   error    'x' is assigned a value but never used    @typescript-eslint/no-unused-vars
  27:18  error    Unexpected any. Specify a different type  @typescript-eslint/no-explicit-any
  41:3   warning  Unexpected console statement              no-console

\u2716 4 problems (3 errors, 1 warning)
  1 error and 0 warnings potentially fixable with the `--fix` option.

""",
        "expect": {
            "failedCount": 3,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "/Users/dev/work/orbit/src/ledger/post.ts", "code": "prefer-const"},
                {"file": "/Users/dev/work/orbit/src/queue.ts", "code": "@typescript-eslint/no-unused-vars"},
                {"file": "/Users/dev/work/orbit/src/queue.ts", "code": "@typescript-eslint/no-explicit-any"},
            ],
        },
        "note": "Columns are padded per file (text-table), so spacing differs between the two files; the warning is excluded from errors.",
    },
    {
        "id": "eslint-parsing-error",
        "framework": "eslint",
        "command": "npx eslint src/parser",
        "exitCode": 1,
        "output": """\

/Users/dev/work/orbit/src/parser/grammar.js
  57:3  error  Parsing error: Unexpected token }

/Users/dev/work/orbit/src/parser/index.js
  14:10  error  'tokenise' is not defined  no-undef

\u2716 2 problems (2 errors, 0 warnings)

""",
        "expect": {
            "failedCount": 2,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "/Users/dev/work/orbit/src/parser/grammar.js", "code": None},
                {"file": "/Users/dev/work/orbit/src/parser/index.js", "code": "no-undef"},
            ],
        },
        "note": "Parsing errors have no rule id, so the row ends right after the message.",
    },
    {
        "id": "eslint-clean",
        "framework": "eslint",
        "command": "npx eslint src",
        "exitCode": 0,
        "output": "",
        "expect": {"failedCount": None, "passedCount": None, "failingTests": [], "errors": []},
    },
    {
        "id": "eslint-color-osc8-links",
        "framework": "eslint",
        "command": "npx eslint --color src/queue.ts",
        "exitCode": 1,
        "output": _lines(
            "",
            "\x1b]8;;file:///Users/dev/work/orbit/src/queue.ts\x1b\\\x1b[4m/Users/dev/work/orbit/src/queue.ts\x1b[24m\x1b]8;;\x1b\\",
            "\x1b[2m   7:1\x1b[22m   \x1b[33mwarning\x1b[39m  Unexpected console statement" + " " * 12 + "\x1b[2mno-console\x1b[22m",
            "\x1b[2m  12:5\x1b[22m   \x1b[31merror\x1b[39m    'x' is assigned a value but never used  \x1b[2mno-unused-vars\x1b[22m",
            "\x1b[2m  19:12\x1b[22m  \x1b[31merror\x1b[39m    Expected '===' and instead saw '=='" + " " * 5 + "\x1b[2meqeqeq\x1b[22m",
            "",
            "\x1b[31m\x1b[1m\u2716 3 problems (2 errors, 1 warning)\x1b[22m\x1b[39m",
            "",
        ),
        "expect": {
            "failedCount": 2,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "/Users/dev/work/orbit/src/queue.ts", "code": "no-unused-vars"},
                {"file": "/Users/dev/work/orbit/src/queue.ts", "code": "eqeqeq"},
            ],
        },
        "note": "File header wrapped in an OSC 8 hyperlink (as added by hyperlink-aware stylish wrappers; core stylish only underlines); line:col is dimmed together with its leading spaces.",
    },
]

# ---------------------------------------------------------------- ruff ----

_PART_B += [
    {
        "id": "ruff-concise-legacy",
        "framework": "ruff",
        "command": "ruff check src",
        "exitCode": 1,
        "output": """\
src/orbit/ledger.py:3:1: I001 [*] Import block is un-sorted or un-formatted
src/orbit/queue.py:1:8: F401 [*] `os` imported but unused
src/orbit/queue.py:14:5: E741 Ambiguous variable name: `l`
Found 3 errors.
[*] 2 fixable with the `--fix` option.
""",
        "expect": {
            "failedCount": 3,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "src/orbit/ledger.py", "code": "I001"},
                {"file": "src/orbit/queue.py", "code": "F401"},
                {"file": "src/orbit/queue.py", "code": "E741"},
            ],
        },
        "note": "ruff 0.4 default text format; '[*]' marks fixable and is not part of the code.",
    },
    {
        "id": "ruff-full-format-snippets",
        "framework": "ruff",
        "command": "uv run ruff check",
        "exitCode": 1,
        "output": """\
F401 [*] `os` imported but unused
 --> src/orbit/queue.py:1:8
  |
1 | import os
  |        ^^
  |
help: Remove unused import: `os`

F821 Undefined name `Waiter`
  --> src/orbit/queue.py:18:30
   |
16 |     def __init__(self) -> None:
17 |         self._items: list[int] = []
18 |         self._waiters: deque[Waiter] = deque()
   |                              ^^^^^^
   |

Found 2 errors.
[*] 1 fixable with the `--fix` option.
""",
        "expect": {
            "failedCount": 2,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "src/orbit/queue.py", "code": "F401"},
                {"file": "src/orbit/queue.py", "code": "F821"},
            ],
        },
        "note": "ruff 0.12+ full format: code and message come first, location on the ' --> ' line whose indent grows with gutter width. Unsure whether ruff also prints trailing context lines after the caret; followed the common no-after-context rendering.",
    },
    {
        "id": "ruff-all-checks-passed",
        "framework": "ruff",
        "command": "ruff check .",
        "exitCode": 0,
        "output": "All checks passed!\n",
        "expect": {"failedCount": 0, "passedCount": None, "failingTests": [], "errors": []},
    },
    {
        "id": "ruff-syntax-error-concise",
        "framework": "ruff",
        "command": "ruff check --output-format concise src",
        "exitCode": 1,
        "output": """\
src/orbit/cli.py:3:8: F401 [*] `sys` imported but unused
src/orbit/parser.py:41:40: SyntaxError: Expected ')', found newline
Found 2 errors.
[*] 1 fixable with the `--fix` option.
""",
        "expect": {
            "failedCount": 2,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "src/orbit/cli.py", "code": "F401"},
                {"file": "src/orbit/parser.py", "code": None},
            ],
        },
        "note": "Syntax errors carry no rule code ('SyntaxError:' in ruff 0.9-0.11; newer releases print 'invalid-syntax:') but are counted in 'Found 2 errors.'.",
    },
    {
        "id": "ruff-format-check",
        "framework": "ruff",
        "command": "ruff format --check",
        "exitCode": 1,
        "output": """\
Would reformat: src/orbit/queue.py
1 file would be reformatted, 3 files already formatted
""",
        "expect": {
            "failedCount": 1,
            "passedCount": None,
            "failingTests": [],
            "errors": [{"file": "src/orbit/queue.py", "code": None}],
        },
        "note": "Formatter check: failedCount is the number of files that would be reformatted; no rule codes.",
    },
]

# ---------------------------------------------------------------- mypy ----

_PART_B += [
    {
        "id": "mypy-errors-with-notes",
        "framework": "mypy",
        "command": "mypy src",
        "exitCode": 1,
        "output": """\
src/orbit/queue.py:12: error: Incompatible return value type (got "str", expected "int")  [return-value]
src/orbit/queue.py:30: error: Argument 1 to "push" of "Queue" has incompatible type "str"; expected "int"  [arg-type]
src/orbit/ledger.py:5: error: Library stubs not installed for "requests"  [import-untyped]
src/orbit/ledger.py:5: note: Hint: "python3 -m pip install types-requests"
src/orbit/ledger.py:5: note: (or run "mypy --install-types" to install all missing stub packages)
src/orbit/ledger.py:5: note: See https://mypy.readthedocs.io/en/stable/running_mypy.html#missing-imports
Found 3 errors in 2 files (checked 14 source files)
""",
        "expect": {
            "failedCount": 3,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "src/orbit/queue.py", "code": "return-value"},
                {"file": "src/orbit/queue.py", "code": "arg-type"},
                {"file": "src/orbit/ledger.py", "code": "import-untyped"},
            ],
        },
        "note": "'note:' lines share the error's file:line but are not errors; code is in trailing brackets after two spaces.",
    },
    {
        "id": "mypy-success",
        "framework": "mypy",
        "command": "uv run mypy src",
        "exitCode": 0,
        "output": "Success: no issues found in 14 source files\n",
        "expect": {"failedCount": 0, "passedCount": None, "failingTests": [], "errors": []},
    },
    {
        "id": "mypy-show-column-numbers",
        "framework": "mypy",
        "command": "mypy --show-column-numbers src",
        "exitCode": 1,
        "output": """\
src/orbit/cli.py:7:1: error: Cannot find implementation or library stub for module named "orbit.plugins"  [import-not-found]
src/orbit/cli.py:7:1: note: See https://mypy.readthedocs.io/en/stable/running_mypy.html#missing-imports
src/orbit/parser.py:12:5: error: Name "foo" is not defined  [name-defined]
src/orbit/parser.py:27:16: error: Incompatible types in assignment (expression has type "None", variable has type "Token")  [assignment]
src/orbit/parser.py:58:12: error: Item "None" of "Token | None" has no attribute "kind"  [union-attr]
Found 4 errors in 2 files (checked 14 source files)
""",
        "expect": {
            "failedCount": 4,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "src/orbit/cli.py", "code": "import-not-found"},
                {"file": "src/orbit/parser.py", "code": "name-defined"},
                {"file": "src/orbit/parser.py", "code": "assignment"},
                {"file": "src/orbit/parser.py", "code": "union-attr"},
            ],
        },
    },
    {
        "id": "mypy-blocking-syntax-error",
        "framework": "mypy",
        "command": "mypy src",
        "exitCode": 2,
        "output": """\
src/orbit/parser.py:41: error: '(' was never closed  [syntax]
Found 1 error in 1 file (errors prevented further checking)
""",
        "expect": {
            "failedCount": 1,
            "passedCount": None,
            "failingTests": [],
            "errors": [{"file": "src/orbit/parser.py", "code": "syntax"}],
        },
        "note": "Blocking error: summary has no 'checked N source files' and exit status is 2 rather than 1.",
    },
]

# ------------------------------------------------------------- pyright ----

_PART_B += [
    {
        "id": "pyright-errors-multiline",
        "framework": "pyright",
        "command": "npx pyright",
        "exitCode": 1,
        "output": """\
/Users/dev/work/orbit/src/orbit/ledger.py
  /Users/dev/work/orbit/src/orbit/ledger.py:44:20 - error: "amount" is not a known attribute of "None" (reportOptionalMemberAccess)
/Users/dev/work/orbit/src/orbit/queue.py
  /Users/dev/work/orbit/src/orbit/queue.py:12:5 - error: "foo" is not defined (reportUndefinedVariable)
  /Users/dev/work/orbit/src/orbit/queue.py:21:9 - warning: Expression value is unused (reportUnusedExpression)
  /Users/dev/work/orbit/src/orbit/queue.py:30:16 - error: Type "str" is not assignable to return type "int"
    "str" is not assignable to "int" (reportReturnType)
  /Users/dev/work/orbit/src/orbit/queue.py:37:21 - error: Cannot access attribute "peek" for class "Queue[int]"
    Attribute "peek" is unknown (reportAttributeAccessIssue)
4 errors, 1 warning, 0 informations 
""",
        "expect": {
            "failedCount": 4,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "/Users/dev/work/orbit/src/orbit/ledger.py", "code": "reportOptionalMemberAccess"},
                {"file": "/Users/dev/work/orbit/src/orbit/queue.py", "code": "reportUndefinedVariable"},
                {"file": "/Users/dev/work/orbit/src/orbit/queue.py", "code": "reportReturnType"},
                {"file": "/Users/dev/work/orbit/src/orbit/queue.py", "code": "reportAttributeAccessIssue"},
            ],
        },
        "note": "For multi-line messages the (rule) sits at the end of the last continuation line; summary line has a trailing space.",
    },
    {
        "id": "pyright-clean-with-update-notice",
        "framework": "pyright",
        "command": "pyright",
        "exitCode": 0,
        "output": """\
WARNING: there is a new pyright version available (v1.1.404 -> v1.1.407).
Please install the new version or set PYRIGHT_PYTHON_FORCE_VERSION to `latest`

0 errors, 0 warnings, 0 informations 
""",
        "expect": {"failedCount": 0, "passedCount": None, "failingTests": [], "errors": []},
        "note": "pip-installed pyright wrapper prints an update WARNING that is not a diagnostic.",
    },
    {
        "id": "basedpyright-errors",
        "framework": "pyright",
        "command": "uv run basedpyright",
        "exitCode": 1,
        "output": """\
/home/dev/orbit/src/orbit/queue.py
  /home/dev/orbit/src/orbit/queue.py:9:24 - error: Type of parameter "items" is partially unknown
    Parameter type is "list[Unknown]" (reportUnknownParameterType)
  /home/dev/orbit/src/orbit/queue.py:15:16 - warning: Return type is Any (reportAny)
  /home/dev/orbit/src/orbit/queue.py:22:9 - error: Method "push" is not marked as override but is overriding a method in class "BaseQueue" (reportImplicitOverride)
2 errors, 1 warning, 0 notes
""",
        "expect": {
            "failedCount": 2,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "/home/dev/orbit/src/orbit/queue.py", "code": "reportUnknownParameterType"},
                {"file": "/home/dev/orbit/src/orbit/queue.py", "code": "reportImplicitOverride"},
            ],
        },
        "note": "basedpyright says 'notes' instead of 'informations'; unsure whether it keeps pyright's trailing space on the summary line.",
    },
]

# -------------------------------------------------------------- gradle ----

_PART_B += [
    {
        "id": "gradle-junit5-test-failure",
        "framework": "gradle",
        "command": "./gradlew test",
        "exitCode": 1,
        "output": """\
> Task :compileJava UP-TO-DATE
> Task :processResources NO-SOURCE
> Task :classes UP-TO-DATE
> Task :compileTestJava
> Task :processTestResources NO-SOURCE
> Task :testClasses

> Task :test FAILED

QueueTest > pushGrowsQueue() FAILED
    org.opentest4j.AssertionFailedError at QueueTest.java:15

5 tests completed, 1 failed

FAILURE: Build failed with an exception.

* What went wrong:
Execution failed for task ':test'.
> There were failing tests. See the report at: file:///Users/dev/work/orbit/build/reports/tests/test/index.html

* Try:
> Run with --scan to get full insights.

BUILD FAILED in 4s
4 actionable tasks: 2 executed, 2 up-to-date
""",
        "expect": {
            "failedCount": 1,
            "passedCount": 4,
            "failingTests": ["QueueTest > pushGrowsQueue()"],
            "errors": [],
        },
        "note": "Gradle reports 'completed' and 'failed' only; passedCount is derived (completed - failed - skipped).",
    },
    {
        "id": "gradle-javac-compile-errors",
        "framework": "gradle",
        "command": "./gradlew build",
        "exitCode": 1,
        "output": """\
> Task :compileJava FAILED
/Users/dev/work/orbit/src/main/java/orbit/Queue.java:12: error: cannot find symbol
        return items.peekFirst(capacity);
                    ^
  symbol:   method peekFirst(int)
  location: variable items of type Deque<T>
  where T is a type-variable:
    T extends Object declared in class Queue
/Users/dev/work/orbit/src/main/java/orbit/Ledger.java:31: error: incompatible types: String cannot be converted to long
        long cents = entry.amount();
                                 ^
2 errors

[Incubating] Problems report is available at: file:///Users/dev/work/orbit/build/reports/problems/problems-report.html

FAILURE: Build failed with an exception.

* What went wrong:
Execution failed for task ':compileJava'.
> Compilation failed; see the compiler output below.
  /Users/dev/work/orbit/src/main/java/orbit/Queue.java:12: error: cannot find symbol
          return items.peekFirst(capacity);
                      ^
    symbol:   method peekFirst(int)
    location: variable items of type Deque<T>
    where T is a type-variable:
      T extends Object declared in class Queue
  /Users/dev/work/orbit/src/main/java/orbit/Ledger.java:31: error: incompatible types: String cannot be converted to long
          long cents = entry.amount();
                                   ^
  2 errors

* Try:
> Check your code and dependencies to fix the compilation error(s)
> Run with --scan to get full insights.

BUILD FAILED in 2s
1 actionable task: 1 executed
""",
        "expect": {
            "failedCount": 2,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "/Users/dev/work/orbit/src/main/java/orbit/Queue.java", "code": None},
                {"file": "/Users/dev/work/orbit/src/main/java/orbit/Ledger.java", "code": None},
            ],
        },
        "note": "Gradle 8.11+ repeats the javac errors (indented by two spaces) under 'What went wrong'; dedupe. failedCount comes from javac's '2 errors'.",
    },
    {
        "id": "gradle-kotlin-compile-errors",
        "framework": "gradle",
        "command": "./gradlew test",
        "exitCode": 1,
        "output": """\
> Task :checkKotlinGradlePluginConfigurationErrors SKIPPED
> Task :processResources NO-SOURCE

> Task :compileKotlin FAILED
e: file:///Users/dev/work/orbit/src/main/kotlin/orbit/Queue.kt:12:5 Unresolved reference 'foo'.
e: file:///Users/dev/work/orbit/src/main/kotlin/orbit/Queue.kt:27:20 Argument type mismatch: actual type is 'String', but 'Int' was expected.
w: file:///Users/dev/work/orbit/src/main/kotlin/orbit/Ledger.kt:8:9 Parameter 'scale' is never used.

[Incubating] Problems report is available at: file:///Users/dev/work/orbit/build/reports/problems/problems-report.html

FAILURE: Build failed with an exception.

* What went wrong:
Execution failed for task ':compileKotlin'.
> A failure occurred while executing org.jetbrains.kotlin.compilerRunner.GradleCompilerRunnerWithWorkers$GradleKotlinCompilerWorkAction
   > Compilation error. See log for more details

* Try:
> Run with --stacktrace option to get the stack trace.
> Run with --info or --debug option to get more log output.
> Run with --scan to get full insights.
> Get more help at https://help.gradle.org.

BUILD FAILED in 9s
1 actionable task: 1 executed
""",
        "expect": {
            "failedCount": None,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "file:///Users/dev/work/orbit/src/main/kotlin/orbit/Queue.kt", "code": None},
                {"file": "file:///Users/dev/work/orbit/src/main/kotlin/orbit/Queue.kt", "code": None},
            ],
        },
        "note": "Kotlin 2.x prints file:// URIs (kept verbatim here); 'w:' lines are warnings. Compile fails before any test runs, so there is no test summary.",
    },
    {
        "id": "gradle-build-successful",
        "framework": "gradle",
        "command": "./gradlew test",
        "exitCode": 0,
        "output": """\
Starting a Gradle Daemon (subsequent builds will be faster)
> Task :compileJava
> Task :processResources NO-SOURCE
> Task :classes
> Task :compileTestJava
> Task :processTestResources NO-SOURCE
> Task :testClasses
> Task :test

Deprecated Gradle features were used in this build, making it incompatible with Gradle 9.0.

You can use '--warning-mode all' to show the individual deprecation warnings and determine if they come from your own scripts or plugins.

For more on this, please refer to https://docs.gradle.org/8.14.3/userguide/command_line_interface.html#sec:command_line_warnings in the Gradle documentation.

BUILD SUCCESSFUL in 6s
3 actionable tasks: 3 executed
""",
        "expect": {"failedCount": None, "passedCount": None, "failingTests": [], "errors": []},
        "note": "Passing Gradle test runs print no test counts.",
    },
    {
        "id": "gradle-flaky-retry-pass",
        "framework": "gradle",
        "command": "./gradlew test",
        "exitCode": 0,
        "output": """\
> Task :compileJava UP-TO-DATE
> Task :processResources NO-SOURCE
> Task :classes UP-TO-DATE
> Task :compileTestJava UP-TO-DATE
> Task :processTestResources NO-SOURCE
> Task :testClasses UP-TO-DATE

> Task :test

QueueTest > drainResolvesWaiters() FAILED
    org.opentest4j.AssertionFailedError at QueueTest.java:58

13 tests completed, 1 failed

BUILD SUCCESSFUL in 7s
4 actionable tasks: 1 executed, 3 up-to-date
""",
        "expect": {
            "failedCount": 1,
            "passedCount": 12,
            "failingTests": ["QueueTest > drainResolvesWaiters()"],
            "errors": [],
        },
        "note": "Flaky: test-retry plugin re-ran the test and it passed, so the build succeeds (exit 0) while the summary still says '1 failed' (13 = 12 tests + 1 retry).",
    },
]

# --------------------------------------------------------------- maven ----

_MVN_HEAD = (
    "[INFO] Scanning for projects...\n"
    "[INFO] \n"
    "[INFO] " + "-" * 26 + "< dev.orbit:orbit >" + "-" * 27 + "\n"
    "[INFO] Building orbit 1.4.0-SNAPSHOT\n"
    "[INFO]   from pom.xml\n"
    "[INFO] " + "-" * 32 + "[ jar ]" + "-" * 33 + "\n"
    "[INFO] \n"
)
_MVN_RULE = "[INFO] " + "-" * 72
_MVN_PREP = """\
[INFO] --- resources:3.3.1:resources (default-resources) @ orbit ---
[INFO] skip non existing resourceDirectory /Users/dev/work/orbit/src/main/resources
[INFO] 
[INFO] --- compiler:3.13.0:compile (default-compile) @ orbit ---
[INFO] Nothing to compile - all classes are up to date.
[INFO] 
[INFO] --- resources:3.3.1:testResources (default-testResources) @ orbit ---
[INFO] skip non existing resourceDirectory /Users/dev/work/orbit/src/test/resources
[INFO] 
[INFO] --- compiler:3.13.0:testCompile (default-testCompile) @ orbit ---
[INFO] Nothing to compile - all classes are up to date.
[INFO] 
[INFO] --- surefire:3.5.2:test (default-test) @ orbit ---
[INFO] Using auto detected provider org.apache.maven.surefire.junitplatform.JUnitPlatformProvider
[INFO] 
[INFO] -------------------------------------------------------
[INFO]  T E S T S
[INFO] -------------------------------------------------------
"""
_MVN_HELP = """\
[ERROR] -> [Help 1]
[ERROR] 
[ERROR] To see the full stack trace of the errors, re-run Maven with the -e switch.
[ERROR] Re-run Maven using the -X switch to enable full debug logging.
[ERROR] 
[ERROR] For more information about the errors and possible solutions, please read the following articles:
"""

_PART_B += [
    {
        "id": "maven-surefire-failure",
        "framework": "maven",
        "command": "mvn test",
        "exitCode": 1,
        "output": _MVN_HEAD + _MVN_PREP + """\
[INFO] Running orbit.LedgerTest
[INFO] Tests run: 5, Failures: 0, Errors: 0, Skipped: 0, Time elapsed: 0.031 s -- in orbit.LedgerTest
[INFO] Running orbit.QueueTest
[ERROR] Tests run: 3, Failures: 1, Errors: 0, Skipped: 0, Time elapsed: 0.05 s <<< FAILURE! -- in orbit.QueueTest
[ERROR] orbit.QueueTest.pushGrowsQueue -- Time elapsed: 0.01 s <<< FAILURE!
org.opentest4j.AssertionFailedError: expected: <2> but was: <1>
\tat org.junit.jupiter.api.AssertionFailureBuilder.build(AssertionFailureBuilder.java:151)
\tat org.junit.jupiter.api.AssertionFailureBuilder.buildAndThrow(AssertionFailureBuilder.java:132)
\tat org.junit.jupiter.api.AssertEquals.failNotEqual(AssertEquals.java:197)
\tat org.junit.jupiter.api.AssertEquals.assertEquals(AssertEquals.java:150)
\tat org.junit.jupiter.api.Assertions.assertEquals(Assertions.java:531)
\tat orbit.QueueTest.pushGrowsQueue(QueueTest.java:15)
\tat java.base/java.lang.reflect.Method.invoke(Method.java:580)

[INFO] Running orbit.parser.TokenizerTest
[INFO] Tests run: 4, Failures: 0, Errors: 0, Skipped: 0, Time elapsed: 0.012 s -- in orbit.parser.TokenizerTest
[INFO] 
[INFO] Results:
[INFO] 
[ERROR] Failures: 
[ERROR]   QueueTest.pushGrowsQueue:15 expected: <2> but was: <1>
[INFO] 
[ERROR] Tests run: 12, Failures: 1, Errors: 0, Skipped: 0
[INFO] 
@@RULE@@
[INFO] BUILD FAILURE
@@RULE@@
[INFO] Total time:  2.384 s
[INFO] Finished at: 2026-10-27T14:03:51+08:00
@@RULE@@
[ERROR] Failed to execute goal org.apache.maven.plugins:maven-surefire-plugin:3.5.2:test (default-test) on project orbit: There are test failures.
[ERROR] 
[ERROR] See /Users/dev/work/orbit/target/surefire-reports for the individual test results.
[ERROR] See dump files (if any exist) [date].dump, [date]-jvmRun[N].dump and [date].dumpstream.
""".replace("@@RULE@@", _MVN_RULE) + _MVN_HELP + """\
[ERROR] [Help 1] http://cwiki.apache.org/confluence/display/MAVEN/MojoFailureException
""",
        "expect": {
            "failedCount": 1,
            "passedCount": 11,
            "failingTests": ["orbit.QueueTest.pushGrowsQueue"],
            "errors": [],
        },
        "note": "Same failure appears as 'orbit.QueueTest.pushGrowsQueue' (per-test line) and 'QueueTest.pushGrowsQueue:15' (Results list); the per-class 'Tests run:' lines must not be summed with the final total. passedCount derived (run - failures - errors - skipped).",
    },
    {
        "id": "maven-compilation-error",
        "framework": "maven",
        "command": "mvn compile",
        "exitCode": 1,
        "output": _MVN_HEAD + """\
[INFO] --- resources:3.3.1:resources (default-resources) @ orbit ---
[INFO] Copying 1 resource from src/main/resources to target/classes
[INFO] 
[INFO] --- compiler:3.13.0:compile (default-compile) @ orbit ---
[INFO] Recompiling the module because of changed source code.
[INFO] Compiling 9 source files with javac [debug release 21] to target/classes
[WARNING] /Users/dev/work/orbit/src/main/java/orbit/Ledger.java: Some input files use unchecked or unsafe operations.
[WARNING] /Users/dev/work/orbit/src/main/java/orbit/Ledger.java: Recompile with -Xlint:unchecked for details.
[INFO] -------------------------------------------------------------
[ERROR] COMPILATION ERROR : 
[INFO] -------------------------------------------------------------
[ERROR] /Users/dev/work/orbit/src/main/java/orbit/Queue.java:[12,5] cannot find symbol
  symbol:   variable capacty
  location: class orbit.Queue<T>
[ERROR] /Users/dev/work/orbit/src/main/java/orbit/Ledger.java:[31,33] incompatible types: java.lang.String cannot be converted to long
[INFO] 2 errors 
[INFO] -------------------------------------------------------------
@@RULE@@
[INFO] BUILD FAILURE
@@RULE@@
[INFO] Total time:  1.207 s
[INFO] Finished at: 2026-10-27T14:10:02+08:00
@@RULE@@
[ERROR] Failed to execute goal org.apache.maven.plugins:maven-compiler-plugin:3.13.0:compile (default-compile) on project orbit: Compilation failure: Compilation failure: 
[ERROR] /Users/dev/work/orbit/src/main/java/orbit/Queue.java:[12,5] cannot find symbol
[ERROR]   symbol:   variable capacty
[ERROR]   location: class orbit.Queue<T>
[ERROR] /Users/dev/work/orbit/src/main/java/orbit/Ledger.java:[31,33] incompatible types: java.lang.String cannot be converted to long
""".replace("@@RULE@@", _MVN_RULE) + _MVN_HELP + """\
[ERROR] [Help 1] http://cwiki.apache.org/confluence/display/MAVEN/MojoFailureException
""",
        "expect": {
            "failedCount": 2,
            "passedCount": None,
            "failingTests": [],
            "errors": [
                {"file": "/Users/dev/work/orbit/src/main/java/orbit/Queue.java", "code": None},
                {"file": "/Users/dev/work/orbit/src/main/java/orbit/Ledger.java", "code": None},
            ],
        },
        "note": "Errors are printed twice (COMPILATION ERROR block and the 'Failed to execute goal' echo); location uses [line,col]; '[INFO] 2 errors ' has a trailing space; [WARNING] lines are not errors.",
    },
    {
        "id": "maven-pass-with-skipped",
        "framework": "maven",
        "command": "mvn test",
        "exitCode": 0,
        "output": _MVN_HEAD + _MVN_PREP + """\
[INFO] Running orbit.LedgerTest
[INFO] Tests run: 5, Failures: 0, Errors: 0, Skipped: 0, Time elapsed: 0.034 s -- in orbit.LedgerTest
[INFO] Running orbit.QueueTest
[WARNING] Tests run: 3, Failures: 0, Errors: 0, Skipped: 1, Time elapsed: 0.021 s -- in orbit.QueueTest
[INFO] Running orbit.parser.TokenizerTest
[INFO] Tests run: 4, Failures: 0, Errors: 0, Skipped: 0, Time elapsed: 0.011 s -- in orbit.parser.TokenizerTest
[INFO] 
[INFO] Results:
[INFO] 
[WARNING] Tests run: 12, Failures: 0, Errors: 0, Skipped: 1
[INFO] 
@@RULE@@
[INFO] BUILD SUCCESS
@@RULE@@
[INFO] Total time:  2.011 s
[INFO] Finished at: 2026-10-27T14:12:40+08:00
@@RULE@@
""".replace("@@RULE@@", _MVN_RULE),
        "expect": {"failedCount": 0, "passedCount": 11, "failingTests": [], "errors": []},
        "note": "Skipped tests turn the summary into a [WARNING] line on a passing build; passedCount derived (12 run - 1 skipped).",
    },
]

# --------------------------------------------------------------- rspec ----

_PART_B += [
    {
        "id": "rspec-failure-pending",
        "framework": "rspec",
        "command": "bundle exec rspec",
        "exitCode": 1,
        "output": """\
..*F.

Pending: (Failures listed here are expected and do not affect your suite's status)

  1) Queue#pop drains in batches
     # Temporarily skipped with xit
     # ./spec/queue_spec.rb:30

Failures:

  1) Queue#push grows the queue
     Failure/Error: expect(queue.size).to eq(2)

       expected: 2
            got: 1

       (compared using ==)
     # ./spec/queue_spec.rb:12:in 'block (3 levels) in <top (required)>'

Finished in 0.0123 seconds (files took 0.10512 seconds to load)
5 examples, 1 failure, 1 pending

Failed examples:

rspec ./spec/queue_spec.rb:10 # Queue#push grows the queue

""",
        "expect": {
            "failedCount": 1,
            "passedCount": 3,
            "failingTests": ["./spec/queue_spec.rb:10"],
            "errors": [],
        },
        "note": "Pending list is also numbered '1)' and must not be read as a failure; failing id taken from the 'rspec <file:line>' rerun line (line of the example, not of the assertion). passedCount derived (examples - failures - pending).",
    },
    {
        "id": "rspec-pass-documentation",
        "framework": "rspec",
        "command": "bundle exec rspec --format documentation",
        "exitCode": 0,
        "output": """\

Randomized with seed 41297

Ledger
  rounds to cents
  rejects negative amounts

Queue
  #push
    grows the queue
    keeps insertion order
  #pop
    shrinks the queue

Finished in 0.00912 seconds (files took 0.07431 seconds to load)
5 examples, 0 failures

Randomized with seed 41297

""",
        "expect": {"failedCount": 0, "passedCount": 5, "failingTests": [], "errors": []},
    },
    {
        "id": "rspec-error-outside-examples",
        "framework": "rspec",
        "command": "bundle exec rspec spec/ledger_spec.rb",
        "exitCode": 1,
        "output": """\

An error occurred while loading ./spec/ledger_spec.rb.
Failure/Error: require "orbit/ledger"

LoadError:
  cannot load such file -- orbit/ledger
# ./spec/ledger_spec.rb:3:in 'Kernel#require'
# ./spec/ledger_spec.rb:3:in '<top (required)>'
No examples found.

Finished in 0.00003 seconds (files took 0.11402 seconds to load)
0 examples, 0 failures, 1 error occurred outside of examples

""",
        "expect": {
            "failedCount": 0,
            "passedCount": 0,
            "failingTests": [],
            "errors": [{"file": "./spec/ledger_spec.rb", "code": None}],
        },
        "note": "Load error: '0 failures' yet the run failed (exit 1); the only signal is '1 error occurred outside of examples'. Ruby 3.4 backtrace quoting ('...').",
    },
]

# ------------------------------------------------------------- phpunit ----

_PHPUNIT_HEAD = """\
PHPUnit 11.5.3 by Sebastian Bergmann and contributors.

Runtime:       PHP 8.3.14
Configuration: /Users/dev/work/orbit/phpunit.xml

"""

_PART_B += [
    {
        "id": "phpunit-one-failure",
        "framework": "phpunit",
        "command": "vendor/bin/phpunit",
        "exitCode": 1,
        "output": _PHPUNIT_HEAD + "..F.".ljust(67) + " 4 / 4 (100%)\n" + """\

Time: 00:00.021, Memory: 8.00 MB

There was 1 failure:

1) Tests\\QueueTest::testPush
Failed asserting that 1 matches expected 2.

/Users/dev/work/orbit/tests/QueueTest.php:15

FAILURES!
Tests: 4, Assertions: 6, Failures: 1.
""",
        "expect": {
            "failedCount": 1,
            "passedCount": 3,
            "failingTests": ["Tests\\QueueTest::testPush"],
            "errors": [],
        },
        "note": "Progress row is padded to column 67 before the counter. passedCount derived (tests - failures).",
    },
    {
        "id": "phpunit-ok",
        "framework": "phpunit",
        "command": "composer test",
        "exitCode": 0,
        "output": "> phpunit\n" + _PHPUNIT_HEAD + "....".ljust(67) + " 4 / 4 (100%)\n" + """\

Time: 00:00.018, Memory: 8.00 MB

OK (4 tests, 6 assertions)
""",
        "expect": {"failedCount": 0, "passedCount": 4, "failingTests": [], "errors": []},
        "note": "Composer echoes the script ('> phpunit') before PHPUnit's banner.",
    },
    {
        "id": "phpunit-errors-data-set-skipped",
        "framework": "phpunit",
        "command": "vendor/bin/phpunit --testsuite unit",
        "exitCode": 1,
        "output": _PHPUNIT_HEAD + ".EFS.F".ljust(67) + " 6 / 6 (100%)\n" + """\

Time: 00:00.034, Memory: 10.00 MB

There was 1 error:

1) Tests\\LedgerTest::testPostRejectsNegative
TypeError: Orbit\\Ledger::post(): Argument #1 ($amount) must be of type int, string given, called in /Users/dev/work/orbit/tests/LedgerTest.php on line 22

/Users/dev/work/orbit/src/Ledger.php:31
/Users/dev/work/orbit/tests/LedgerTest.php:22

--

There were 2 failures:

1) Tests\\QueueTest::testPush
Failed asserting that 1 matches expected 2.

/Users/dev/work/orbit/tests/QueueTest.php:15

2) Tests\\QueueTest::testPop with data set "empty"
Failed asserting that 0 is null.

/Users/dev/work/orbit/tests/QueueTest.php:41

ERRORS!
Tests: 6, Assertions: 7, Errors: 1, Failures: 2, Skipped: 1.
""",
        "expect": {
            "failedCount": 3,
            "passedCount": 2,
            "failingTests": [
                "Tests\\LedgerTest::testPostRejectsNegative",
                "Tests\\QueueTest::testPush",
                'Tests\\QueueTest::testPop with data set "empty"',
            ],
            "errors": [],
        },
        "note": "failedCount = Errors + Failures; numbering restarts per section; data-set name is part of the id. PHPUnit 10+ omits the data values (PHPUnit 9 appended them in parentheses).",
    },
]

# ------------------------------------------------------------- generic ----

_PART_B += [
    {
        "id": "generic-node-typeerror-crash",
        "framework": "generic",
        "command": "node scripts/migrate.js",
        "exitCode": 1,
        "output": """\
/Users/dev/work/orbit/scripts/migrate.js:18
  const target = config.db.x;
                           ^

TypeError: Cannot read properties of undefined (reading 'x')
    at migrate (/Users/dev/work/orbit/scripts/migrate.js:18:28)
    at Object.<anonymous> (/Users/dev/work/orbit/scripts/migrate.js:31:1)
    at Module._compile (node:internal/modules/cjs/loader:1730:14)
    at Object..js (node:internal/modules/cjs/loader:1895:10)
    at Module.load (node:internal/modules/cjs/loader:1465:32)
    at Function._load (node:internal/modules/cjs/loader:1282:12)
    at TracingChannel.traceSync (node:diagnostics_channel:322:14)
    at wrapModuleLoad (node:internal/modules/cjs/loader:235:24)
    at Function.executeUserEntryPoint [as runMain] (node:internal/modules/run_main:171:5)
    at node:internal/main/run_main_module:36:49

Node.js v22.20.0
""",
        "expect": {
            "failedCount": None,
            "passedCount": None,
            "failingTests": [],
            "errors": [{"file": "/Users/dev/work/orbit/scripts/migrate.js", "code": "TypeError"}],
        },
        "note": "Uncaught exception: code is the exception class; node:internal frames are noise.",
    },
    {
        "id": "generic-python-module-not-found",
        "framework": "generic",
        "command": "python3 -m orbit.worker",
        "exitCode": 1,
        "output": """\
Traceback (most recent call last):
  File "<frozen runpy>", line 198, in _run_module_as_main
  File "<frozen runpy>", line 88, in _run_code
  File "/home/dev/orbit/src/orbit/worker.py", line 7, in <module>
    from orbit.scheduler import Scheduler
  File "/home/dev/orbit/src/orbit/scheduler.py", line 3, in <module>
    from orbit.queue import Queue, QueueFull
ModuleNotFoundError: No module named 'orbit.queue'
""",
        "expect": {
            "failedCount": None,
            "passedCount": None,
            "failingTests": [],
            "errors": [{"file": "/home/dev/orbit/src/orbit/scheduler.py", "code": "ModuleNotFoundError"}],
        },
        "note": "Innermost user frame is the error location; '<frozen runpy>' frames are not files.",
    },
    {
        "id": "generic-make-script-error",
        "framework": "generic",
        "command": "make test",
        "exitCode": 2,
        "output": """\
mkdir -p build/reports
./scripts/check-config.sh
checking config/orbit.toml ...
error: config file missing: config/orbit.toml
hint: copy config/orbit.example.toml to config/orbit.toml and set ORBIT_DB_URL
make: *** [Makefile:8: test] Error 2
""",
        "expect": {
            "failedCount": None,
            "passedCount": None,
            "failingTests": [],
            "errors": [{"file": "Makefile", "code": None}],
        },
        "note": "Only located failure is make's '[Makefile:8: test]'; the script's 'error:' line names a missing file, not a source location. GNU make 4.x format (macOS make 3.81 prints 'make: *** [test] Error 2').",
    },
    {
        "id": "generic-bun-test-fail",
        "framework": "generic",
        "command": "bun test",
        "exitCode": 1,
        "output": """\
bun test v1.2.21 (7c45ed97)

src/ledger.test.ts:
(pass) Ledger > rounds to cents [0.05ms]
(pass) Ledger > rejects negative amounts [0.02ms]

src/queue.test.ts:
(pass) Queue > push > keeps insertion order [0.12ms]
10 |   it("grows the queue", () => {
11 |     const q = new Queue<number>();
12 |     q.push(1);
13 |     expect(q.size).toBe(2);
                        ^
error: expect(received).toBe(expected)

Expected: 2
Received: 1

      at <anonymous> (/Users/dev/work/orbit/src/queue.test.ts:13:20)
(fail) Queue > push > grows the queue [0.41ms]
(pass) Queue > pop > shrinks the queue [0.03ms]

1 tests failed:
(fail) Queue > push > grows the queue [0.41ms]

 4 pass
 1 fail
 9 expect() calls
Ran 5 tests across 2 files. [38.00ms]
""",
        "expect": {
            "failedCount": 1,
            "passedCount": 4,
            "failingTests": ["Queue > push > grows the queue"],
            "errors": [],
        },
        "note": "Non-TTY bun prints '(pass)'/'(fail)' instead of check marks; the error block comes BEFORE its '(fail)' line; failure is repeated in the recap. Unsure whether the '1 tests failed:' recap appears for small runs.",
    },
    {
        "id": "generic-deno-test-fail",
        "framework": "generic",
        "command": "deno test",
        "exitCode": 1,
        "output": """\
Check file:///Users/dev/work/orbit/src/queue_test.ts
running 3 tests from ./src/queue_test.ts
push grows the queue ... FAILED (2ms)
push keeps insertion order ... ok (0ms)
pop shrinks the queue ... ok (0ms)

 ERRORS 

push grows the queue => ./src/queue_test.ts:10:6
error: AssertionError: Values are not equal.


    [Diff] Actual / Expected


-   1
+   2

  throw new AssertionError(message);
        ^
    at assertEquals (https://jsr.io/@std/assert/1.0.13/equals.ts:64:9)
    at file:///Users/dev/work/orbit/src/queue_test.ts:13:3

 FAILURES 

push grows the queue => ./src/queue_test.ts:10:6

FAILED | 2 passed | 1 failed (14ms)

error: Test failed
""",
        "expect": {
            "failedCount": 1,
            "passedCount": 2,
            "failingTests": ["push grows the queue"],
            "errors": [],
        },
        "note": "Deno 2: ' ERRORS ' / ' FAILURES ' banners keep their padding spaces without colour; the 'error:' lines are test failures, not compile errors.",
    },
]

OUTPUTS = _PART_A + _PART_B
