import Foundation

/// MSBuild, VSTest and the Microsoft Testing Platform (TASK-PARSERS 5.3).
final class DotnetRecognizer: LineRecognizer {
    private static let error = Pattern(#"[ \t]*(.+?)\(([0-9]+)(?:,([0-9]+))?(?:,[0-9]+,[0-9]+)?\): error ([A-Za-z]+[0-9]+): (.*?)(?: \[([^\]]+)\])?"#)
    private static let toolError = Pattern(#"[ \t]*([A-Za-z][A-Za-z0-9_.-]*) : error ([A-Za-z]+[0-9]+): (.*?)(?: \[([^\]]+)\])?"#)
    private static let warning = Pattern(#"[ \t]*.+?(?:\([0-9,]+\))? : warning [A-Za-z]+[0-9]+: .*|[ \t]*.+?\([0-9,]+\): warning [A-Za-z]+[0-9]+: .*"#)
    private static let count = Pattern(#"[ \t]*([0-9]+) Error\(s\)"#)
    private static let noise = Pattern(#"[ \t]*[0-9]+ Warning\(s\)|Build FAILED\.|Build succeeded\.|Time Elapsed .*"#)
    private static let terminal = Pattern(#"Build (?:failed|succeeded)(?: with ([0-9]+) error\(s\)(?: and [0-9]+ warning\(s\))?)?(?: and [0-9]+ warning\(s\))? in [0-9.]+s"#)
    private static let vstestFailed = Pattern(#"[ \t]*Failed ([^ ]+) \[[^\]]*\]"#)
    private static let vstestPassed = Pattern(#"[ \t]*(?:Passed|Skipped) ([^ ]+) \[[^\]]*\]"#)
    private static let vstestSummary = Pattern(#"(?:Failed|Passed)![ \t]*-[ \t]*Failed:[ \t]*([0-9]+),[ \t]*Passed:[ \t]*([0-9]+),[ \t]*Skipped:[ \t]*([0-9]+),[ \t]*Total:[ \t]*([0-9]+).*"#)
    private static let mtpSummary = Pattern(#"[ \t]*Test summary: total: ([0-9]+), failed: ([0-9]+), succeeded: ([0-9]+), skipped: ([0-9]+).*"#)
    private static let mtpFailed = Pattern(#"[ \t]*failed (.+?) \([0-9.]+m?s\)"#)

    private let found: FailureCollector

    init(_ found: FailureCollector) {
        self.found = found
    }

    func step(_ lines: [TextLine], _ i: Int) -> Int {
        let line = lines[i]
        if let match = Self.error.fullMatch(line) {
            found.error("msbuild", file: match.text(1), code: match.text(4), message: match.text(5))
            return 1
        }
        if let match = Self.toolError.fullMatch(line) {
            found.error("msbuild", file: [], code: match.text(2), message: match.text(3))
            return 1
        }
        if Self.warning.fullMatches(line) { return 1 }
        if let match = Self.count.fullMatch(line) {
            found.summary("msbuild", .error, failed: TaskText.number(match.text(1)), passed: nil)
            return 1
        }
        if let match = Self.terminal.fullMatch(line) {
            if let errors = match[1], !errors.isEmpty {
                found.summary("msbuild", .error, failed: TaskText.number(errors), passed: nil)
            }
            return 1
        }
        if Self.noise.fullMatches(line) { return 1 }
        if let match = Self.vstestFailed.fullMatch(line) {
            found.test("dotnet", match.text(1))
            return 1
        }
        if Self.vstestPassed.fullMatches(line) { return 1 }
        if let match = Self.vstestSummary.fullMatch(line) {
            found.summary("dotnet", .test, failed: TaskText.number(match.text(1)), passed: TaskText.number(match.text(2)),
                          mode: .sum)
            return 1
        }
        if let match = Self.mtpSummary.fullMatch(line) {
            found.summary("dotnet-mtp", .test, failed: TaskText.number(match.text(2)),
                          passed: TaskText.number(match.text(3)))
            return 1
        }
        if let match = Self.mtpFailed.fullMatch(line) {
            found.test("dotnet", match.text(1))
            return 1
        }
        return 0
    }
}
