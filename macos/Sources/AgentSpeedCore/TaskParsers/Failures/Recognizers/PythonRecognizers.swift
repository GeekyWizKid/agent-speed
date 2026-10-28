import Foundation

/// pytest (TASK-PARSERS 5.3).
final class PytestRecognizer: LineRecognizer {
    private static let fail = Pattern(#"(?:\[gw[0-9]+\] \[ *[0-9]+%\] )?(FAILED|ERROR) (?!\()([^ ]+?)(?: - (.*))?"#)
    private static let verbose = Pattern(#"([^ ]+::.+?) (FAILED|ERROR)(?: +\[ *[0-9]+%\])?"#)
    private static let summary = Pattern(#"(?:=+ )?((?:[0-9]+ (?:failed|passed|errors?|skipped|xfailed|xpassed|deselected|warnings?|rerun|reruns|subtests passed|subtests failed)(?:, )?)+) in [0-9]+(?:\.[0-9]+)?s(?: \([0-9:.]+\))?(?: =+)?"#)
    private static let none = Pattern(#"(?:=+ )?no tests ran in [0-9]+(?:\.[0-9]+)?s(?: =+)?"#)
    private static let separator = Bytes(utf8: "::")

    private let found: FailureCollector

    init(_ found: FailureCollector) {
        self.found = found
    }

    func step(_ lines: [TextLine], _ i: Int) -> Int {
        let line = lines[i]
        var nodeId: Bytes?
        if let match = Self.fail.fullMatch(line) {
            nodeId = match.text(2)
        } else if let match = Self.verbose.fullMatch(line) {
            nodeId = match.text(1)
        }
        if let nodeId {
            if let split = nodeId.firstRange(of: Self.separator) {
                found.test("pytest", Bytes(nodeId[(split + 2)...]), file: Bytes(nodeId[..<split]))
            } else {
                found.test("pytest", [], file: nodeId)
            }
            return 1
        }
        if let match = Self.summary.fullMatch(line) {
            let counts = FailureCounts.parse(match.text(1))
            found.summary("pytest", .test, failed: FailureCounts.sum(counts, "failed", "error", "errors"),
                          passed: counts[Bytes(utf8: "passed")] ?? 0)
            return 1
        }
        if Self.none.fullMatches(line) {
            found.summary("pytest", .test, failed: 0, passed: 0)
            return 1
        }
        return 0
    }
}

/// unittest and Django (TASK-PARSERS 5.3).
final class UnittestRecognizer: LineRecognizer {
    private static let fail = Pattern(#"(FAIL|ERROR|UNEXPECTED SUCCESS): ([A-Za-z_][A-Za-z0-9_]*) \(([A-Za-z0-9_.]+)\)(?: .*)?"#)
    private static let ran = Pattern(#"Ran ([0-9]+) tests? in [0-9]+(?:\.[0-9]+)?s"#)
    private static let ok = Pattern(#"OK(?: \((.*)\))?"#)
    private static let failed = Pattern(#"FAILED \((.*)\)"#)
    private static let pair = Pattern(#"[ \t]*([a-z ]+)=([0-9]+)[ \t]*"#)

    private let found: FailureCollector
    private var ran: Int64?

    init(_ found: FailureCollector) {
        self.found = found
    }

    func step(_ lines: [TextLine], _ i: Int) -> Int {
        let line = lines[i]
        if let match = Self.fail.fullMatch(line) {
            let name = match.text(2)
            let location = match.text(3)
            found.test("unittest", location.hasSuffix([Byte.dot] + name) ? location : location + [Byte.dot] + name)
            return 1
        }
        if let match = Self.ran.fullMatch(line) {
            ran = TaskText.number(match.text(1))
            return 1
        }
        if let total = ran, let match = Self.ok.fullMatch(line) ?? Self.failed.fullMatch(line) {
            var counts: [Bytes: Int64] = [:]
            for part in match.text(1).split(byte: 0x2C) {
                if let pair = Self.pair.fullMatch(part) { counts[pair.text(1)] = TaskText.number(pair.text(2)) }
            }
            let failed = FailureCounts.sum(counts, "failures", "errors", "unexpected successes")
            let passed = TaskText.subtract(TaskText.subtract(TaskText.subtract(total, failed),
                                                             counts[Bytes(utf8: "skipped")] ?? 0),
                                           counts[Bytes(utf8: "expected failures")] ?? 0)
            found.summary("unittest", .test, failed: failed, passed: max(0, passed))
            ran = nil
            return 1
        }
        return 0
    }
}
