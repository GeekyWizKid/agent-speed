import Foundation

/// tsc, eslint, ruff, black, mypy and pyright (TASK-PARSERS 5.3).
final class CheckerRecognizer: LineRecognizer {
    private static let tscParen = Pattern(#"(.+?)\(([0-9]+),([0-9]+)\): error (TS[0-9]+): (.*)"#)
    private static let tscPretty = Pattern(#"(.+?):([0-9]+):([0-9]+) - error (TS[0-9]+): (.*)"#)
    private static let tscFound = Pattern(#"Found ([0-9]+) errors? in .*"#)
    private static let foundErrors = Pattern(#"Found ([0-9]+) errors?(?: \(([0-9]+) fixed, ([0-9]+) remaining\))?\."#)
    private static let tscTable = Pattern(#"Errors +Files"#)
    private static let tscRow = Pattern(#"[ \t]+[0-9]+[ \t]+.+:[0-9]+"#)
    private static let eslintFile = Pattern(#"(/[^:]*|[A-Za-z]:[\\/][^:]*)"#)
    private static let eslintMessage = Pattern(#"[ \t]+([0-9]+):([0-9]+)[ \t]+(error|warning)[ \t]+(.+?)(?:[ \t]{2,}([^ \t]+))?"#)
    private static let eslintUnix = Pattern(#"(.+?):([0-9]+):([0-9]+): (.+) \[(Error|Warning)/(.+)\]"#)
    private static let eslintSummary = Pattern(#"✖ ([0-9]+) problems? \(([0-9]+) errors?, ([0-9]+) warnings?\)"#)
    private static let eslintFixable = Pattern(#"[ \t]+[0-9]+ errors? and [0-9]+ warnings? potentially fixable.*"#)
    private static let ruffConcise = Pattern(#"(.+?\.(?:py|pyi|ipynb)):([0-9]+):([0-9]+): ([A-Z]+[0-9]+|SyntaxError|invalid-syntax):? (?:\[\*\] )?(.*)"#)
    private static let ruffHeader = Pattern(#"([A-Z]{1,4}[0-9]{1,4}|invalid-syntax|SyntaxError):? (?:\[\*\] )?(.+)"#)
    private static let ruffLocation = Pattern(#" *--> (.+?):([0-9]+):([0-9]+)"#)
    private static let ruffPassed = Pattern(#"All checks passed!"#)
    private static let ruffWould = Pattern(#"Would reformat: (.+)"#)
    private static let blackWould = Pattern(#"would reformat (.+)"#)
    private static let formatSummary = Pattern(#"(?:Oh no! .*)?([0-9]+) files? would be reformatted(?:, [0-9]+ files? (?:already formatted|would be left unchanged))?\.?"#)
    private static let mypyError = Pattern(#"(.+?\.pyi?):([0-9]+)(?::([0-9]+))?: error: (.+?)(?:  \[([a-z0-9-]+)\])?"#)
    private static let mypyNote = Pattern(#"(.+?\.pyi?):([0-9]+)(?::([0-9]+))?: note: .*"#)
    private static let mypyFound = Pattern(#"Found ([0-9]+) errors? in [0-9]+ files? \((?:checked [0-9]+ source files?|errors prevented further checking)\)"#)
    private static let mypySuccess = Pattern(#"Success: no issues found in [0-9]+ source files?"#)
    private static let pyrightError = Pattern(#"[ \t]*(.+?):([0-9]+):([0-9]+) - error: (.+?)(?: \((report[A-Za-z]+)\))?"#)
    private static let pyrightRule = Pattern(#" \((report[A-Za-z]+)\)$"#)
    private static let pyrightOther = Pattern(#"[ \t]*(.+?):([0-9]+):([0-9]+) - (?:warning|information|info|note): .*"#)
    private static let pyrightSummary = Pattern(#"([0-9]+) errors?, ([0-9]+) warnings?, ([0-9]+) (?:informations?|infos?|notes?) ?"#)
    private static let format = Bytes(utf8: "format")

    private let found: FailureCollector
    private var eslintFileName: Bytes?
    private var tscTableOpen = false

    init(_ found: FailureCollector) {
        self.found = found
    }

    func step(_ lines: [TextLine], _ i: Int) -> Int {
        let line = lines[i]
        if tscTableOpen {
            if Self.tscRow.fullMatches(line) { return 1 }
            tscTableOpen = false
        }
        if let match = Self.tscParen.fullMatch(line) ?? Self.tscPretty.fullMatch(line) {
            found.error("tsc", file: match.text(1), code: match.text(4), message: match.text(5))
            return 1
        }
        if let match = Self.mypyFound.fullMatch(line) {
            found.summary("mypy", .error, failed: TaskText.number(match.text(1)), passed: nil)
            return 1
        }
        if let match = Self.tscFound.fullMatch(line) {
            found.summary("tsc", .error, failed: TaskText.number(match.text(1)), passed: nil)
            return 1
        }
        if let match = Self.foundErrors.fullMatch(line) {
            found.summary("found-errors", .error, failed: TaskText.number(match[3] ?? match.text(1)), passed: nil)
            return 1
        }
        if Self.tscTable.fullMatches(line) {
            tscTableOpen = true
            return 1
        }
        if let match = Self.eslintUnix.fullMatch(line) {
            if match.text(5) == Bytes(utf8: "Error") {
                found.error("eslint", file: match.text(1), code: match.text(6), message: match.text(4))
            }
            return 1
        }
        if Self.eslintFile.fullMatches(line), i + 1 < lines.count, Self.eslintMessage.fullMatches(lines[i + 1]) {
            eslintFileName = line.bytes
            return 1
        }
        if let file = eslintFileName, let match = Self.eslintMessage.fullMatch(line) {
            if match.text(3) == Bytes(utf8: "error") {
                found.error("eslint", file: file, code: match.text(5), message: match.text(4))
            }
            return 1
        }
        if let match = Self.eslintSummary.fullMatch(line) {
            found.summary("eslint", .error, failed: TaskText.number(match.text(2)), passed: nil)
            return 1
        }
        if Self.eslintFixable.fullMatches(line) { return 1 }
        if let match = Self.ruffConcise.fullMatch(line) {
            found.error("ruff", file: match.text(1), code: match.text(4), message: match.text(5))
            return 1
        }
        if let match = Self.ruffHeader.fullMatch(line) {
            for k in (i + 1)..<min(i + 4, lines.count) {
                if let location = Self.ruffLocation.fullMatch(lines[k]) {
                    found.error("ruff", file: location.text(1), code: match.text(1), message: match.text(2))
                    return k - i + 1
                }
            }
        }
        if Self.ruffPassed.fullMatches(line) {
            found.summary("ruff", .error, failed: 0, passed: nil)
            return 1
        }
        if let match = Self.ruffWould.fullMatch(line) {
            found.error("ruff", file: match.text(1), code: Self.format, message: [])
            return 1
        }
        if let match = Self.blackWould.fullMatch(line) {
            found.error("black", file: match.text(1), code: Self.format, message: [])
            return 1
        }
        if let match = Self.formatSummary.fullMatch(line) {
            found.summary("format", .error, failed: TaskText.number(match.text(1)), passed: nil)
            return 1
        }
        if let match = Self.mypyError.fullMatch(line) {
            found.error("mypy", file: match.text(1), code: match.text(5), message: match.text(4))
            return 1
        }
        if Self.mypyNote.fullMatches(line) { return 1 }
        if Self.mypySuccess.fullMatches(line) {
            found.summary("mypy", .error, failed: 0, passed: nil)
            return 1
        }
        if let match = Self.pyrightError.fullMatch(line) {
            let indent = line.bytes.leadingCount(of: [Byte.space, Byte.tab])
            var k = i
            var code = match.text(5)
            while k + 1 < lines.count, lines[k + 1].bytes.leadingCount(of: [Byte.space, Byte.tab]) > indent,
                  !Self.pyrightError.fullMatches(lines[k + 1]), !Self.pyrightOther.fullMatches(lines[k + 1]) {
                k += 1
            }
            if k > i, code.isEmpty { code = Self.pyrightRule.search(lines[k])?.text(1) ?? [] }
            found.error("pyright", file: match.text(1), code: code,
                        message: Self.pyrightRule.replacingAll(in: match.text(4), with: ""))
            return k - i + 1
        }
        if Self.pyrightOther.fullMatches(line) { return 1 }
        if let match = Self.pyrightSummary.fullMatch(line) {
            found.summary("pyright", .error, failed: TaskText.number(match.text(1)), passed: nil)
            return 1
        }
        return 0
    }
}
