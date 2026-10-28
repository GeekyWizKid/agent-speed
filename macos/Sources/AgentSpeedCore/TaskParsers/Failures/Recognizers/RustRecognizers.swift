import Foundation

/// cargo test: libtest and nextest (TASK-PARSERS 5.3).
final class CargoRecognizer: LineRecognizer {
    private static let line = Pattern(#"test (.+) \.\.\. (FAILED|ok|ignored(?:, .*)?|bench: .*)"#)
    private static let stdout = Pattern(#"---- (.+) (?:stdout|stderr) ----"#)
    private static let result = Pattern(#"test result: (?:ok|FAILED)\. ([0-9]+) passed; ([0-9]+) failed; ([0-9]+) ignored; ([0-9]+) measured; ([0-9]+) filtered out(?:; finished in .*)?"#)
    private static let failures = Pattern(#"failures:"#)
    private static let listed = Pattern(#"    ([^ ].*)"#)
    private static let nextestFail = Pattern(#" *(?:FAIL|SIGSEGV|SIGABRT|SIGKILL|SIGBUS|TIMEOUT|LEAK-FAIL|ABORT) \[ *[0-9.]+m?s\] (?:\( *[0-9]+/[0-9]+\) )?([^ ]+) (.+)"#)
    private static let nextestOther = Pattern(#" *(?:PASS|SKIP|LEAK|SLOW|FLAKY|START|RETRY|TRY [0-9]+ [A-Z-]+) \[.*"#)
    private static let nextestSummary = Pattern(#" *Summary \[[^\]]*\] +([0-9]+) tests? run: (.*)"#)
    private static let doctestLine = Pattern(#"\(line [0-9]+\)"#)
    private static let notes = Pattern(#" \([^)]*\)"#)
    private static let lineTemplate = PatternTemplate("(line N)")
    private static let dashSeparator = Bytes(utf8: " - ")

    private let found: FailureCollector
    private var listing = false

    init(_ found: FailureCollector) {
        self.found = found
    }

    private func testName(_ name: Bytes) -> Bytes {
        Self.doctestLine.replacingAll(in: name, with: Self.lineTemplate)
    }

    func step(_ lines: [Bytes], _ i: Int) -> Int {
        let line = lines[i]
        if listing {
            if let match = Self.listed.fullMatch(line),
               !match.text(1).split(Self.dashSeparator)[0].contains(Byte.space) {
                found.test("cargo", testName(match.text(1)))
                return 1
            }
            listing = false
        }
        if let match = Self.line.fullMatch(line) {
            if match.text(2) == Bytes(utf8: "FAILED") { found.test("cargo", testName(match.text(1))) }
            return 1
        }
        if let match = Self.stdout.fullMatch(line) {
            found.test("cargo", testName(match.text(1)))
            return 1
        }
        if let match = Self.result.fullMatch(line) {
            found.summary("cargo", .test, failed: TaskText.number(match.text(2)),
                          passed: TaskText.number(match.text(1)), mode: .sum)
            return 1
        }
        if Self.failures.fullMatches(line), i + 1 < lines.count, Self.listed.fullMatches(lines[i + 1]) {
            listing = true
            return 1
        }
        if let match = Self.nextestFail.fullMatch(line) {
            found.test("cargo", testName(match.text(2)))
            return 1
        }
        if Self.nextestOther.fullMatches(line) { return 1 }
        if let match = Self.nextestSummary.fullMatch(line) {
            let counts = FailureCounts.parse(Self.notes.replacingAll(in: match.text(2), with: ""))
            let failed = FailureCounts.sum(counts, "failed", "timed out", "exec failed", "leaky failed")
            found.summary("nextest", .test, failed: failed, passed: counts[Bytes(utf8: "passed")] ?? 0)
            return 1
        }
        return 0
    }
}

/// rustc and cargo build (TASK-PARSERS 5.3).
final class RustcRecognizer: LineRecognizer {
    private static let header = Pattern(#"error(?:\[(E[0-9]{4})\])?: (.+)"#)
    private static let warning = Pattern(#"warning(?:\[[A-Za-z0-9_:]+\])?: .+"#)
    private static let location = Pattern(#" *--> (.+?):([0-9]+):([0-9]+)"#)
    private static let couldNot = Pattern(#"error: could not compile `[^`]+`(?: \([^)]*\))?(?:;.*)?(?: due to ([0-9]+) previous errors?| due to previous error)?(?:;.*)?"#)
    private static let aborting = Pattern(#"error: aborting due to (?:([0-9]+) )?previous errors?(?:;.*)?"#)
    private static let status = Pattern(#"error: (?:test failed|build failed|failed to run|[0-9]+ targets? failed|process didn't exit successfully|Recipe ).*"#)

    private let found: FailureCollector

    init(_ found: FailureCollector) {
        self.found = found
    }

    /// The index of the `-->` line within the 3 lines after a header, when no other header comes first.
    private func locationLine(_ lines: [Bytes], _ i: Int) -> Int? {
        for k in (i + 1)..<min(i + 4, lines.count) {
            if Self.location.fullMatches(lines[k]) { return k }
            if Self.header.fullMatches(lines[k]) || Self.warning.fullMatches(lines[k]) { return nil }
        }
        return nil
    }

    func step(_ lines: [Bytes], _ i: Int) -> Int {
        let line = lines[i]
        if let match = Self.couldNot.fullMatch(line) ?? Self.aborting.fullMatch(line) {
            found.summary("rustc", .error, failed: TaskText.number(match[1] ?? [0x31]), passed: nil, mode: .sum)
            return 1
        }
        if Self.status.fullMatches(line) { return 1 }
        if let match = Self.header.fullMatch(line) {
            guard let k = locationLine(lines, i), let location = Self.location.fullMatch(lines[k]) else { return 0 }
            found.error("rustc", file: location.text(1), code: match.text(1), message: match.text(2))
            return k - i + 1
        }
        if Self.warning.fullMatches(line) {
            return locationLine(lines, i).map { $0 - i + 1 } ?? 0
        }
        return 0
    }
}
