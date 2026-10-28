import Foundation

/// jest, vitest, mocha, bun and deno (TASK-PARSERS 5.3).
final class JavaScriptRecognizer: LineRecognizer {
    private static let jestFile = Pattern(#" ?(FAIL|PASS) +([^ ]+)(?: \([0-9.]+ ?m?s\))?"#)
    private static let jestBullet = Pattern(#"  ● (.+)"#)
    private static let jestTests = Pattern(#"Tests:[ \t]+(.*[0-9]+ total)"#)
    private static let jestSuites = Pattern(#"Test Suites:[ \t]+(.*[0-9]+ total)"#)
    private static let jestNoise = Pattern(#"(?:Snapshots:|Time:|Ran all test suites).*|[ \t]+[✓✕○✎] .*|Summary of all failing tests"#)
    private static let vitestFail = Pattern(#" *FAIL +(.+?) > (.+?)(?: \[[^\]]*\])?"#)
    private static let vitestLoad = Pattern(#" *FAIL +([^ ]+) \[ (.+) \]"#)
    private static let vitestFile = Pattern(#" *[❯>✓] ([^ ]+) \([0-9]+ tests?(?: \|.*)?\)(?: [0-9.]+m?s)?"#)
    private static let vitestX = Pattern(#" +[×✗] (.+?)(?: [0-9.]+m?s)?"#)
    private static let vitestTests = Pattern(#" *Tests +(.+?) \(([0-9]+)\)"#)
    private static let vitestFiles = Pattern(#" *Test Files +(.+?) \(([0-9]+)\)"#)
    private static let mochaPassing = Pattern(#"[ \t]*([0-9]+) passing \([0-9a-z.]+\)"#)
    private static let mochaFailing = Pattern(#"[ \t]*([0-9]+) failing"#)
    private static let mochaPending = Pattern(#"[ \t]*([0-9]+) pending"#)
    private static let mochaItem = Pattern(#"([ \t]*)([0-9]+)\) (.*)"#)
    private static let bunFail = Pattern(#"\(fail\) (.+?)(?: \[[0-9.]+m?s\])?"#)
    private static let bunCount = Pattern(#" *([0-9]+) (pass|fail)"#)
    private static let denoSummary = Pattern(#"(?:ok|FAILED) \| ([0-9]+) passed(?: \([0-9]+ steps?\))? \| ([0-9]+) failed(?: \([0-9]+ steps?\))?.*"#)
    private static let denoFailure = Pattern(#"(.+?) => (.+?):[0-9]+:[0-9]+"#)
    private static let failedLabel = Bytes(utf8: "failed")
    private static let passedLabel = Bytes(utf8: "passed")

    private let found: FailureCollector
    private var jestFile = Bytes()
    private var jestTests: [Bytes: Int64]?
    private var jestSuites: [Bytes: Int64]?
    private var vitestFile = Bytes()
    private var vitestTests: [Bytes: Int64]?
    private var vitestFiles: [Bytes: Int64]?
    private var mochaPassing: Int64?
    private var mochaFailing: Int64?
    private var mochaDetails = false
    private var bun: [Bytes: Int64] = [:]

    init(_ found: FailureCollector) {
        self.found = found
    }

    func step(_ lines: [Bytes], _ i: Int) -> Int {
        let line = lines[i]
        if mochaDetails, let match = Self.mochaItem.fullMatch(line) {
            var parts = [match.text(3)]
            var k = i
            let indent = match.text(1).count
            while parts[parts.count - 1].last != Byte.colon, k + 1 < lines.count, !lines[k + 1].trimmed().isEmpty,
                  lines[k + 1].leadingCount(of: [Byte.space]) > indent {
                k += 1
                parts.append(lines[k].trimmed())
            }
            let title = Bytes.joined(parts.map { $0.trimmed() }, separator: [Byte.space])
            if title.last == Byte.colon {
                found.test("mocha", Bytes(title.dropLast()))
                return k - i + 1
            }
        }
        if let match = Self.vitestLoad.fullMatch(line) {
            found.test("vitest", [], file: match.text(1))
            return 1
        }
        if let match = Self.vitestFail.fullMatch(line) {
            found.test("vitest", match.text(2), file: match.text(1))
            return 1
        }
        if let match = Self.jestFile.fullMatch(line) {
            jestFile = match.text(1) == Bytes(utf8: "FAIL") ? match.text(2) : []
            return 1
        }
        if let match = Self.jestBullet.fullMatch(line) {
            if match.text(1) != Bytes(utf8: "Console") { found.test("jest", match.text(1), file: jestFile) }
            return 1
        }
        if let match = Self.jestSuites.fullMatch(line) {
            flushJest()
            jestSuites = FailureCounts.parse(match.text(1))
            return 1
        }
        if let match = Self.jestTests.fullMatch(line) {
            jestTests = FailureCounts.parse(match.text(1))
            flushJest()
            return 1
        }
        if Self.jestNoise.fullMatches(line) { return 1 }
        if let match = Self.vitestFile.fullMatch(line) {
            vitestFile = match.text(1)
            return 1
        }
        if let match = Self.vitestX.fullMatch(line), !vitestFile.isEmpty {
            found.test("vitest", match.text(1), file: vitestFile)
            return 1
        }
        if let match = Self.vitestFiles.fullMatch(line) {
            flushVitest()
            vitestFiles = FailureCounts.parse(match.text(1), separator: 0x7C)
            return 1
        }
        if let match = Self.vitestTests.fullMatch(line) {
            vitestTests = FailureCounts.parse(match.text(1), separator: 0x7C)
            flushVitest()
            return 1
        }
        if let match = Self.mochaPassing.fullMatch(line) {
            mochaPassing = TaskText.number(match.text(1))
            return 1
        }
        if let match = Self.mochaFailing.fullMatch(line) {
            mochaFailing = TaskText.number(match.text(1))
            mochaDetails = true
            return 1
        }
        if Self.mochaPending.fullMatches(line) { return 1 }
        if let match = Self.bunFail.fullMatch(line) {
            found.test("bun", match.text(1))
            return 1
        }
        if let match = Self.bunCount.fullMatch(line) {
            bun[match.text(2)] = TaskText.number(match.text(1))
            return 1
        }
        if let match = Self.denoSummary.fullMatch(line) {
            found.summary("deno", .test, failed: TaskText.number(match.text(2)), passed: TaskText.number(match.text(1)))
            return 1
        }
        if let match = Self.denoFailure.fullMatch(line) {
            found.test("deno", match.text(1), file: match.text(2))
            return 1
        }
        return 0
    }

    private func flush(_ framework: String, _ tests: [Bytes: Int64]?, _ files: [Bytes: Int64]?) {
        guard tests != nil || files != nil else { return }
        var failed = tests?[Self.failedLabel] ?? 0
        if failed == 0, let files, !files.isEmpty { failed = files[Self.failedLabel] ?? 0 }
        found.summary(framework, .test, failed: failed, passed: tests?[Self.passedLabel] ?? 0, mode: .sum)
    }

    private func flushJest() {
        flush("jest", jestTests, jestSuites)
        jestTests = nil
        jestSuites = nil
    }

    private func flushVitest() {
        flush("vitest", vitestTests, vitestFiles)
        vitestTests = nil
        vitestFiles = nil
    }

    func finish() {
        flushJest()
        flushVitest()
        if mochaPassing != nil || mochaFailing != nil {
            found.summary("mocha", .test, failed: mochaFailing ?? 0, passed: mochaPassing ?? 0)
        }
        if !bun.isEmpty {
            found.summary("bun", .test, failed: bun[Bytes(utf8: "fail")] ?? 0, passed: bun[Bytes(utf8: "pass")] ?? 0)
        }
    }
}
