import Foundation

/// JUnit through Gradle and Surefire, javac, kotlinc and Maven (TASK-PARSERS 5.3).
final class JvmRecognizer: LineRecognizer {
    private static let gradleTest = Pattern(#"([^ ]+) > (.+?) (FAILED|PASSED|SKIPPED)"#)
    private static let gradleSummary = Pattern(#"([0-9]+) tests? completed, ([0-9]+) failed(?:, ([0-9]+) skipped)?"#)
    private static let gradleNoise = Pattern(#"> Task [^ ]+(?: [A-Z-]+)?|FAILURE: Build failed with an exception\.|BUILD (?:SUCCESSFUL|FAILED) in .*|\* What went wrong:|\* Try:"#)
    private static let surefireTest = Pattern(#"\[ERROR\] ([A-Za-z0-9_.$]+)\.([A-Za-z0-9_$\[\]]+)(?:\(.*\))? -- Time elapsed: .* <<< (?:FAILURE|ERROR)!"#)
    private static let surefireOld = Pattern(#"(?:\[ERROR\] )?([A-Za-z0-9_$]+)\(([A-Za-z0-9_.$]+)\)[ \t]+Time elapsed: .* <<< (?:FAILURE|ERROR)!"#)
    private static let surefireHeader = Pattern(#"\[ERROR\] (?:Failures|Errors): ?"#)
    private static let surefireListed = Pattern(#"\[ERROR\][ \t]+([A-Za-z0-9_$]+)\.([A-Za-z0-9_$\[\]]+)(?::[0-9]+)?(?:[ \t].*)?"#)
    private static let surefireRun = Pattern(#"\[(?:ERROR|WARNING|INFO)\] Tests run: ([0-9]+), Failures: ([0-9]+), Errors: ([0-9]+), Skipped: ([0-9]+)(?:, Flakes: [0-9]+)?(.*)"#)
    private static let mavenJavac = Pattern(#"\[ERROR\] (.+?\.(?:java|kt|scala)):\[([0-9]+),([0-9]+)\] (.+)"#)
    private static let mavenErrors = Pattern(#"\[INFO\] ([0-9]+) errors?[ \t]*"#)
    private static let mavenNoise = Pattern(#"\[ERROR\] COMPILATION ERROR :?|\[INFO\] BUILD (?:FAILURE|SUCCESS)"#)
    private static let javacError = Pattern(#"(.+?\.java):([0-9]+): error: (.+)"#)
    private static let javacWarning = Pattern(#"(.+?\.java):([0-9]+): warning: .*"#)
    private static let javacCount = Pattern(#"([0-9]+) errors?"#)
    private static let kotlinError = Pattern(#"e: (?:file://)?(.+?\.kts?):([0-9]+):([0-9]+) (.+)"#)
    private static let kotlinErrorOld = Pattern(#"e: (.+?\.kts?): \(([0-9]+), ([0-9]+)\): (.+)"#)
    private static let kotlinWarning = Pattern(#"w: .*"#)

    private let found: FailureCollector
    private var listing = false

    init(_ found: FailureCollector) {
        self.found = found
    }

    /// The class's last dot component, `.`, the method without a trailing `()`.
    static func junitID(_ className: Bytes, _ method: Bytes) -> Bytes {
        let simple = className.lastPart(byte: Byte.dot)
        let name = method.hasSuffix(Bytes(utf8: "()")) ? Bytes(method.dropLast(2)) : method
        return simple + [Byte.dot] + name
    }

    func step(_ lines: [TextLine], _ i: Int) -> Int {
        let line = lines[i]
        if listing {
            if Self.surefireRun.fullMatches(line) || line.bytes.hasPrefix(Bytes(utf8: "[INFO]"))
                || line.bytes == Bytes(utf8: "[ERROR] ") || line.bytes == Bytes(utf8: "[ERROR]") {
                listing = false
            } else {
                if let match = Self.surefireListed.fullMatch(line), !line.bytes.hasPrefix(Bytes(utf8: "[ERROR]   Run ")) {
                    found.test("junit", Self.junitID(match.text(1), match.text(2)))
                    return 1
                }
                if line.bytes.hasPrefix(Bytes(utf8: "[ERROR]")) || line.bytes.hasPrefix(Bytes(utf8: "[WARNING]")) { return 1 }
                listing = false
            }
        }
        if let match = Self.gradleTest.fullMatch(line) {
            if match.text(3) == Bytes(utf8: "FAILED") { found.test("junit", Self.junitID(match.text(1), match.text(2))) }
            return 1
        }
        if let match = Self.gradleSummary.fullMatch(line) {
            let total = TaskText.number(match.text(1))
            let failed = TaskText.number(match.text(2))
            let skipped = TaskText.number(match[3] ?? [0x30])
            found.summary("gradle", .test, failed: failed,
                          passed: max(0, TaskText.subtract(TaskText.subtract(total, failed), skipped)), mode: .sum)
            return 1
        }
        if Self.gradleNoise.fullMatches(line) { return 1 }
        if let match = Self.surefireTest.fullMatch(line) {
            found.test("junit", Self.junitID(match.text(1), match.text(2)))
            return 1
        }
        if let match = Self.surefireOld.fullMatch(line) {
            found.test("junit", Self.junitID(match.text(2), match.text(1)))
            return 1
        }
        if Self.surefireHeader.fullMatches(line) {
            listing = true
            return 1
        }
        if let match = Self.surefireRun.fullMatch(line) {
            if match.text(5).trimmed().isEmpty {
                let run = TaskText.number(match.text(1))
                let failures = TaskText.number(match.text(2))
                let errors = TaskText.number(match.text(3))
                let skipped = TaskText.number(match.text(4))
                let failed = TaskText.add(failures, errors)
                found.summary("maven", .test, failed: failed,
                              passed: max(0, TaskText.subtract(TaskText.subtract(run, failed), skipped)))
            }
            return 1
        }
        if let match = Self.mavenJavac.fullMatch(line) {
            found.error("javac", file: match.text(1), code: [], message: match.text(4))
            return 1
        }
        if let match = Self.mavenErrors.fullMatch(line) {
            found.summary("javac", .error, failed: TaskText.number(match.text(1)), passed: nil)
            return 1
        }
        if Self.mavenNoise.fullMatches(line) { return 1 }
        if let match = Self.javacError.fullMatch(line) {
            found.error("javac", file: match.text(1), code: [], message: match.text(3))
            return 1
        }
        if Self.javacWarning.fullMatches(line) { return 1 }
        if let match = Self.javacCount.fullMatch(line) {
            found.summary("javac", .error, failed: TaskText.number(match.text(1)), passed: nil)
            return 1
        }
        if let match = Self.kotlinError.fullMatch(line) ?? Self.kotlinErrorOld.fullMatch(line) {
            found.error("kotlinc", file: match.text(1), code: [], message: match.text(4))
            return 1
        }
        if Self.kotlinWarning.fullMatches(line) { return 1 }
        return 0
    }
}
