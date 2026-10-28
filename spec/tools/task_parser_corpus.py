"""Hand-written corpus of the task-score parsers (spec/TASK-PARSERS.md), read by task_parsers.py.

`cases()` lists every fixture case with its inputs and `want`, the expectation written by hand. Digests in `want` are
computed here with hashlib from hand-normalised text, independently of the reference. `hand_check` compares a
reference output with `want`; `task_parsers.py --write` refuses to write the fixture while one fails. Everything is
fictional; nothing is copied from real agent logs.
"""
import hashlib
import json

from task_parser_outputs import OUTPUTS

CWD = "/Users/dev/work/orbit"


def sha16(text):
    return hashlib.sha256(text.encode("utf-8")).hexdigest()[:16]


E = sha16("")


def H(file, old, new):
    return {"file": sha16(file), "old": None if old is None else sha16(old), "new": sha16(new)}


def key(*segments):
    return sha16("\x1e".join("\x1f".join(tokens) for tokens in segments))


# ============================================================================ action_kind

def kinds():
    rows = [
        # Claude
        ("claudeCode", "Read", {"file_path": "/Users/dev/work/orbit/src/queue.py"}, "read"),
        ("claudeCode", "Grep", {"pattern": "push", "path": "src"}, "search"),
        ("claudeCode", "Glob", {"pattern": "**/*.py"}, "search"),
        ("claudeCode", "LSP", {"operation": "references"}, "search"),
        ("claudeCode", "Edit", {"file_path": "a.py", "old_string": "a", "new_string": "b"}, "edit"),
        ("claudeCode", "MultiEdit", {"file_path": "a.py", "edits": []}, "edit"),
        ("claudeDesktop", "Write", {"file_path": "a.py", "content": ""}, "edit"),
        ("claudeCode", "NotebookEdit", {"notebook_path": "a.ipynb", "new_source": ""}, "edit"),
        ("claudeCode", "Bash", {"command": "cargo test", "description": "Run tests"}, "command"),
        ("claudeCode", "Bash", {"command": "cat src/queue.py | head -n 40"}, "read"),
        ("claudeCode", "Bash", {"command": "rg -n 'fn push' src | head"}, "search"),
        ("claudeCode", "Bash", {"command": "git status && git diff --stat"}, "read"),
        ("claudeCode", "Bash", {"command": "git grep -n push"}, "search"),
        ("claudeCode", "Bash", {"command": "sed -i '' 's/a/b/' src/queue.py"}, "command"),
        ("claudeCode", "Bash", {"command": "cat > notes.txt <<'EOF'\nhello\nEOF"}, "command"),
        ("claudeCode", "Bash", {"command": "find . -name '*.orig' -delete"}, "command"),
        ("claudeCode", "Bash", {"command": "echo done"}, "command"),
        ("claudeCode", "Bash", {"command": "cd src && ls -la"}, "read"),
        ("claudeCode", "Bash", {"command": "git commit -m 'wip'"}, "command"),
        ("claudeCode", "Bash", {"command": "cat src/a.py && cargo test"}, "command"),
        ("claudeCode", "PowerShell", {"command": "dotnet test"}, "command"),
        ("claudeCode", "Bash", {"command": "apply_patch <<'EOF'\n*** Begin Patch\n*** Delete File: a.txt\n*** End Patch\nEOF"},
         "edit"),
        ("claudeCode", "Bash", {}, "command"),
        ("claudeCode", "Bash", {"command": ["ls", "-la"]}, "command"),
        ("claudeCode", "Bash", {"command": "git -C packages/queue status"}, "read"),
        ("claudeCode", "Bash", {"command": "git show grep"}, "read"),
        ("claudeCode", "Bash", {"command": "echo hi > f.txt && cat f.txt"}, "command"),
        ("claudeCode", "Bash", {"command": "cat a.txt > /dev/null 2>&1"}, "read"),
        ("claudeCode", "Task", {"description": "Explore", "prompt": "find", "subagent_type": "Explore"}, "delegate"),
        ("claudeCode", "Agent", {"description": "Explore", "prompt": "find"}, "delegate"),
        ("claudeCode", "SendMessage", {"to": "a1", "message": "go on"}, "delegate"),
        ("claudeCode", "WebFetch", {"url": "https://example.com", "prompt": "summary"}, "web"),
        ("claudeCode", "WebSearch", {"query": "rust borrow"}, "web"),
        ("claudeCode", "TodoWrite", {"todos": []}, "other"),
        ("claudeCode", "BashOutput", {"bash_id": "bash_1"}, "other"),
        ("claudeCode", "mcp__github__create_issue", {"title": "x"}, "other"),
        ("claudeCode", "bash", {"command": "ls"}, "other"),
        # Codex
        ("codex", "shell", '{"command":["bash","-lc","cargo test"],"workdir":"/Users/dev/work/orbit"}', "command"),
        ("codex", "shell", '{"command":["sed","-n","1,80p","src/lib.rs"]}', "read"),
        ("codex", "shell", '{"command":["rg","--files"]}', "search"),
        ("codex", "shell", '{"command":["apply_patch","*** Begin Patch\\n*** Delete File: a.txt\\n*** End Patch\\n"]}',
         "edit"),
        ("codex", "shell_command", '{"command":"go test ./..."}', "command"),
        ("codex", "exec_command", '{"cmd":"pnpm vitest run","yield_time_ms":10000}', "command"),
        ("codex", "exec_command", '{"cmd":"nl -ba src/queue.ts | sed -n 1,120p"}', "read"),
        ("codex", "exec", '{"cmd":"cargo test","yield_time_ms":10000}', "command"),
        ("codex", "exec", "ls", "read"),
        ("codex", "apply_patch", "*** Begin Patch\n*** Delete File: a.txt\n*** End Patch", "edit"),
        ("codex", "write_stdin", '{"session_id":7,"chars":""}', "other"),
        ("codex", "update_plan", '{"plan":[]}', "other"),
        ("codex", "view_image", '{"path":"shot.png"}', "read"),
        ("codex", "spawn_agent", '{"message":"review"}', "delegate"),
        ("codex", "wait_agent", '{"ids":["a"]}', "other"),
        ("codex", "web_search", '{"query":"x"}', "web"),
        ("codex", "shell", "not json", "command"),
        ("codex", "exec", '{"foo":1}', "command"),
        ("codex", "shell", '{"command":[]}', "command"),
        # omp
        ("omp", "read", {"path": "src/queue.ts"}, "read"),
        ("omp", "grep", {"pattern": "dispatch("}, "search"),
        ("omp", "search", {"pattern": "x"}, "search"),
        ("omp", "find", {"pattern": "*.ts"}, "search"),
        ("omp", "edit", {"input": "*** Begin Patch\n[src/a.ts#1a2b]\nREM\n*** End Patch"}, "edit"),
        ("omp", "write", {"path": "a.ts", "content": ""}, "edit"),
        ("omp", "bash", {"command": "bun test"}, "command"),
        ("omp", "task", {"tasks": []}, "delegate"),
        ("omp", "yield", {"result": "done"}, "other"),
        ("omp", "web_search", {"query": "x"}, "web"),
        ("omp", "Bash", {"command": "ls"}, "read"),
        # Pi
        ("pi", "read", {"path": "src/queue.ts"}, "read"),
        ("pi", "ls", {"path": "src"}, "read"),
        ("pi", "find", {"pattern": "*.ts"}, "search"),
        ("pi", "edit", {"path": "a.ts", "edits": []}, "edit"),
        ("pi", "bash", {"command": "npm test"}, "command"),
        ("pi", "powershell", {"command": "dotnet build"}, "command"),
        # OpenClaw
        ("openclaw", "exec", {"command": "pytest -q"}, "command"),
        ("openclaw", "exec", {"command": "ls -la"}, "read"),
        ("openclaw", "apply_patch", {"input": "*** Begin Patch\n*** Delete File: a.txt\n*** End Patch"}, "edit"),
        ("openclaw", "edit", {"path": "a.ts", "edits": []}, "edit"),
        ("openclaw", "process", {"action": "poll", "sessionId": "7"}, "other"),
        ("openclaw", "sessions_spawn", {"task": "review"}, "delegate"),
        ("openclaw", "web_fetch", {"url": "https://example.com"}, "web"),
        ("openclaw", "memory_search", {"query": "x"}, "other"),
        # others
        ("gemini", "run_shell_command", {"command": "ls"}, "other"),
        ("codex", None, "{}", "other"),
    ]
    out = []
    for n, (source, tool, value, want) in enumerate(rows):
        out.append({"id": "kind-%02d-%s-%s" % (n, source, tool), "group": "actionKind", "source": source,
                    "tool": tool, "input": value, "cwd": CWD, "want": want})
    return out


# ============================================================================ command_info

def C(cls, suite=None, targeted=False, masked=False, background=False):
    return {"class": cls, "suite": suite, "targeted": targeted, "masked": masked, "background": background}


COMMANDS = [
    # (id, text, want, extra) — extra: {"key": hand digest} or {"sameKey": id} or {"otherKey": id}
    ("cargo", "cargo test", C("test", "cargo test"), {"key": key(["cargo", "test"])}),
    ("cargo-cd-env-tail", "cd /Users/dev/work/orbit && RUST_BACKTRACE=1 cargo test 2>&1 | tail -n 40",
     C("test", "cargo test", masked=True), {"sameKey": "cargo"}),
    ("cargo-color-flag", "cargo test --color=always -q", C("test", "cargo test"), {"sameKey": "cargo"}),
    ("cargo-package", "cargo test -p orbit-queue", C("test", "cargo test", True),
     {"key": key(["cargo", "test", "-p", "orbit-queue"])}),
    ("cargo-filter", "cargo test queue::tests::push", C("test", "cargo test", True), {}),
    ("cargo-libtest-exact", "cargo test --workspace -- --exact queue::push", C("test", "cargo test", True), {}),
    ("cargo-workspace", "cargo test --workspace --no-fail-fast", C("test", "cargo test"), {}),
    ("cargo-nextest", "cargo nextest run", C("test", "cargo nextest"), {}),
    ("cargo-nextest-filter", "cargo nextest run -E 'test(push)'", C("test", "cargo nextest", True), {}),
    ("cargo-build-then-test", "cargo build && cargo test", C("test", "cargo test"),
     {"key": key(["cargo", "build"], ["cargo", "test"])}),
    ("cargo-or-true", "cargo test || true", C("test", "cargo test", masked=True), {}),
    ("cargo-semicolon-echo", "cargo test; echo exit=$?", C("test", "cargo test", masked=True), {}),
    ("cargo-clippy", "cargo clippy --all-targets -- -D warnings", C("check", "cargo clippy"), {}),
    ("cargo-check", "cargo check", C("check", "cargo check"), {}),
    ("cargo-fmt-check", "cargo fmt --check", C("check", "cargo fmt"), {}),
    ("cargo-fmt", "cargo fmt", C("other"), {}),
    ("cargo-run", "cargo run --release", C("other"), {}),
    ("cargo-toolchain", "cargo +nightly build", C("build", "cargo build"), {}),
    ("swift-test", "swift test", C("test", "swift test"), {}),
    ("swift-test-filter-beautify", "swift test --filter QueueTests 2>&1 | xcbeautify",
     C("test", "swift test", True, True), {}),
    ("swift-build", "swift build -c release", C("build", "swift build"), {}),
    ("xcrun-swift", "xcrun swift test", C("test", "swift test"), {"sameKey": "swift-test"}),
    ("xcodebuild-test", "xcodebuild test -scheme Orbit -destination 'platform=macOS'", C("test", "xcodebuild test"), {}),
    ("xcodebuild-only", "xcodebuild -scheme Orbit test -only-testing:OrbitTests/QueueTests",
     C("test", "xcodebuild test", True), {}),
    ("xcodebuild-build", "xcodebuild -project Orbit.xcodeproj -scheme Orbit build", C("build", "xcodebuild build"), {}),
    ("xcodebuild-default", "xcodebuild -scheme Orbit CODE_SIGNING_ALLOWED=NO", C("build", "xcodebuild build"), {}),
    ("xcodebuild-list", "xcodebuild -list", C("other"), {}),
    ("pytest", "pytest", C("test", "pytest"), {"key": key(["pytest"])}),
    ("python-m-pytest", "python3 -m pytest", C("test", "pytest"), {"sameKey": "pytest"}),
    ("uv-pytest", "uv run pytest", C("test", "pytest"), {"sameKey": "pytest"}),
    ("poetry-pytest-q", "poetry run pytest -q", C("test", "pytest"), {"sameKey": "pytest"}),
    ("pytest-tests-dir", "pytest tests/", C("test", "pytest"), {}),
    ("pytest-file", "pytest tests/test_queue.py -x", C("test", "pytest", True), {}),
    ("pytest-node", "python -m pytest 'tests/test_queue.py::test_push[case-1]'", C("test", "pytest", True), {}),
    ("pytest-k", "pytest -k 'push and not slow'", C("test", "pytest", True), {}),
    ("pytest-lf", "pytest --lf", C("test", "pytest", True), {}),
    ("pytest-subdir", "pytest tests/unit", C("test", "pytest", True), {}),
    ("pytest-abs-root", "pytest /Users/dev/work/orbit/tests", C("test", "pytest"), {}),
    ("pytest-cov", "pytest --cov src --cov-report term-missing", C("test", "pytest"), {}),
    ("unittest", "python -m unittest discover -s tests", C("test", "unittest"), {}),
    ("unittest-target", "python -m unittest tests.test_queue.QueueTests", C("test", "unittest", True), {}),
    ("django", "python manage.py test ledger", C("test", "django test", True), {}),
    ("tox", "tox -e py312", C("test", "tox"), {}),
    ("python-script", "python scripts/migrate.py", C("other"), {}),
    ("mypy", "mypy src", C("check", "mypy"), {}),
    ("mypy-file", "uv run mypy src/orbit/queue.py", C("check", "mypy", True), {}),
    ("ruff", "ruff check .", C("check", "ruff"), {}),
    ("ruff-format-check", "ruff format --check", C("check", "ruff format"), {}),
    ("ruff-format", "ruff format", C("other"), {}),
    ("pyright", "npx pyright", C("check", "pyright"), {}),
    ("go-test", "go test ./...", C("test", "go test"), {}),
    ("go-test-run", "go test -run TestParse ./parser", C("test", "go test", True), {}),
    ("go-test-pkg", "go test ./queue/...", C("test", "go test", True), {}),
    ("go-test-race", "go test -race -count=1 ./...", C("test", "go test"), {}),
    ("go-build", "go build ./...", C("build", "go build"), {}),
    ("go-vet", "go vet ./...", C("check", "go vet"), {}),
    ("golangci", "golangci-lint run ./...", C("check", "golangci-lint"), {}),
    ("npm-test", "npm test", C("test", "npm test"), {"key": key(["npm", "run", "test"])}),
    ("npm-run-test", "npm run test", C("test", "npm test"), {"sameKey": "npm-test"}),
    ("npm-test-file", "npm test -- src/queue.test.ts", C("test", "npm test", True), {}),
    ("npm-test-reporter", "npm test -- --reporter dot", C("test", "npm test"), {}),
    ("npm-test-t", "npm test -- -t 'grows the queue'", C("test", "npm test", True), {}),
    ("npm-run-lint", "npm run lint", C("check", "npm run lint"), {}),
    ("npm-run-typecheck", "npm run typecheck", C("check", "npm run typecheck"), {}),
    ("npm-run-build", "npm run build", C("build", "npm run build"), {}),
    ("npm-run-test-unit", "npm run test:unit", C("test", None), {}),
    ("npm-workspace", "npm test -w packages/queue", C("test", "npm test", True), {}),
    ("npm-install", "npm install", C("other"), {}),
    ("pnpm-test", "pnpm test", C("test", "pnpm test"), {}),
    ("pnpm-lint", "pnpm lint", C("check", "pnpm run lint"), {"sameKey": "pnpm-run-lint"}),
    ("pnpm-run-lint", "pnpm run lint", C("check", "pnpm run lint"), {}),
    ("pnpm-vitest", "pnpm vitest run", C("test", "vitest"), {}),
    ("pnpm-filter", "pnpm --filter @orbit/queue test", C("test", "pnpm test", True), {}),
    ("yarn-test", "yarn test", C("test", "yarn test"), {}),
    ("yarn-workspace", "yarn workspace @orbit/queue test", C("test", "yarn test", True), {}),
    ("bun-test", "bun test", C("test", "bun test"), {}),
    ("bun-run-test", "bun run test", C("test", "bun run test"), {}),
    ("npx-jest", "npx jest src/queue.test.ts", C("test", "jest", True), {}),
    ("jest-all", "npx jest --ci", C("test", "jest"), {}),
    ("vitest-run", "npx vitest run", C("test", "vitest"), {}),
    ("vitest-filter", "npx vitest run queue", C("test", "vitest", True), {}),
    ("mocha", "npx mocha 'test/**/*.spec.js'", C("test", "mocha", True), {}),
    ("mocha-grep", "mocha --grep push", C("test", "mocha", True), {}),
    ("tsc", "npx tsc --noEmit", C("check", "tsc"), {}),
    ("tsc-project", "tsc -p tsconfig.build.json --noEmit", C("check", "tsc"), {}),
    ("tsc-watch", "tsc --watch", C("other"), {}),
    ("eslint", "npx eslint .", C("check", "eslint"), {}),
    ("eslint-file", "eslint src/queue.ts --fix", C("check", "eslint", True), {}),
    ("prettier-check", "npx prettier --check .", C("check", "prettier"), {}),
    ("prettier-write", "npx prettier --write .", C("other"), {}),
    ("dotnet-test", "dotnet test", C("test", "dotnet test"), {}),
    ("dotnet-test-filter", "dotnet test --filter FullyQualifiedName~Queue", C("test", "dotnet test", True), {}),
    ("dotnet-build", "dotnet build -c Release", C("build", "dotnet build"), {}),
    ("dotnet-format", "dotnet format --verify-no-changes", C("check", "dotnet format"), {}),
    ("pwsh-dotnet", "pwsh -NoProfile -Command \"dotnet test; exit $LASTEXITCODE\"", C("test", "dotnet test",
                                                                                     masked=True), {}),
    ("gradle-test", "./gradlew test", C("test", "gradle test"), {}),
    ("gradle-tests-filter", "./gradlew test --tests 'orbit.QueueTest'", C("test", "gradle test", True), {}),
    ("gradle-module", "./gradlew :app:test", C("test", "gradle test", True), {}),
    ("gradle-build", "gradle build", C("test", "gradle build"), {}),
    ("gradle-build-skip", "./gradlew build -x test", C("build", "gradle build"), {}),
    ("gradle-assemble", "./gradlew assembleDebug", C("build", None), {}),
    ("mvn-test", "mvn test", C("test", "mvn test"), {}),
    ("mvn-test-one", "mvn -q test -Dtest=QueueTest", C("test", "mvn test", True), {}),
    ("mvn-package-skip", "./mvnw package -DskipTests", C("build", "mvn package"), {}),
    ("mvn-compile", "mvn compile", C("build", "mvn compile"), {}),
    ("make-test", "make test", C("test", "make test"), {}),
    ("make-check", "make check", C("test", "make check"), {}),
    ("make-default", "make -j8", C("build", "make"), {}),
    ("make-test-var", "make test TEST=queue", C("test", "make test", True), {}),
    ("make-dir", "make -C build lint", C("check", "make lint"), {}),
    ("make-install", "make install", C("other"), {}),
    ("just-test", "just test", C("test", "just test"), {}),
    ("just-test-args", "just test queue", C("test", "just test", True), {}),
    ("cmake-build", "cmake --build build -j", C("build", "cmake --build"), {}),
    ("ctest", "ctest --test-dir build --output-on-failure", C("test", "ctest"), {}),
    ("ctest-r", "ctest --test-dir build -R queue", C("test", "ctest", True), {}),
    ("cc", "clang -Wall -Werror -c src/main.c -o build/main.o", C("build", "cc"), {}),
    ("cc-syntax", "gcc -fsyntax-only src/main.c", C("check", "cc"), {}),
    ("rspec", "bundle exec rspec", C("test", "rspec"), {}),
    ("rspec-line", "bundle exec rspec spec/queue_spec.rb:10", C("test", "rspec", True), {}),
    ("rails-test", "bin/rails test test/models/queue_test.rb", C("test", "rails test", True), {}),
    ("phpunit", "vendor/bin/phpunit", C("test", "phpunit"), {}),
    ("phpunit-filter", "php vendor/bin/phpunit --filter testPush", C("test", "phpunit", True), {}),
    ("composer-test", "composer test", C("test", "composer test"), {}),
    ("mix-test", "mix test test/queue_test.exs:12", C("test", "mix test", True), {}),
    ("deno-test", "deno test --allow-read", C("test", "deno test"), {}),
    ("bash-lc", "bash -lc 'cd /Users/dev/work/orbit && cargo test'", C("test", "cargo test"), {"sameKey": "cargo"}),
    ("cd-subdir", "cd packages/queue && npm test", C("test", "npm test"),
     {"key": key(["@packages/queue", "npm", "run", "test"])}),
    ("subshell-cd", "(cd packages/queue && npm test) && npm test", C("test", "npm test"),
     {"key": key(["@packages/queue", "npm", "run", "test"], ["npm", "run", "test"])}),
    ("env-prefix", "env CI=1 FORCE_COLOR=0 npx vitest run", C("test", "vitest"), {"sameKey": "vitest-run"}),
    ("timeout-prefix", "timeout 300 cargo test", C("test", "cargo test"), {"sameKey": "cargo"}),
    ("sudo-time", "time go test ./...", C("test", "go test"), {"sameKey": "go-test"}),
    ("nohup-bg", "nohup cargo test > test.log 2>&1 &", C("test", "cargo test", background=True), {}),
    ("bg-amp", "cargo test &", C("test", "cargo test", background=True), {}),
    ("pipefail", "set -o pipefail; cargo test 2>&1 | tee test.log", C("test", "cargo test"), {}),
    ("if-construct", "if cargo test; then echo ok; fi", C("test", "cargo test", masked=True), {}),
    ("negated", "! cargo test", C("test", "cargo test"), {}),
    ("strongest-wins", "npm run lint && npm run build && npm test", C("test", "npm test"), {}),
    ("check-then-build", "cargo clippy && cargo build -p orbit", C("build", "cargo build", True), {}),
    ("for-loop", "for f in a b; do cargo test $f; done", C("test", "cargo test", True), {}),
    ("heredoc-script", "python - <<'EOF'\nprint(1)\nEOF", C("other"), {}),
    ("apply-patch-shell", "apply_patch <<'EOF'\n*** Begin Patch\n*** Delete File: x\n*** End Patch\nEOF", C("other"),
     {}),
    ("comment", "cargo test # all of them", C("test", "cargo test"), {"sameKey": "cargo"}),
    ("quoted-escapes", "pytest \"tests/test q.py\" -k 'a b'", C("test", "pytest", True), {}),
    ("line-continuation", "cargo test \\\n  --workspace", C("test", "cargo test"), {}),
    ("crlf-free-text", "echo hi", C("other"), {"key": key(["echo", "hi"])}),
    ("cat-only", "cat README.md", C("other"), {"key": key(["cat", "README.md"])}),
    ("empty", "", C("other"), {"key": sha16("")}),
    # review fixes
    ("coverage-pytest", "coverage run -m pytest tests/test_queue.py", C("test", "pytest", True), {}),
    ("python-opts-module", "python -X dev -W error -m pytest", C("test", "pytest"), {"sameKey": "pytest"}),
    ("django-python-opts", "python -u manage.py test", C("test", "django test"), {}),
    ("cd-abs-back-into-cwd", "cd /Users/dev/work/orbit/web; cd /Users/dev/work/orbit/core && cargo test",
     C("test", "cargo test"), {"key": key(["@core", "cargo", "test"])}),
    ("cd-sub-then-cwd", "cd web && cd /Users/dev/work/orbit && cargo test", C("test", "cargo test"),
     {"sameKey": "cargo"}),
    ("cd-empty-arg", "cd '' && cargo test", C("test", "cargo test"), {"sameKey": "cargo"}),
    ("cd-only", "cd src", C("other"), {"key": key(["cd", "src"])}),
    ("masked-subshell-after", "cargo test; (echo hi)", C("test", "cargo test", masked=True), {}),
    ("masked-newline-subshell", "cargo test\n(cd a && ls)", C("test", "cargo test", masked=True), {}),
    ("masked-paren-pipe", "(cargo test) | tee log", C("test", "cargo test", masked=True), {}),
    ("masked-brace-pipe", "{ cargo test; } | tee log", C("test", "cargo test", masked=True), {}),
    ("subshell-semicolon-close", "(cd sub; cargo build;) && cargo test", C("test", "cargo test"),
     {"key": key(["@sub", "cargo", "build"], ["cargo", "test"])}),
    ("bg-subshell", "(cargo test) &", C("test", "cargo test", background=True), {}),
    ("assign-with-newline", "FOO='a\nb' cargo test", C("test", "cargo test"), {"sameKey": "cargo"}),
    ("pipefail-plus-o", "set +o pipefail; cargo test | tail", C("test", "cargo test", masked=True), {}),
    ("pipefail-eo", "set -eo pipefail; cargo test | tail", C("test", "cargo test"), {}),
    ("gradle-exclude-eq", "./gradlew build --exclude-task=test", C("build", "gradle build"), {}),
    ("npm-run-w-before-script", "npm run -w web test", C("test", "npm test", True), {}),
    ("pnpm-filter-key", "pnpm --filter web test", C("test", "pnpm test", True),
     {"key": key(["pnpm", "--filter", "web", "test"])}),
    ("prettier-dashdash", "npx prettier --check -- src/a.ts", C("check", "prettier", True), {}),
    ("function-definition", "t() { cargo test; }", C("test", "cargo test"), {}),
    ("crlf-command", "cargo build\r\ncargo test\r\n", C("test", "cargo test", masked=False),
     {"key": key(["cargo", "build"], ["cargo", "test"])}),
    ("windows-cwd-case", "cd c:\\users\\dev\\work\\orbit\\pkg && dotnet test", C("test", "dotnet test"), {}),
]

# `negated`: `!` inverts the exit status; the parser drops it as a leading keyword, so it is not marked masked.


def commands():
    out = []
    for name, text, want, extra in COMMANDS:
        case = {"id": "cmd-" + name, "group": "command", "text": text, "cwd": CWD, "want": dict(want)}
        if "key" in extra:
            case["want"]["key"] = extra["key"]
        if "sameKey" in extra:
            case["sameKeyAs"] = "cmd-" + extra["sameKey"]
        out.append(case)
    return out


# ============================================================================ outcome, result text, sessions

def O(exit_code, failed, t=1_792_500_000_000):
    return {"exitCode": exit_code, "failed": failed, "t": t}


def claude(content, is_error=None):
    block = {"type": "tool_result", "tool_use_id": "toolu_01", "content": content}
    if is_error is not None:
        block["is_error"] = is_error
    return block


def codex(output, kind="function_call_output"):
    return {"type": kind, "call_id": "call_1", "output": output}


def pi(text, is_error=False, details=None, name="bash"):
    msg = {"role": "toolResult", "toolCallId": "call_p1", "toolName": name,
           "content": [{"type": "text", "text": text}], "isError": is_error, "timestamp": 1_792_500_000_000}
    if details is not None:
        msg["details"] = details
    return msg


UNIFIED_RUNNING = json.dumps([{"type": "input_text", "text": "Chunk ID: 4e2a91\nWall time: 10.0021 seconds\nProcess "
                                                             "running with session ID 7\nOriginal token count: 212\n"
                                                             "Output:\n   Compiling orbit v0.3.0\n"}])
UNIFIED_DONE = json.dumps([{"type": "input_text", "text": "Chunk ID: 77b0c3\nWall time: 4.5100 seconds\nProcess exited "
                                                          "with code 101\nOriginal token count: 90\nOutput:\ntest "
                                                          "result: FAILED. 11 passed; 1 failed; 0 ignored; 0 measured; "
                                                          "0 filtered out; finished in 0.02s\n"}])

OUTCOMES = [
    ("claude-ok", "claudeCode", claude("all good"), "Bash", {"command": "cargo test"}, O(None, False)),
    ("claude-exit", "claudeCode", claude("Exit code 101\nerror: test failed, to rerun pass `--lib`", True), "Bash",
     {"command": "cargo test"}, O(101, True)),
    ("claude-blocks", "claudeCode", claude([{"type": "text", "text": "Exit code 1\nFAILED tests/test_q.py::test_a"}],
                                           True), "Bash", {"command": "pytest"}, O(1, True)),
    ("claude-rejected", "claudeCode", claude("The user doesn't want to proceed with this tool use. The tool use was "
                                             "rejected (eg. if it was a file edit, the new_string was NOT written to "
                                             "the file). STOP what you are doing.", True), "Bash",
     {"command": "cargo test"}, O(None, None)),
    ("claude-interrupted", "claudeCode", claude("[Request interrupted by user for tool use]", True), "Bash",
     {"command": "cargo test"}, O(None, None)),
    ("claude-timeout", "claudeCode", claude("Command timed out after 2m 0.0s\n   Compiling orbit v0.3.0", True), "Bash",
     {"command": "cargo test"}, O(None, None)),
    ("claude-background-flag", "claudeCode", claude("Command running in background with ID: bash_3"), "Bash",
     {"command": "cargo test", "run_in_background": True}, O(None, None)),
    ("claude-background-amp", "claudeCode", claude(""), "Bash", {"command": "cargo test > /tmp/t.log 2>&1 &"},
     O(None, None)),
    ("claude-masked-pass", "claudeCode", claude("test result: ok. 12 passed; 0 failed; 0 ignored; 0 measured; "
                                                "0 filtered out"), "Bash",
     {"command": "cargo test 2>&1 | tail -n 3"}, O(None, None)),
    ("claude-masked-fail-exit", "claudeCode", claude("Exit code 1\n", True), "Bash",
     {"command": "cargo test | grep -v warning"}, O(1, None)),
    ("claude-edit-failed", "claudeCode", claude("<tool_use_error>String to replace not found in file.\nString: "
                                                "foo</tool_use_error>", True), "Edit",
     {"file_path": "a.py", "old_string": "foo", "new_string": "bar"}, O(None, True)),
    ("claude-edit-ok", "claudeCode", claude("The file a.py has been updated."), "Edit",
     {"file_path": "a.py", "old_string": "foo", "new_string": "bar"}, O(None, False)),
    ("claude-bashoutput-done", "claudeCode", claude("<status>completed</status>\n\n<exit_code>1</exit_code>\n\n"
                                                    "<stdout>\n1 failed\n</stdout>"), "BashOutput",
     {"bash_id": "bash_3"}, O(1, True)),
    ("claude-bashoutput-running", "claudeCode", claude("<status>running</status>\n\n<stdout>\n...\n</stdout>"),
     "BashOutput", {"bash_id": "bash_3"}, O(None, None)),
    ("claude-no-call", "claudeCode", claude("Exit code 2\nmake: *** [test] Error 2", True), None, None, O(2, True)),
    ("codex-json-meta", "codex", codex('{"output":"ok\\n","metadata":{"exit_code":0,"duration_seconds":0.4}}'),
     "shell", '{"command":["bash","-lc","cargo test"]}', O(0, False)),
    ("codex-json-meta-fail", "codex", codex('{"output":"error[E0308]: mismatched types\\n","metadata":'
                                            '{"exit_code":101,"duration_seconds":2.1}}'),
     "shell", '{"command":["cargo","build"]}', O(101, True)),
    ("codex-text-exit", "codex", codex("Exit code: 1\nWall time: 3.2 seconds\nOutput:\nFAILED tests/test_q.py::test_a"),
     "shell_command", '{"command":"pytest"}', O(1, True)),
    ("codex-text-total-lines", "codex", codex("Exit code: 0\nWall time: 12.4 seconds\nTotal output lines: 2400\n"
                                              "Output:\n...\n"), "shell_command", '{"command":"cargo test"}',
     O(0, False)),
    ("codex-unified-running", "codex", codex(UNIFIED_RUNNING), "exec_command", '{"cmd":"cargo test"}', O(None, None)),
    ("codex-unified-done", "codex", codex(UNIFIED_DONE), "write_stdin", '{"session_id":7,"chars":""}', O(101, True)),
    ("codex-custom-json", "codex", codex('{"chunk_id":"x","wall_time_seconds":3.9,"exit_code":101,'
                                         '"original_token_count":12,"output":"test failed"}', "custom_tool_call_output"),
     "exec", '{"cmd":"cargo test","yield_time_ms":10000}', O(101, True)),
    ("codex-custom-json-running", "codex", codex('{"chunk_id":"y","wall_time_seconds":10.0,"session_id":3,'
                                                 '"output":"Compiling"}', "custom_tool_call_output"),
     "exec", '{"cmd":"cargo test"}', O(None, None)),
    ("codex-timeout", "codex", codex("Exit code: 124\nWall time: 10 seconds\nOutput:\ncommand timed out after 10000 "
                                     "milliseconds\n   Compiling orbit"), "shell_command", '{"command":"cargo test"}',
     O(124, None)),
    ("codex-aborted", "codex", codex("aborted"), "shell_command", '{"command":"cargo test"}', O(None, None)),
    ("codex-patch-success", "codex", codex("Success. Updated the following files:\nM src/queue.rs\n",
                                           "custom_tool_call_output"), "apply_patch", "*** Begin Patch\n*** End Patch",
     O(None, False)),
    ("codex-patch-failed", "codex", codex("apply_patch verification failed: Failed to find expected lines in "
                                          "src/queue.rs", "custom_tool_call_output"), "apply_patch",
     "*** Begin Patch\n*** End Patch", O(None, True)),
    ("codex-plain", "codex", codex("a b"), "exec", "ls", O(None, None)),
    ("codex-masked", "codex", codex("Exit code: 0\nWall time: 9.1 seconds\nOutput:\ntest result: FAILED. 3 passed; "
                                    "1 failed; 0 ignored; 0 measured; 0 filtered out"), "exec_command",
     '{"cmd":"cargo test 2>&1 | tail -n 20"}', O(0, None)),
    ("codex-array-value", "codex", codex([{"type": "input_text", "text": "Exit code: 2\nWall time: 0.1 seconds\n"
                                                                         "Output:\nmake: *** [test] Error 2"}]),
     "shell_command", '{"command":"make test"}', O(2, True)),
    ("pi-fail", "pi", pi("FAIL src/queue.test.ts\n\nCommand exited with code 1", True), "bash",
     {"command": "npm test"}, O(1, True)),
    ("pi-ok", "pi", pi("Tests: 12 passed, 12 total"), "bash", {"command": "npm test"}, O(None, False)),
    ("pi-aborted", "pi", pi("partial\n\nCommand aborted", True), "bash", {"command": "npm test"}, O(None, None)),
    ("pi-timeout", "pi", pi("partial\n\nCommand timed out after 30 seconds", True), "bash", {"command": "npm test"},
     O(None, None)),
    ("pi-read-error", "pi", pi("ENOENT: no such file", True, name="read"), "read", {"path": "a.ts"}, O(None, True)),
    ("omp-details", "omp", pi("error TS2322\n\nCommand exited with code 2", True, {"exitCode": 2}), "bash",
     {"command": "npx tsc --noEmit"}, O(2, True)),
    ("omp-timeout", "omp", pi("partial\n\n[Command timed out after 120 seconds]", True), "bash",
     {"command": "cargo test"}, O(None, None)),
    ("openclaw-nonzero-not-error", "openclaw", pi("1 failed\n\n(Command exited with code 1)", False,
                                                  {"status": "completed", "exitCode": 1}, "exec"), "exec",
     {"command": "pytest -q"}, O(1, True)),
    ("openclaw-ok", "openclaw", pi("12 passed", False, {"status": "completed", "exitCode": 0}, "exec"), "exec",
     {"command": "pytest -q"}, O(0, False)),
    ("openclaw-running", "openclaw", pi("Command still running (session 7, pid 4120). Use process to poll.", False,
                                        {"status": "running"}, "exec"), "exec", {"command": "pytest"},
     O(None, None)),
    ("openclaw-background", "openclaw", pi("started", False, {"status": "completed", "exitCode": 0}, "exec"), "exec",
     {"command": "pytest", "background": True}, O(None, None)),
    ("openclaw-terminated", "openclaw", pi("Command was terminated by SIGTERM", True, None, "exec"), "exec",
     {"command": "pytest"}, O(None, None)),
    ("unknown-source", "gemini", {"output": "x"}, None, None, O(None, None)),
    ("claude-huge-exit", "claudeCode", claude("Exit code 99999999999999999999\nboom", True), "Bash",
     {"command": "make"}, O(999_999_999_999_999_999, True)),
    ("codex-no-output-line", "codex", codex("".join("line %d\n" % n for n in range(9)) + "command timed out after 10 "
                                                                                     "milliseconds\n"),
     "shell_command", '{"command":"make"}', O(None, None)),
    ("codex-jq-array", "codex", codex("[1,2]"), "shell_command", '{"command":"jq .a x.json"}', O(None, None)),
    ("codex-jq-object", "codex", codex('{"session_id":3}'), "shell_command", '{"command":"jq . s.json"}',
     O(None, None)),
    ("codex-running-in-body", "codex", codex("Exit code: 0\nWall time: 0.2 seconds\nOutput:\nProcess running with "
                                             "session ID 7\n"), "shell_command", '{"command":"cat log"}',
     O(0, False)),
    ("codex-nan", "codex", codex("[NaN]"), "shell_command", '{"command":"echo"}', O(None, None)),
    ("codex-meta-not-running", "codex", codex('{"output":"","metadata":{"exit_code":0},"session_id":4}'),
     "exec", '{"cmd":"true"}', O(0, False)),
]


def outcomes():
    out = []
    for name, source, result, tool, value, want in OUTCOMES:
        case = {"id": "out-" + name, "group": "outcome", "source": source, "result": result, "t": want["t"],
                "cwd": CWD, "want": want}
        if tool is not None:
            case["tool"], case["input"] = tool, value
        out.append(case)
    return out


RESULT_TEXTS = [
    ("claude-exit-line", "claudeCode", claude("Exit code 1\nline a\nline b", True), "line a\nline b"),
    ("claude-crlf-blocks", "claudeCode", claude([{"type": "text", "text": "a\r\nb"}, {"type": "image"},
                                                 {"type": "text", "text": "c"}]), "a\nb\nc"),
    ("codex-header", "codex", codex("Exit code: 1\nWall time: 3.2 seconds\nOutput:\nFAILED x\n"), "FAILED x\n"),
    ("codex-unified", "codex", codex(UNIFIED_DONE),
     "test result: FAILED. 11 passed; 1 failed; 0 ignored; 0 measured; 0 filtered out; finished in 0.02s\n"),
    ("codex-json", "codex", codex('{"output":"ok\\nfine","metadata":{"exit_code":0}}'), "ok\nfine"),
    ("codex-no-header", "codex", codex("Output:\nnot a header block"), "not a header block"),
    ("codex-plain", "codex", codex("plain text"), "plain text"),
    ("codex-jq-array", "codex", codex("[1,2]"), "[1,2]"),
    ("codex-header-needs-known-lines", "codex", codex("note: x\nOutput:\nrest"), "note: x\nOutput:\nrest"),
    ("pi-trailing-status", "pi", pi("FAIL x\n\nCommand exited with code 1\n", True), "FAIL x\n"),
    ("openclaw-paren-status", "openclaw", pi("1 failed\n\n(Command exited with code 1)", False, None, "exec"),
     "1 failed\n"),
]


def result_texts():
    return [{"id": "text-" + n, "group": "resultText", "source": s, "result": r, "want": w}
            for n, s, r, w in RESULT_TEXTS]


SESSIONS = [
    ("codex-write-stdin", "codex", "write_stdin", '{"session_id":7,"chars":""}', None, "codex:7", None),
    ("codex-running-text", "codex", "exec_command", '{"cmd":"cargo test"}', codex(UNIFIED_RUNNING), None, "codex:7"),
    ("codex-running-json", "codex", "exec", '{"cmd":"cargo test"}',
     codex('{"wall_time_seconds":10.0,"session_id":3,"output":""}', "custom_tool_call_output"), None, "codex:3"),
    ("codex-finished", "codex", "exec_command", '{"cmd":"cargo test"}', codex(UNIFIED_DONE), None, None),
    ("claude-bashoutput", "claudeCode", "BashOutput", {"bash_id": "bash_3"}, None, "claude:bash_3", None),
    ("claude-taskoutput", "claudeCode", "TaskOutput", {"task_id": "b7x2"}, None, "claude:b7x2", None),
    ("claude-background", "claudeCode", "Bash", {"command": "cargo test", "run_in_background": True},
     claude("Command running in background with ID: bash_3"), None, "claude:bash_3"),
    ("claude-read", "claudeCode", "Read", {"file_path": "a"}, claude("text"), None, None),
    ("codex-running-in-body", "codex", "shell_command", '{"command":"cat log"}',
     codex("Exit code: 0\nWall time: 0.2 seconds\nOutput:\nProcess running with session ID 7\n"), None, None),
    ("codex-meta-exit-session", "codex", "exec", '{"cmd":"true"}',
     codex('{"output":"","metadata":{"exit_code":0},"session_id":4}'), None, None),
    ("claude-background-output-note", "claudeCode", "Bash", {"command": "cargo test", "run_in_background": True},
     claude("Command running in background with ID: bk3x9a. Output is being written to: /tmp/claude/tasks/bk3x9a"
            ".output"), None, "claude:bk3x9a"),
    ("claude-background-dotted-id", "claudeCode", "Bash", {"command": "make", "run_in_background": True},
     claude("Command running in background with ID: job.7."), None, "claude:job.7"),
]


# Claude background-task notices (4.3): queued_command attachments in task-notification mode.
def notice(prompt, mode="task-notification", kind="queued_command"):
    return {"type": "attachment", "timestamp": "2026-10-28T10:00:00.000Z",
            "attachment": {"type": kind, "commandMode": mode, "prompt": prompt}}


def notice_text(summary, status="completed", task="bk3x9a"):
    return ("<task-notification>\n<task-id>%s</task-id>\n<tool-use-id>toolu_01</tool-use-id>\n<output-file>"
            "/tmp/claude/tasks/%s.output</output-file>\n<status>%s</status>\n<summary>%s</summary>\n"
            "</task-notification>" % (task, task, status, summary))


NOTIFICATIONS = [
    ("completed", notice(notice_text('Background command "Run the tests" completed (exit code 0)')),
     {"session": "claude:bk3x9a", "exitCode": 0}),
    ("failed", notice(notice_text('Background command "Build (release)" failed with exit code 101', "failed")),
     {"session": "claude:bk3x9a", "exitCode": 101}),
    ("stopped", notice(notice_text('Background command "Serve" was stopped', "killed")), None),
    ("agent", notice(notice_text('Agent "Review the queue" completed')), None),
    ("monitor", notice("<task-notification>\n<task-id>m1</task-id>\n<summary>Monitor \"logs\": line</summary>\n"
                       "<event>x</event>\n</task-notification>"), None),
    ("other-mode", notice(notice_text('Background command "x" completed (exit code 0)'), mode="prompt"), None),
    ("no-task-id", notice("<summary>Background command \"x\" completed (exit code 0)</summary>"), None),
    ("crlf", notice(notice_text('Background command "x" completed (exit code 2)').replace("\n", "\r\n")),
     {"session": "claude:bk3x9a", "exitCode": 2}),
]


def notifications():
    return [{"id": "notice-" + n, "group": "notification", "source": "claudeCode", "line": line, "want": want}
            for n, line, want in NOTIFICATIONS]


# Codex code mode (4.4): `exec` scripts that call tools.
def script_call(tool, source, value):
    return {"tool": tool, "input": value, "digest": sha16(source)}


def script(calls, prints=None):
    return {"calls": calls, "plain": prints is not None, "prints": prints or []}


SCRIPTS = [
    ("one-print", 'text(await tools.exec_command({cmd: "cargo test", yield_time_ms: 10000}));',
     script([script_call("exec_command", 'tools.exec_command({cmd: "cargo test", yield_time_ms: 10000})',
                         {"cmd": "cargo test", "yield_time_ms": 10000})], [[0, "result"]])),
    ("sequence", "text(await tools.write_stdin({session_id: 7, chars: '', max_output_tokens: 4000}));\n"
                 "await tools.apply_patch(`*** Begin Patch\n*** Add File: a.txt\n+hi\n*** End Patch`)\n"
                 "text(await tools.exec_command({'cmd': \"rg -n \\\"x\\\" src\", workdir: \"/w\",}))",
     script([script_call("write_stdin", "tools.write_stdin({session_id: 7, chars: '', max_output_tokens: 4000})",
                         {"session_id": 7, "chars": "", "max_output_tokens": 4000}),
             script_call("apply_patch", "tools.apply_patch(`*** Begin Patch\n*** Add File: a.txt\n+hi\n*** End Patch`)",
                         "*** Begin Patch\n*** Add File: a.txt\n+hi\n*** End Patch"),
             script_call("exec_command", "tools.exec_command({'cmd': \"rg -n \\\"x\\\" src\", workdir: \"/w\",})",
                         {"cmd": 'rg -n "x" src', "workdir": "/w"})], [[0, "result"], [2, "result"]])),
    ("bound-output", "const r = await tools.exec_command({cmd: 'cargo test', workdir: '/w'});\ntext(r.output);",
     script([script_call("exec_command", "tools.exec_command({cmd: 'cargo test', workdir: '/w'})",
                         {"cmd": "cargo test", "workdir": "/w"})], [[0, "output"]])),
    ("awaited-output", "text((await tools.exec_command({cmd:'ls',yield_time_ms:1})).output);",
     script([script_call("exec_command", "tools.exec_command({cmd:'ls',yield_time_ms:1})",
                         {"cmd": "ls", "yield_time_ms": 1})], [[0, "output"]])),
    ("bound-literal", "const p = `*** Begin Patch\n*** Delete File: a\n*** End Patch`;\nconst c = 'make';\n"
                      "text(await tools.apply_patch(p)); text(await tools.exec_command({cmd: c}));",
     script([script_call("apply_patch", "tools.apply_patch(p)", "*** Begin Patch\n*** Delete File: a\n*** End Patch"),
             script_call("exec_command", "tools.exec_command({cmd: c})", {"cmd": "make"})],
            [[0, "result"], [1, "result"]])),
    ("printed-later", "const a = await tools.exec_command({cmd:'a'}); const b = await tools.exec_command({cmd:'b'});"
                      " text(b.output); text(a);",
     script([script_call("exec_command", "tools.exec_command({cmd:'a'})", {"cmd": "a"}),
             script_call("exec_command", "tools.exec_command({cmd:'b'})", {"cmd": "b"})],
            [[1, "output"], [0, "result"]])),
    ("promise-all", "const r = await Promise.all([tools.exec_command({cmd: 'ls'}), tools.exec_command({cmd: 'pwd'})]);"
                    "\nfor (const x of r) text(x);",
     script([script_call("exec_command", "tools.exec_command({cmd: 'ls'})", {"cmd": "ls"}),
             script_call("exec_command", "tools.exec_command({cmd: 'pwd'})", {"cmd": "pwd"})])),
    ("non-literal", "const c = 'cargo ' + 'test';\ntext(await tools.exec_command({cmd: c, yield_time_ms: 1000}));",
     script([script_call("exec_command", "tools.exec_command({cmd: c, yield_time_ms: 1000})",
                         {"yield_time_ms": 1000})])),
    ("template-opaque", "text(await tools.exec_command({cmd: `cargo test ${name}`}));",
     script([script_call("exec_command", "tools.exec_command({cmd: `cargo test ${name}`})", {})], [[0, "result"]])),
    ("escapes", "text(await tools.exec_command({cmd: \"printf '\\\\u00e9\\\\x41\\\\n'\", x: \"\\u00e9\\x41\\u{1F600}\\\nz\"}))",
     script([script_call(
         "exec_command", "tools.exec_command({cmd: \"printf '\\\\u00e9\\\\x41\\\\n'\", x: \"\\u00e9\\x41\\u{1F600}\\\nz\"})",
         {"cmd": "printf '\\u00e9\\x41\\n'", "x": "\u00e9A\U0001F600z"})], [[0, "result"]])),
    ("regex-and-comments", "// list files\nconst q = x => x.replace(/'/g, \"'\\\\''\"); /* tools.fake() */\n"
                           "text(await tools.exec_command({cmd: 'ls'}));",
     script([script_call("exec_command", "tools.exec_command({cmd: 'ls'})", {"cmd": "ls"})])),
    ("no-tools", "text(1 + 2);", None),
    ("json-object-input", '{"cmd": "ls"}', None),
    ("unterminated", "text(await tools.exec_command({cmd: 'ls}));", None),
    ("member-not-tools", "text(await x.tools.exec_command({cmd: 'ls'}));", None),
    ("no-argument", "text(await tools.list_agents());",
     script([script_call("list_agents", "tools.list_agents()", None)], [[0, "result"]])),
]


def scripts():
    return [{"id": "script-" + n, "group": "codeScript", "source": "codex", "tool": "exec", "input": text,
             "want": want} for n, text, want in SCRIPTS]


def items_result(texts, kind="custom_tool_call_output"):
    return {"type": kind, "call_id": "c1", "output": [{"type": "input_text", "text": t} for t in texts]}


CODE_ITEMS = [
    ("completed", items_result(["Script completed\nWall time 1.2 seconds\nOutput:\n", '{"exit_code":0,"output":"ok"}']),
     ['{"exit_code":0,"output":"ok"}']),
    ("crlf-header", items_result(["Script completed\r\nWall time 1.2 seconds\r\nOutput:\r\n", "a\r\nb"]),
     ["a\nb"]),
    ("failed", items_result(["Script failed\nWall time 0.1 seconds\nOutput:\n", "x"]), None),
    ("running", {"type": "custom_tool_call_output", "output": "Script running with cell ID 3\nWall time 10.0 seconds"},
     None),
    ("image-item", {"type": "custom_tool_call_output", "output": [
        {"type": "input_text", "text": "Script completed\nOutput:\n"}, {"type": "input_image", "image_url": "x"},
        {"type": "input_text", "text": "{}"}]}, [None, "{}"]),
]


def code_items_cases():
    return [{"id": "items-" + n, "group": "codeItems", "source": "codex", "result": result, "want": want}
            for n, result, want in CODE_ITEMS]


def sessions():
    out = []
    for name, source, tool, value, result, cont, running in SESSIONS:
        case = {"id": "session-" + name, "group": "session", "source": source, "tool": tool, "input": value,
                "want": {"continuation": cont, "running": running}}
        if result is not None:
            case["result"] = result
        out.append(case)
    return out


# ============================================================================ edit_hunks

QUEUE_PATCH = """*** Begin Patch
*** Update File: src/queue.py
@@ class Queue:
     def push(self, x):
-        self.items.append(x)
+        self.items.insert(0, x)
@@ def pop(self):
-        return self.items.pop()
+        if not self.items:
+            return None
+        return self.items.pop()
*** Add File: src/empty.py
+\"\"\"Empty module.\"\"\"
+
*** Delete File: src/old.py
*** Update File: src/name.py
*** Move to: src/names.py
@@
 A = 1
-B = 2
+B = 3
*** End of File
*** End Patch
"""
QUEUE_PATCH_WANT = [
    H("src/queue.py", "        self.items.append(x)", "        self.items.insert(0, x)"),
    H("src/queue.py", "", "        if not self.items:\n            return None"),
    H("src/empty.py", None, '"""Empty module."""'),
    H("src/old.py", None, ""),
    H("src/name.py", None, ""),
    H("src/names.py", "B = 2", "B = 3"),
]
PUSH = H("src/queue.py", "        self.items.append(x)", "        self.items.insert(0, x)")
PUSH_REVERT = H("src/queue.py", "        self.items.insert(0, x)", "        self.items.append(x)")

EDITS = [
    # Claude
    ("claude-edit", "claudeCode", "Edit",
     {"file_path": "/Users/dev/work/orbit/src/queue.py",
      "old_string": "    def push(self, x):\n        self.items.append(x)\n",
      "new_string": "    def push(self, x):\n        self.items.insert(0, x)\n"}, [PUSH]),
    ("claude-edit-revert-more-context", "claudeCode", "Edit",
     {"file_path": "src/queue.py",
      "old_string": "        self.items.insert(0, x)\n\n    def pop(self):",
      "new_string": "        self.items.append(x)\n\n    def pop(self):"}, [PUSH_REVERT]),
    ("claude-edit-crlf-trailing-space", "claudeCode", "Edit",
     {"file_path": "src\\cfg.py", "old_string": "a = 1  \r\nb = 2\r\n", "new_string": "a = 1\r\nb = 3\r\n"},
     [H("src/cfg.py", "b = 2", "b = 3")]),
    ("claude-edit-blank-line-only", "claudeCode", "Edit",
     {"file_path": "src/cfg.py", "old_string": "x = 1\n\ny = 2", "new_string": "x = 1\ny = 2"},
     [H("src/cfg.py", "", "")]),
    ("claude-edit-partial-line", "claudeCode", "Edit",
     {"file_path": "src/math.py", "old_string": "a + b", "new_string": "a - b", "replace_all": True},
     [H("src/math.py", "a + b", "a - b")]),
    ("claude-multiedit", "claudeCode", "MultiEdit",
     {"file_path": "/Users/dev/work/orbit/src/queue.py", "edits": [
         {"old_string": "import os\n", "new_string": ""},
         {"old_string": "MAX = 10", "new_string": "MAX = 20", "replace_all": True}]},
     [H("src/queue.py", "import os", ""), H("src/queue.py", "MAX = 10", "MAX = 20")]),
    ("claude-multiedit-empty", "claudeCode", "MultiEdit", {"file_path": "src/queue.py", "edits": []}, []),
    ("claude-multiedit-not-list", "claudeCode", "MultiEdit", {"file_path": "src/queue.py", "edits": 5}, []),
    ("claude-multiedit-bad-entry", "claudeCode", "MultiEdit",
     {"file_path": "src/queue.py", "edits": [{"old_string": "a"}, {"old_string": "b", "new_string": "c"}]},
     [H("src/queue.py", "b", "c")]),
    ("claude-write", "claudeCode", "Write",
     {"file_path": "/Users/dev/work/orbit/src/hello.py", "content": "print('hi')\n"},
     [H("src/hello.py", None, "print('hi')")]),
    ("claude-write-crlf", "claudeCode", "Write",
     {"file_path": "/Users/dev/other/notes.md", "content": "\r\n# Notes \r\n\r\ntext\r\n\r\n"},
     [H("/Users/dev/other/notes.md", None, "# Notes\n\ntext")]),
    ("claude-write-empty", "claudeCode", "Write", {"file_path": "src/__init__.py", "content": ""},
     [H("src/__init__.py", None, "")]),
    ("claude-notebook-replace", "claudeCode", "NotebookEdit",
     {"notebook_path": "/Users/dev/work/orbit/nb/explore.ipynb", "cell_id": "c3", "new_source": "df.head()\n"},
     [H("nb/explore.ipynb#c3", None, "df.head()")]),
    ("claude-notebook-insert", "claudeCode", "NotebookEdit",
     {"notebook_path": "nb/explore.ipynb", "cell_id": "c3", "new_source": "import pandas as pd",
      "edit_mode": "insert", "cell_type": "code"},
     [H("nb/explore.ipynb", "", "import pandas as pd")]),
    ("claude-notebook-delete", "claudeCode", "NotebookEdit",
     {"notebook_path": "nb/explore.ipynb", "cell_id": "c4", "new_source": "", "edit_mode": "delete"},
     [H("nb/explore.ipynb#c4", None, "")]),
    ("claude-notebook-cell-number", "claudeCode", "NotebookEdit",
     {"notebook_path": "nb/explore.ipynb", "cell_number": 2, "new_source": "x = 1"},
     [H("nb/explore.ipynb#2", None, "x = 1")]),
    # Codex
    ("codex-custom-patch", "codex", "apply_patch", QUEUE_PATCH, QUEUE_PATCH_WANT),
    ("codex-function-patch", "codex", "apply_patch", json.dumps({"input": QUEUE_PATCH}), QUEUE_PATCH_WANT),
    ("codex-patch-crlf-no-begin", "codex", "apply_patch",
     "*** Update File: src/queue.py\r\n@@\r\n     def push(self, x):\r\n-        self.items.append(x)\r\n"
     "+        self.items.insert(0, x)\r\n", [PUSH]),
    ("codex-shell-argv", "codex", "shell",
     json.dumps({"command": ["apply_patch", "*** Begin Patch\n*** Update File: src/queue.py\n@@\n-        "
                             "self.items.insert(0, x)\n+        self.items.append(x)\n*** End Patch\n"],
                 "workdir": CWD}), [PUSH_REVERT]),
    ("codex-shell-heredoc", "codex", "shell",
     json.dumps({"command": ["bash", "-lc", "apply_patch <<'EOF'\n*** Begin Patch\n*** Delete File: src/old.py\n"
                                            "*** End Patch\nEOF\n"]}), [H("src/old.py", None, "")]),
    ("codex-exec-command-heredoc", "codex", "exec_command",
     json.dumps({"cmd": "cd /Users/dev/work/orbit && apply_patch <<'PATCH'\n*** Begin Patch\n*** Add File: "
                        "docs/a.md\n+# A\n*** End Patch\nPATCH"}), [H("docs/a.md", None, "# A")]),
    ("codex-shell-not-patch", "codex", "shell", '{"command":["cargo","test"]}', []),
    ("codex-exec-crlf-heredoc", "codex", "exec_command",
     json.dumps({"cmd": "apply_patch <<'EOF'\r\n*** Begin Patch\r\n*** Delete File: src/old.py\r\n*** End Patch"
                        "\r\nEOF\r\n"}), [H("src/old.py", None, "")]),
    ("codex-patch-empty-input-uses-patch", "codex", "apply_patch",
     json.dumps({"input": "", "patch": "*** Begin Patch\n*** Delete File: a.txt\n*** End Patch"}), []),
    # Pi
    ("pi-edit", "pi", "edit",
     {"path": "src/queue.ts", "edits": [{"oldText": "items.push(x);", "newText": "items.unshift(x);"},
                                        {"oldText": "const MAX = 10;", "newText": "const MAX = 20;"}]},
     [H("src/queue.ts", "items.push(x);", "items.unshift(x);"), H("src/queue.ts", "const MAX = 10;",
                                                                    "const MAX = 20;")]),
    ("pi-edit-legacy", "pi", "edit", {"path": "src/queue.ts", "oldText": "a", "newText": "b"},
     [H("src/queue.ts", "a", "b")]),
    ("pi-edit-json-string", "pi", "edit",
     {"path": "/Users/dev/work/orbit/src/queue.ts", "edits": '[{"oldText":"a","newText":"b"}]'},
     [H("src/queue.ts", "a", "b")]),
    ("pi-edit-single-object", "pi", "edit", {"path": "src/queue.ts", "edits": {"oldText": "c", "newText": "d"}},
     [H("src/queue.ts", "c", "d")]),
    ("pi-write", "pi", "write", {"path": "src/new.ts", "content": "export {};\n"},
     [H("src/new.ts", None, "export {};")]),
    ("pi-bash-not-edit", "pi", "bash", {"command": "npm test"}, []),
    ("pi-write-dot-segments", "pi", "write", {"path": "./src/././a.ts", "content": "a"}, [H("src/a.ts", None, "a")]),
    # omp
    ("omp-edit-replace", "omp", "edit",
     {"path": "src/queue.ts", "old_string": "items.unshift(x);", "new_string": "items.push(x);"},
     [H("src/queue.ts", "items.unshift(x);", "items.push(x);")]),
    ("omp-edit-hashline", "omp", "edit",
     {"input": "*** Begin Patch\n[src/queue.ts#a1f3]\nPUT 3.=3:\n+const MAX = 30;\nPUT >12:\n+  // drained\n+\nCUT "
               "20.=22\n[src/old.ts#09bc]\nREM\n[src/a.ts#77aa]\nMV src/b.ts\n*** End Patch"},
     [H("src/queue.ts", "", "  // drained"), H("src/old.ts", None, ""), H("src/a.ts", None, "")]),
    ("omp-edit-apply-patch-mode", "omp", "edit",
     {"input": "*** Begin Patch\n*** Update File: src/queue.py\n@@\n-        self.items.append(x)\n+        "
               "self.items.insert(0, x)\n*** End Patch"}, [PUSH]),
    ("omp-edit-patch-mode", "omp", "edit",
     {"path": "src/queue.py", "edits": [{"diff": "@@\n     def push(self, x):\n-        self.items.insert(0, x)\n+  "
                                                  "      self.items.append(x)\n"},
                                        {"op": "create", "diff": "+x = 1\n+y = 2\n"}]},
     [PUSH_REVERT, H("src/queue.py", None, "x = 1\ny = 2")]),
    ("omp-edit-sloppy", "omp", "edit", {"input": "*** Edit File: src/queue.ts\nsomething"}, []),
    ("omp-write", "omp", "write", {"path": "src/z.ts", "content": "z\n"}, [H("src/z.ts", None, "z")]),
    ("omp-ast-edit", "omp", "ast_edit", {"pattern": "a", "rewrite": "b"}, []),
    # OpenClaw
    ("openclaw-edit", "openclaw", "edit",
     {"path": "src/queue.ts", "edits": [{"oldText": "a", "newText": "b"}], "oldText": "a", "newText": "b"},
     [H("src/queue.ts", "a", "b")]),
    ("openclaw-write", "openclaw", "write", {"path": "/Users/dev/work/orbit/README.md", "content": "# Orbit\n"},
     [H("README.md", None, "# Orbit")]),
    ("openclaw-apply-patch", "openclaw", "apply_patch",
     {"input": "*** Begin Patch\n*** Update File: src/queue.py\n@@\n-        self.items.append(x)\n+        "
               "self.items.insert(0, x)\n*** End Patch"}, [PUSH]),
    ("openclaw-exec-patch", "openclaw", "exec",
     {"command": "apply_patch <<'EOF'\n*** Begin Patch\n*** Delete File: src/old.py\n*** End Patch\nEOF"},
     [H("src/old.py", None, "")]),
]


def edits():
    return [{"id": "edit-" + n, "group": "edits", "source": s, "tool": t, "input": v, "cwd": CWD, "want": w}
            for n, s, t, v, w in EDITS]


# ============================================================================ failures

# Where the reference's documented rules differ from the corpus author's own reading (spec 5.5):
OVERRIDES = {
    # failed = failed + errors (a collection error is a failure)
    "pytest-param-collection-error": {"failedCount": 3},
    # no summary line: failedCount = distinct failures found
    "swiftc-build-swift6-duplicated": {"failedCount": 2},
    "swiftc-classic-warnings-errors": {"failedCount": 2},
    "swiftc-swift-test-compile-fail": {"failedCount": 2},
    "xcodebuild-xcode16-parallel-fail-tail": {"failedCount": 2},
    "go-build-errors": {"failedCount": 3},
    "go-vet-findings": {"failedCount": 3},
    "cc-gcc-make-utf8-quotes": {"failedCount": 2},
    "cc-gnu-ld-undefined-reference": {"failedCount": 2},
    "cc-macos-ld-undefined-symbols": {"failedCount": 2},
    "tsc-plain-three-errors": {"failedCount": 3},
    "tsc-windows-crlf-multiline": {"failedCount": 3},
    "tsc-head-c-cut-mid-line": {"failedCount": 4},
    "gradle-kotlin-compile-errors": {"failedCount": 2},
    "generic-node-typeerror-crash": {"failedCount": 1},
    "generic-python-module-not-found": {"failedCount": 1},
    "generic-make-script-error": {"failedCount": 1},
    # go: failing tests plus packages that did not build; passes = `--- PASS` lines, else `ok` packages
    "go-test-all-mixed": {"failedCount": 5, "passedCount": 1},
    "go-test-verbose-run": {"failedCount": 2, "passedCount": 4},
    # a suite that fails to load counts once when no test failed
    "jest-suite-failed-to-run-syntax": {"failedCount": 1},
    "vitest-suite-import-failure": {"failedCount": 1},
    # rspec: errors outside examples are failures
    "rspec-error-outside-examples": {"failedCount": 1},
    # a clean completion marker (BUILD SUCCESSFUL) reads as zero failures (5.4)
    "gradle-build-successful": {"failedCount": 0, "framework": "gradle"},
}


def output_cases():
    out = []
    for c in OUTPUTS:
        failed = None if "|" in c["command"] else c["exitCode"] != 0
        want = {"failedCount": c["expect"]["failedCount"], "passedCount": c["expect"]["passedCount"]}
        want.update(OVERRIDES.get(c["id"], {}))
        out.append({"id": "fail-" + c["id"], "group": "failures", "command": c["command"], "output": c["output"],
                    "failed": failed, "want": want, "author": c["expect"], "exitCode": c["exitCode"]})
    return out


def budget_output():
    head = ("   Compiling orbit v0.3.0 (/Users/dev/work/orbit)\nerror[E0308]: mismatched types\n --> src/a.rs:3:5\n"
            "  |\n")
    filler = "".join("   Compiling crate-été-%05d v0.1.%d\n" % (n, n % 7) for n in range(9000))
    middle = "error[E0425]: cannot find value `gone` in this scope\n --> src/mid.rs:9:9\n"
    tail = "error: could not compile `orbit` (lib) due to 2 previous errors\n"
    return head + filler[:150_000] + middle + filler[150_000:] + tail


EXTRA_FAILURES = [
    ("budget-head-tail", "cargo build", budget_output(), True, None,
     {"failedCount": 2, "passedCount": None, "framework": "rustc",
      "canonical": ["rustc|error|src/a.rs|E0308|mismatched types"]}),
    ("cwd-relative", "swift build",
     "/Users/dev/work/orbit/Sources/Orbit/Queue.swift:14:9: error: cannot find 'x' in scope\n", True, CWD,
     {"canonical": ["swiftc|error|Sources/Orbit/Queue.swift||cannot find 'x' in scope"], "failedCount": 1}),
    ("mask-volatile", "make",
     "src/io.c:40:3: error: timeout after 2.5s at 2026-10-28T09:12:44.120Z in /var/folders/x1/ab_c/T/orbit-77/sock "
     "(addr 0x7ffee4b2a8c0, id 9f86d081884c7d65, pid 4120, line 88)\n", True, None,
     {"canonical": ["cc|error|src/io.c||timeout after <dur> at <time> in <tmp> (addr 0x?, id <hex>, pid N, line N)"]}),
    ("generic-needs-failed", "node scripts/migrate.js", "TypeError: Cannot read properties of undefined\n", False,
     None, {"fingerprints": [], "failedCount": None, "framework": None}),
    ("generic-unknown-outcome", "node scripts/migrate.js", "TypeError: Cannot read properties of undefined\n", None,
     None, {"fingerprints": [], "failedCount": None}),
    ("generic-file-line", "./build.sh", "lib/x.zig:3:7: error: expected type 'u8'\n", True, None,
     {"canonical": ["generic|error|lib/x.zig||expected type 'u8'"]}),
    ("turbo-prefixes", "npx turbo test",
     "@orbit/queue:test:  FAIL  src/queue.test.ts\n@orbit/queue:test:   ● Queue › push › grows\n"
     "@orbit/queue:test: \n@orbit/queue:test: Tests:       1 failed, 3 passed, 4 total\n", True, None,
     {"canonical": ["jest|test|src/queue.test.ts|Queue › push › grows|"], "failedCount": 1,
      "passedCount": 3}),
    ("nextest-flaky-retry", "cargo nextest run --retries 2",
     "    Starting 3 tests across 1 binary\n   TRY 1 FAIL [   0.010s] orbit sync::tests::reconnects\n"
     "   TRY 2 PASS [   0.011s] orbit sync::tests::reconnects\n        PASS [   0.002s] orbit queue::tests::push\n"
     "        PASS [   0.002s] orbit queue::tests::pop\n     Summary [   0.030s] 3 tests run: 3 passed (1 flaky), "
     "0 skipped\n", False, None, {"canonical": [], "failedCount": 0, "passedCount": 3, "framework": "nextest"}),
    ("dotnet-mtp", "dotnet test",
     "failed Push_Grows (12ms)\n  Assert.Equal() Failure\nTest summary: total: 12, failed: 1, succeeded: 11, "
     "skipped: 0, duration: 1.2s\n", True, None,
     {"canonical": ["dotnet|test||Push_Grows|"], "failedCount": 1, "passedCount": 11}),
    ("xctest-skipped", "swift test",
     "Test Suite 'All tests' passed at 2026-10-28 09:00:00.120.\n\t Executed 5 tests, with 1 test skipped and 0 "
     "failures (0 unexpected) in 0.010 (0.012) seconds\n", False, None,
     {"canonical": [], "failedCount": 0, "passedCount": 4}),
    ("phpunit-skipped-not-failures", "vendor/bin/phpunit",
     "There was 1 skipped test:\n\n1) Tests\\QueueTest::testSlow\nslow\n\nOK, but incomplete, skipped, or risky "
     "tests!\nTests: 4, Assertions: 6, Skipped: 1.\n", False, None,
     {"canonical": [], "failedCount": 0, "passedCount": 3}),
    ("progress-carriage-returns", "cargo test",
     "   Compiling a\r   Compiling b\rtest queue::push ... FAILED\r\ntest result: FAILED. 0 passed; 1 failed; 0 "
     "ignored; 0 measured; 0 filtered out; finished in 0.00s\r\n", True, None,
     {"canonical": ["cargo|test||queue::push|"], "failedCount": 1, "passedCount": 0}),
    ("ansi-osc-controls", "pytest",
     "\x1b]8;;file:///x\x1b\\\x1b[31mFAILED\x1b[0m tests/test_q.py::test_a - assert 1 == 2\x07\n\x1b[31m=== 1 failed in "
     "0.10s ===\x1b[0m\n", True, None,
     {"canonical": ["pytest|test|tests/test_q.py|test_a|"], "failedCount": 1, "passedCount": 0}),
    ("compile-before-tests-dotnet", "dotnet test",
     "  Determining projects to restore...\n/Users/dev/work/orbit/tests/QueueTests.cs(9,13): error CS0246: The type "
     "or namespace name 'Queu' could not be found (are you missing a using directive or an assembly reference?) "
     "[/Users/dev/work/orbit/tests/Orbit.Tests.csproj]\n\nBuild FAILED.\n    0 Warning(s)\n    1 Error(s)\n", True,
     CWD, {"failedCount": 1, "passedCount": None, "framework": "msbuild",
           "canonical": ["msbuild|error|tests/QueueTests.cs|CS0246|The type or namespace name 'Queu' could not be "
                         "found (are you missing a using directive or an assembly reference?)"]}),
    ("cwd-not-mid-path", "make",
     "src/io.c:3:1: error: cannot open /opt/mirror/Users/dev/work/orbit/data.txt or /usr/tmp/x\n", True, CWD,
     {"canonical": ["cc|error|src/io.c||cannot open /opt/mirror/Users/dev/work/orbit/data.txt or /usr/tmp/x"]}),
    ("xcodebuild-count-needs-header", "xcodebuild build", "(3 failures)\n", True, None,
     {"fingerprints": [], "failedCount": None}),
    ("workspace-prefix-needs-space", "pnpm -r test",
     "a/b test:zz 1\na/b test:zz 2\na/b test:zz 3\n=== 1 failed in 0.1s ===\n", True, None,
     {"failedCount": 1, "passedCount": 0, "framework": "pytest"}),
    ("cap-256", "cargo test",
     "".join("test t%03d ... FAILED\n" % n for n in range(300)) + "test result: FAILED. 0 passed; 300 failed; 0 "
     "ignored; 0 measured; 0 filtered out; finished in 0.01s\n", True, None,
     {"failedCount": 300, "passedCount": 0, "fingerprintCount": 256}),
]

# Completion markers (5.4): a tool that ends cleanly reports zero failures, even when a pipe hides its exit code.
EXTRA_FAILURES += [
    ("complete-cargo", "cargo build 2>&1 | tail -3",
     "   Compiling orbit v0.3.0 (/Users/dev/work/orbit)\n    Finished `dev` profile [unoptimized + debuginfo] "
     "target(s) in 2.31s\n", None, None,
     {"fingerprints": [], "failedCount": 0, "passedCount": None, "framework": "cargo"}),
    ("complete-cargo-old", "cargo clippy 2>&1 | tail -2",
     "warning: `orbit` (lib) generated 1 warning\n    Finished dev [unoptimized + debuginfo] target(s) in 0.42s\n",
     None, None, {"failedCount": 0, "framework": "cargo"}),
    ("complete-swiftpm", "swift build 2>&1 | tail -2", "[12/12] Linking orbit\nBuild complete! (8.04s)\n", None,
     None, {"failedCount": 0, "passedCount": None, "framework": "swiftpm"}),
    ("complete-xcodebuild", "xcodebuild -scheme Orbit build | tail -1", "** BUILD SUCCEEDED **\n", None, None,
     {"failedCount": 0, "framework": "xcodebuild"}),
    ("complete-maven", "mvn -q package | tail -3", "[INFO] BUILD SUCCESS\n[INFO] Total time:  4.210 s\n", None,
     None, {"failedCount": 0, "framework": "maven"}),
    ("complete-prettier", "npx prettier --check src | tail -1",
     "Checking formatting...\nAll matched files use Prettier code style!\n", None, None,
     {"failedCount": 0, "framework": "prettier"}),
    ("complete-vite", "npm run build 2>&1 | tail -2",
     "dist/assets/index-4f2a.js  142.10 kB\n\u2713 built in 2.13s\n", None, None,
     {"failedCount": 0, "framework": "vite"}),
    ("complete-loses-to-errors", "cargo build 2>&1 | tail",
     "error[E0425]: cannot find value `x` in this scope\n --> src/main.rs:2:5\nerror: could not compile `orbit`\n",
     None, None, {"failedCount": 1, "framework": "rustc"}),
    ("complete-not-a-prefix", "cargo build 2>&1 | tail -1", "    Finished early, see log\n", None, None,
     {"failedCount": None, "framework": None}),
]


def extra_failures():
    return [{"id": "fail-x-" + n, "group": "failures", "command": cmd, "output": text, "failed": failed, "cwd": cwd,
             "want": want} for n, cmd, text, failed, cwd, want in EXTRA_FAILURES]


# ============================================================================ checks

def last_token(name):
    import re
    tokens = [t for t in re.split(r"[ :./›>\[\]()\-\"']+", name) if t and not t.isdigit()]
    return tokens[-1] if tokens else name


def hand_check(case, got):
    want = case["want"]
    problems = []
    if case["group"] in ("actionKind", "resultText", "notification", "codeScript", "codeItems"):
        if got != want:
            problems.append("expected %r got %r" % (want, got))
        return problems
    if case["group"] == "edits":
        if got != want:
            problems.append("expected %r got %r" % (want, got))
        return problems
    for k, v in want.items():
        if k == "fingerprintCount":
            if len(got["fingerprints"]) != v:
                problems.append("fingerprints: expected %d got %d" % (v, len(got["fingerprints"])))
        elif got.get(k) != v:
            problems.append("%s: expected %r got %r" % (k, v, got.get(k)))
    if case["group"] == "failures" and "author" in case:
        author = case["author"]
        canon = got["canonical"]
        if len(canon) != len(author["failingTests"]) + len(author["errors"]):
            problems.append("expected %d failures, found %r" % (len(author["failingTests"]) + len(author["errors"]),
                                                                canon))
        for test in author["failingTests"]:
            token = last_token(test)
            if not any(token in c for c in canon):
                problems.append("failing test %r not found" % test)
        for error in author["errors"]:
            if error.get("code") and not any("|" + error["code"] + "|" in c or error["code"] in c for c in canon):
                problems.append("error code %r not found" % error["code"])
        if got["fingerprints"] != sorted({sha16(c) for c in canon})[:256]:
            problems.append("fingerprints do not match the canonical strings")
    return problems


def check_relations(cases, outputs):
    """Cross-case expectations: commands that must share a key."""
    problems = []
    for case in cases:
        other = case.get("sameKeyAs")
        if other and outputs[case["id"]]["key"] != outputs[other]["key"]:
            problems.append("%s: key differs from %s" % (case["id"], other))
    return problems


def cases():
    return kinds() + commands() + outcomes() + result_texts() + sessions() + notifications() + scripts() + \
        code_items_cases() + edits() + output_cases() + extra_failures()
