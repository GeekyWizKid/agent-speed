import Foundation

/// rspec, phpunit and ctest (TASK-PARSERS 5.3).
final class MiscRecognizer: LineRecognizer {
    private static let rspecFailed = Pattern(#"rspec (?:\./)?(.+?)(?::[0-9]+|\[[0-9:]+\]) # (.*)"#)
    private static let rspecSummary = Pattern(#"([0-9]+) examples?, ([0-9]+) failures?(?:, ([0-9]+) pending)?(?:, ([0-9]+) errors? occurred outside of examples)?"#)
    private static let rspecLoad = Pattern(#"An error occurred while loading (?:\./)?(.+?)\.?"#)
    private static let phpunitSection = Pattern(#"There (?:was|were) [0-9]+ ([a-z][a-z ]*?):"#)
    private static let phpunitItem = Pattern(#"[0-9]+\) ([A-Za-z0-9_\\]+)::([A-Za-z0-9_]+)(?: with data set (#[0-9]+|\"[^\"]*\"))?(?: \(.*\))?"#)
    private static let phpunitTests = Pattern(#"Tests: ([0-9]+), Assertions: [0-9]+((?:, [A-Za-z ]+: [0-9]+)*)\."#)
    private static let phpunitOK = Pattern(#"OK \(([0-9]+) tests?, [0-9]+ assertions?\)"#)
    private static let phpunitNoise = Pattern(#"FAILURES!|ERRORS!|OK, but (?:there were issues|incomplete, skipped, or risky tests)!"#)
    private static let phpunitPair = Pattern(#"([A-Za-z ]+): ([0-9]+)"#)
    private static let ctestLine = Pattern(#" *[0-9]+/[0-9]+ Test +#[0-9]+: (.+?) \.*(?: +|\*\*\*)(Passed|Failed|Exception|Timeout|Not Run|Skipped)(?:[: ].*)?"#)
    private static let ctestListHeader = Pattern(#"The following tests (?:FAILED|did not run):"#)
    private static let ctestListed = Pattern(#"[ \t]+[0-9]+ - (.+?) \((.+)\)"#)
    private static let ctestSummary = Pattern(#"([0-9]+)% tests passed, ([0-9]+) tests? failed out of ([0-9]+)"#)
    private static let ctestNoise = Pattern(#" *Start +[0-9]+: .*|Test project .*|Errors while running CTest|Total Test time .*"#)
    private static let failingSections = TaskText.words(["failure", "failures", "error", "errors"])
    private static let failingCtest = TaskText.words(["Failed", "Exception", "Timeout"])

    private let found: FailureCollector
    private var phpSection: Bytes?
    private var ctestFailedList: Bool?

    init(_ found: FailureCollector) {
        self.found = found
    }

    func step(_ lines: [TextLine], _ i: Int) -> Int {
        let line = lines[i]
        if let failedList = ctestFailedList {
            if let match = Self.ctestListed.fullMatch(line) {
                if failedList { found.test("ctest", match.text(1)) }
                return 1
            }
            ctestFailedList = nil
        }
        if let match = Self.rspecFailed.fullMatch(line) {
            found.test("rspec", match.text(2), file: match.text(1))
            return 1
        }
        if let match = Self.rspecSummary.fullMatch(line) {
            let examples = TaskText.number(match.text(1))
            let failures = TaskText.number(match.text(2))
            let pending = TaskText.number(match[3] ?? [0x30])
            let outside = TaskText.number(match[4] ?? [0x30])
            found.summary("rspec", .test, failed: TaskText.add(failures, outside),
                          passed: max(0, TaskText.subtract(TaskText.subtract(examples, failures), pending)))
            return 1
        }
        if let match = Self.rspecLoad.fullMatch(line) {
            found.error("rspec", file: match.text(1), code: Bytes(utf8: "load"), message: [])
            return 1
        }
        if let match = Self.phpunitSection.fullMatch(line) {
            phpSection = match.text(1)
            return 1
        }
        if let match = Self.phpunitItem.fullMatch(line) {
            if let section = phpSection, Self.failingSections.contains(section) {
                var name = match.text(1) + Bytes(utf8: "::") + match.text(2)
                if let data = match[3], !data.isEmpty { name += [Byte.space] + data }
                found.test("phpunit", name)
            }
            return 1
        }
        if let match = Self.phpunitTests.fullMatch(line) {
            var counts: [Bytes: Int64] = [:]
            for part in match.text(2).split(Bytes(utf8: ", ")) {
                if let pair = Self.phpunitPair.fullMatch(part) { counts[pair.text(1)] = TaskText.number(pair.text(2)) }
            }
            let failed = FailureCounts.sum(counts, "Errors", "Failures")
            let passed = TaskText.subtract(TaskText.subtract(TaskText.subtract(TaskText.number(match.text(1)), failed),
                                                             counts[Bytes(utf8: "Skipped")] ?? 0),
                                           counts[Bytes(utf8: "Incomplete")] ?? 0)
            found.summary("phpunit", .test, failed: failed, passed: max(0, passed))
            return 1
        }
        if let match = Self.phpunitOK.fullMatch(line) {
            found.summary("phpunit", .test, failed: 0, passed: TaskText.number(match.text(1)))
            return 1
        }
        if Self.phpunitNoise.fullMatches(line) { return 1 }
        if let match = Self.ctestLine.fullMatch(line) {
            if Self.failingCtest.contains(match.text(2)) { found.test("ctest", match.text(1)) }
            return 1
        }
        if Self.ctestListHeader.fullMatches(line) {
            ctestFailedList = line.bytes.contains(Bytes(utf8: "FAILED"))
            return 1
        }
        if let match = Self.ctestSummary.fullMatch(line) {
            let failed = TaskText.number(match.text(2))
            let total = TaskText.number(match.text(3))
            found.summary("ctest", .test, failed: failed, passed: max(0, TaskText.subtract(total, failed)))
            return 1
        }
        if Self.ctestNoise.fullMatches(line) { return 1 }
        return 0
    }
}
