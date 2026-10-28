import Foundation

/// swift-testing, XCTest, xcodebuild and swiftc (TASK-PARSERS 5.3).
final class SwiftRecognizer: LineRecognizer {
    private static let mark = #"[^ \tA-Za-z0-9]+ "#
    private static let issue = Pattern(mark + #"Test (.+?) recorded an issue.*"#)
    private static let stFailed = Pattern(mark + #"Test (.+?) failed after [0-9]+(?:\.[0-9]+)? seconds(?: with [0-9]+ issues?)?(?: \(including [0-9]+ known issues?\))?\."#)
    private static let stOther = Pattern(mark + #"(?:Test .+? (?:passed after .*|started\.|skipped.*)|Suite .+? (?:passed|failed|started).*|Test run started\.|Testing Library Version: .*|Target Platform: .*)"#)
    private static let stRun = Pattern(mark + #"Test run with ([0-9]+) tests?(?: in [0-9]+ suites?)? (passed|failed) after [0-9]+(?:\.[0-9]+)? seconds(?: with ([0-9]+) issues?)?(?: \(including [0-9]+ known issues?\))?\."#)
    private static let stCases = Pattern(#" with [0-9]+ test cases?$"#)
    private static let xcCase = Pattern(#"Test [Cc]ase '(-\[[^\]]+\]|[^']+)' (failed|passed|started|skipped)(?: on '[^']*')?(?: \([0-9.]+ seconds\))?\.?"#)
    private static let xcError = Pattern(#"(.+?):([0-9]+): error: (-\[[^\]]+\]|[A-Za-z0-9_.()/]+) : .*"#)
    private static let xcSuite = Pattern(#"Test [Ss]uite '.+' (?:started|passed|failed).*"#)
    private static let xcExecuted = Pattern(#"[ \t]*Executed ([0-9]+) tests?, with (?:([0-9]+) tests? skipped and )?([0-9]+) failures? \(([0-9]+) unexpected\) in [0-9.]+ \([0-9.]+\) seconds"#)
    private static let xbFailing = Pattern(#"Failing tests:"#)
    private static let xbListed = Pattern(#"[ \t]+(.+?)(?: \(.*\))?"#)
    private static let xbResult = Pattern(#"\*\* (?:TEST|BUILD|ANALYZE|ARCHIVE|CLEAN|TEST BUILD|INSTALL) (?:FAILED|SUCCEEDED|INTERRUPTED) \*\*"#)
    private static let xbCommands = Pattern(#"The following build commands failed:"#)
    private static let xbCount = Pattern(#"\(([0-9]+) failures?\)"#)
    private static let swiftcError = Pattern(#"(.+\.swift):([0-9]+):([0-9]+): error: (.+)"#)
    private static let swiftcWarning = Pattern(#"(.+\.swift):([0-9]+):([0-9]+): (?:warning|note): .+"#)
    private static let noise = Pattern(#"error: (?:emit-module command failed|fatalError|compile command failed|link command failed).*"#)

    private let found: FailureCollector
    private var stFailedNames = Set<Bytes>()
    private var xcFailedNames = Set<Bytes>()
    private var listing = false
    private var commands = false

    init(_ found: FailureCollector) {
        self.found = found
    }

    /// `Class.method` of an XCTest name: `-[` … `]` stripped, spaces and `/` as dots, `()` dropped, the last two parts.
    static func xctestID(_ name: Bytes) -> Bytes {
        var s = name
        if s.hasPrefix(Bytes(utf8: "-[")), s.last == 0x5D, s.count >= 3 { s = Bytes(s[2..<(s.count - 1)]) }
        s = s.replacing(byte: Byte.space, with: Byte.dot).replacing(byte: Byte.slash, with: Byte.dot)
            .replacing(Bytes(utf8: "()"), with: [])
        let parts = s.split(byte: Byte.dot).filter { !$0.isEmpty }
        return Bytes.joined(Array(parts.suffix(2)), separator: [Byte.dot])
    }

    private func xctestFailure(_ name: Bytes) {
        let test = Self.xctestID(name)
        xcFailedNames.insert(test)
        found.test("xctest", test)
    }

    func step(_ lines: [Bytes], _ i: Int) -> Int {
        let line = lines[i]
        if listing {
            if let match = Self.xbListed.fullMatch(line) {
                if match.text(1).last != Byte.colon { xctestFailure(match.text(1)) }
                return 1
            }
            listing = false
        }
        if commands {
            if let match = Self.xbCount.fullMatch(line) {
                found.summary("xcodebuild", .error, failed: TaskText.number(match.text(1)), passed: nil)
                commands = false
                return 1
            }
            if line.isIndented { return 1 }
            commands = false
        }
        if let match = Self.stFailed.fullMatch(line), !match.text(1).hasPrefix(Bytes(utf8: "run with ")) {
            let name = Self.stCases.replacingAll(in: match.text(1), with: "")
            stFailedNames.insert(name)
            found.test("swift-testing", name)
            return 1
        }
        if let match = Self.stRun.fullMatch(line) {
            let total = TaskText.number(match.text(1))
            let failed: Int64
            if match.text(2) == Bytes(utf8: "passed") {
                failed = 0
            } else if !stFailedNames.isEmpty {
                failed = Int64(stFailedNames.count)
            } else {
                failed = TaskText.number(match[3] ?? [0x31])
            }
            found.summary("swift-testing", .test, failed: failed, passed: max(0, TaskText.subtract(total, failed)))
            return 1
        }
        if Self.issue.fullMatches(line) || Self.stOther.fullMatches(line) { return 1 }
        if let match = Self.xcCase.fullMatch(line) {
            if match.text(2) == Bytes(utf8: "failed") { xctestFailure(match.text(1)) }
            return 1
        }
        if let match = Self.xcError.fullMatch(line) {
            xctestFailure(match.text(3))
            return 1
        }
        if Self.xcSuite.fullMatches(line) { return 1 }
        if let match = Self.xcExecuted.fullMatch(line) {
            let total = TaskText.number(match.text(1))
            let skipped = TaskText.number(match[2] ?? [0x30])
            let failures = TaskText.number(match.text(3))
            let failed = xcFailedNames.isEmpty ? failures : Int64(xcFailedNames.count)
            found.summary("xctest", .test, failed: failed,
                          passed: max(0, TaskText.subtract(TaskText.subtract(total, failed), skipped)))
            return 1
        }
        if Self.xbFailing.fullMatches(line) {
            listing = true
            return 1
        }
        if Self.xbResult.fullMatches(line) { return 1 }
        if Self.xbCommands.fullMatches(line) {
            commands = true
            return 1
        }
        if let match = Self.swiftcError.fullMatch(line) {
            found.error("swiftc", file: match.text(1), code: [], message: match.text(4))
            return 1
        }
        if Self.swiftcWarning.fullMatches(line) || Self.noise.fullMatches(line) { return 1 }
        return 0
    }
}
