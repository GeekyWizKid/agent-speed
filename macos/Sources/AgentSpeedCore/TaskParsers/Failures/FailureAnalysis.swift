import Foundation

/// `failures` (TASK-PARSERS 5): recognisers over the cleaned output, then counts, framework and fingerprints.
enum FailureAnalysis {
    static let maxFingerprints = 256

    private static let genericFile = Pattern(#"(.+?\.[A-Za-z0-9]+):([0-9]+)(?::([0-9]+))?:? (?:fatal )?error(?:\[([^\]]+)\])?:? (.+)"#)
    private static let genericPrefix = Pattern(#"(?:error|ERROR|Error|fatal|FATAL)(?:\[([^\]]+)\])?: (.+)"#)
    private static let genericException = Pattern(#"(?:Uncaught )?([A-Za-z_][A-Za-z0-9_.]*(?:Error|Exception)): (.*)"#)
    private static let completions: [(String, Pattern)] = [
        ("cargo", Pattern(#"Finished .* target\(s\) in .+"#)),
        ("swiftpm", Pattern(#"Build complete!(?: \(.*\))?"#)),
        ("xcodebuild", Pattern(#"\*\* [A-Z]+(?: [A-Z]+)* SUCCEEDED \*\*(?: \[.*\])?"#)),
        ("gradle", Pattern(#"BUILD SUCCESSFUL(?: in .+)?"#)),
        ("maven", Pattern(#"\[INFO\] BUILD SUCCESS"#)),
        ("prettier", Pattern(#"All matched files use Prettier code style!"#)),
        ("vite", Pattern(#"✓ built in [0-9]+(?:\.[0-9]+)?(?:ms|s)"#)),
    ]
    private static let tscPrefix = Bytes(utf8: "tsc|")

    private struct Segment {
        var lines: [Bytes]
        var claimed: [Bool]
    }

    private static func recognizers(_ found: FailureCollector) -> [LineRecognizer] {
        [
            PytestRecognizer(found), UnittestRecognizer(found), CargoRecognizer(found), RustcRecognizer(found),
            SwiftRecognizer(found), GoRecognizer(found), JavaScriptRecognizer(found), CheckerRecognizer(found),
            DotnetRecognizer(found), JvmRecognizer(found), MiscRecognizer(found), CcRecognizer(found),
        ]
    }

    /// `failures` and the canonical strings behind its fingerprints, sorted (fixtures only).
    static func detail(suite: String?, errorFilter: Bool, output: Bytes, cwd: String?,
                       failed: Bool?) -> (TaskFailures, [Bytes]) {
        let found = FailureCollector(cwd: cwd)
        var segments: [Segment] = []
        for part in OutputText.budget(output) {
            let lines = OutputText.cleanLines(part)
            var claimed = [Bool](repeating: false, count: lines.count)
            let readers = recognizers(found)
            var i = 0
            while i < lines.count {
                var used = 0
                for reader in readers {
                    used = reader.step(lines, i)
                    if used > 0 { break }
                }
                if used > 0 {
                    for k in i..<min(i + used, lines.count) { claimed[k] = true }
                    i += used
                } else {
                    i += 1
                }
            }
            for reader in readers { reader.finish() }
            segments.append(Segment(lines: lines, claimed: claimed))
        }
        if found.canonical.isEmpty, failed == true {
            for segment in segments { generic(found, segment) }
        }
        return (summarize(found, suite: suite, errorFilter: errorFilter, output: output, segments: segments, failed: failed),
                sortedDistinct(found.canonical))
    }

    private static func generic(_ found: FailureCollector, _ segment: Segment) {
        for (index, line) in segment.lines.enumerated() where !segment.claimed[index] {
            if let match = genericFile.fullMatch(line) {
                found.error("generic", file: match.text(1), code: match.text(4), message: match.text(5))
            } else if let match = genericPrefix.fullMatch(line) {
                found.error("generic", file: [], code: match.text(1), message: match.text(2))
            } else if let match = genericException.fullMatch(line) {
                found.error("generic", file: [], code: match.text(1), message: match.text(2))
            }
        }
    }

    private static func sortedDistinct(_ items: [Bytes]) -> [Bytes] {
        var sorted = items.sorted { $0.ordinalLess($1) }
        var write = 0
        for read in sorted.indices where read == 0 || sorted[read] != sorted[write - 1] {
            sorted[write] = sorted[read]
            write += 1
        }
        sorted.removeLast(sorted.count - write)
        return sorted
    }

    private static func completion(_ segments: [Segment]) -> String? {
        for segment in segments {
            for line in segment.lines {
                let trimmed = line.trimmed()
                for (name, pattern) in completions where pattern.fullMatches(trimmed) { return name }
            }
        }
        return nil
    }

    private struct Combined {
        var family: FailureCollector.Family
        var failed: Int64
        var passed: Int64?
    }

    private static func summarize(_ found: FailureCollector, suite: String?, errorFilter: Bool, output: Bytes,
                                  segments: [Segment], failed: Bool?) -> TaskFailures {
        let hasTsc = found.canonical.contains { $0.hasPrefix(tscPrefix) }
        var summaries = found.summaries.map { summary -> FailureCollector.Summary in
            var summary = summary
            if summary.framework == "found-errors" {
                summary.framework = hasTsc || suite == "tsc" || suite == "vue-tsc" ? "tsc" : "ruff"
            }
            return summary
        }
        if summaries.contains(where: { $0.framework == "nextest" }) {
            summaries.removeAll { $0.framework == "cargo" }
        }
        var combined: [String: Combined] = [:]
        var order: [String] = []
        for summary in summaries {
            if let existing = combined[summary.framework] {
                if summary.mode == .sum {
                    let passed: Int64? = existing.passed == nil && summary.passed == nil ? nil
                        : TaskText.add(existing.passed ?? 0, summary.passed ?? 0)
                    combined[summary.framework] = Combined(family: existing.family,
                                                           failed: TaskText.add(existing.failed, summary.failed),
                                                           passed: passed)
                } else {
                    combined[summary.framework] = Combined(family: summary.family, failed: summary.failed,
                                                           passed: summary.passed)
                }
            } else {
                order.append(summary.framework)
                combined[summary.framework] = Combined(family: summary.family, failed: summary.failed,
                                                       passed: summary.passed)
            }
        }
        let canonical = sortedDistinct(found.canonical)
        let tests = order.filter { combined[$0]!.family == .test }
        let errors = order.filter { combined[$0]!.family == .error }
        let chosen = tests.isEmpty ? errors : tests
        var failedCount: Int64?
        var passedCount: Int64?
        var framework: String?
        if !chosen.isEmpty {
            failedCount = chosen.reduce(0) { TaskText.add($0, combined[$1]!.failed) }
            let passes = chosen.compactMap { combined[$0]!.passed }
            passedCount = !tests.isEmpty && !passes.isEmpty ? passes.reduce(0, TaskText.add) : nil
            framework = chosen.first { combined[$0]!.failed != 0 }
                ?? chosen.first { (combined[$0]!.passed ?? 0) != 0 } ?? chosen[0]
        } else if !canonical.isEmpty {
            failedCount = Int64(canonical.count)
            framework = found.canonical[0].firstPart(byte: 0x7C).string
        } else {
            framework = completion(segments)
            failedCount = framework == nil ? nil : 0
            if failedCount == nil, failed == false, errorFilter, MaskedRuns.isSilent(output) { failedCount = 0 }
        }
        var digests = Set<String>()
        for text in canonical { digests.insert(TaskText.sha16(text)) }
        return TaskFailures(fingerprints: Array(digests.sorted().prefix(maxFingerprints)), failedCount: failedCount,
                            passedCount: passedCount, framework: framework)
    }
}
