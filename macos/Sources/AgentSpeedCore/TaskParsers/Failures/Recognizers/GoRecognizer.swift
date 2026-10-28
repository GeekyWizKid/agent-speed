import Foundation

/// go test, go build and go vet (TASK-PARSERS 5.3).
final class GoRecognizer: LineRecognizer {
    private static let fail = Pattern(#"[ \t]*--- FAIL: ([^ ]+) \([0-9.]+s\)"#)
    private static let pass = Pattern(#"[ \t]*--- (PASS|SKIP): ([^ ]+) \([0-9.]+s\)"#)
    private static let run = Pattern(#"=== (?:RUN|PAUSE|CONT|NAME) .*"#)
    private static let package = Pattern(#"(FAIL|ok)[ \t]+([^ \t]+)[ \t]+(?:[0-9.]+s|\(cached\)|\[([^\]]+)\])(?:[ \t].*)?"#)
    private static let bare = Pattern(#"(?:FAIL|PASS|\?[ \t]+[^ \t]+[ \t]+\[no test files\])"#)
    private static let error = Pattern(#"([^ \t][^ \t:]*\.go):([0-9]+)(?::([0-9]+))?: (.+)"#)
    private static let header = Pattern(#"# [^ ]+(?: \[[^\]]+\])?"#)
    private static let brokenStates = TaskText.words(["build failed", "setup failed"])

    private let found: FailureCollector
    private var failed = Set<Bytes>()
    private var passed = Set<Bytes>()
    private var ok: Int64 = 0
    private var packages: Int64 = 0
    private var broken: Int64 = 0

    init(_ found: FailureCollector) {
        self.found = found
    }

    func step(_ lines: [TextLine], _ i: Int) -> Int {
        let line = lines[i]
        if let match = Self.fail.fullMatch(line) {
            failed.insert(match.text(1))
            found.test("go", match.text(1))
            return 1
        }
        if let match = Self.pass.fullMatch(line) {
            if match.text(1) == Bytes(utf8: "PASS") { passed.insert(match.text(2)) }
            return 1
        }
        if Self.run.fullMatches(line) || Self.bare.fullMatches(line) || Self.header.fullMatches(line) { return 1 }
        if let match = Self.package.fullMatch(line) {
            packages += 1
            if match.text(1) == Bytes(utf8: "ok") {
                ok += 1
            } else if let state = match[3], Self.brokenStates.contains(state) {
                broken += 1
            }
            return 1
        }
        if let match = Self.error.fullMatch(line) {
            found.error("go", file: match.text(1), code: [], message: match.text(4))
            return 1
        }
        return 0
    }

    func finish() {
        guard packages > 0 || !failed.isEmpty || !passed.isEmpty else { return }
        let passes: Int64? = !passed.isEmpty ? Int64(passed.count) : (ok > 0 ? ok : nil)
        found.summary("go", .test, failed: Int64(failed.count) + broken, passed: passes)
    }
}
