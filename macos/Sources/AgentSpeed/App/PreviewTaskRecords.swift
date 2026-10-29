import AgentSpeedCore
import Foundation

/// Sample task records (METRICS 6.11) for the Usage History previews: a few hundred tasks over the last 60 days across
/// Codex, Claude Code, Claude Desktop and Pi, with models shared between sources, scored and unscored tasks, passes,
/// stalls and fixes. Deterministic, so renders compare between runs.
enum PreviewTaskRecords {
    private struct Profile {
        var source: SourceKind
        var models: [String]
        var tasks: Int
        var mean: Double
        var spread: Double
        var scoredShare: Double
        var successShare: Double
        var stallShare: Double
    }

    private static let profiles = [
        Profile(source: .codex, models: ["gpt-6.1-sol", "gpt-6.1-terra"], tasks: 128, mean: 69, spread: 18,
                scoredShare: 0.75, successShare: 0.78, stallShare: 0.1),
        Profile(source: .claudeCode, models: ["claude-opus-5-5", "claude-sonnet-5-5"], tasks: 214, mean: 64, spread: 20,
                scoredShare: 0.8, successShare: 0.69, stallShare: 0.16),
        Profile(source: .claudeDesktop, models: ["claude-fable-5-1"], tasks: 9, mean: 62, spread: 16,
                scoredShare: 0.67, successShare: 0.67, stallShare: 0.11),
        Profile(source: .pi, models: ["gpt-6.1-terra", "claude-opus-5-5"], tasks: 38, mean: 58, spread: 22,
                scoredShare: 0.7, successShare: 0.6, stallShare: 0.18),
    ]

    static func records(endingAt now: Date, calendar: Calendar) -> [TaskRecord] {
        var random = Generator(seed: 0x5EED)
        var records: [TaskRecord] = []
        let formatter = DateFormatter()
        formatter.calendar = calendar
        formatter.timeZone = calendar.timeZone
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.dateFormat = "yyyy-MM-dd"
        for profile in profiles {
            for index in 0..<profile.tasks {
                let end = now.addingTimeInterval(-Double(random.next(in: 0..<(60 * 86_400))))
                let minutes = 2 + random.next(in: 0..<40)
                let startMs = Int64((end.timeIntervalSince1970 - Double(minutes * 60)) * 1000)
                let scored = random.unit() < profile.scoredShare
                let score = scored ? max(0, min(100, profile.mean + profile.spread * random.normal())) : nil
                let success = scored && random.unit() < profile.successShare
                let fixAttempts = scored ? random.next(in: 0..<5) : 0
                let fixFailures = fixAttempts > 0 ? random.next(in: 0..<(fixAttempts + 1)) / 2 : 0
                let recovered = max(0, fixAttempts - fixFailures - 1)
                var counts = TaskCounts(actions: 20, counted: 16)
                counts.fixAttempts = fixAttempts
                counts.fixFailures = fixFailures
                counts.recovered = recovered
                counts.recoverAttempts = recovered > 0 ? recovered + random.next(in: 0..<(recovered + 1)) : 0
                records.append(TaskRecord(
                    agentId: "\(profile.source.rawValue)-\(index)", source: profile.source,
                    day: formatter.string(from: end), ordinal: 0, startMs: startMs,
                    endMs: Int64(end.timeIntervalSince1970 * 1000), complete: true,
                    model: profile.models[index % profile.models.count], state: success ? .passing : (scored ? .failing : .none),
                    score: score, counts: counts,
                    firstPassMs: success ? startMs + Int64(60_000 * (1 + random.next(in: 0..<9))) : nil,
                    success: success, stalled: random.unit() < profile.stallShare
                ))
            }
        }
        return records
    }

    /// A small linear congruential generator: the same records on every run.
    private struct Generator {
        var state: UInt64

        init(seed: UInt64) { state = seed }

        mutating func unit() -> Double {
            state = state &* 6_364_136_223_846_793_005 &+ 1_442_695_040_888_963_407
            return Double(state >> 11) / Double(1 << 53)
        }

        mutating func next(in range: Range<Int>) -> Int {
            range.lowerBound + Int(unit() * Double(range.count))
        }

        /// Box–Muller.
        mutating func normal() -> Double {
            let u = max(unit(), 1e-12)
            return (-2 * log(u)).squareRoot() * cos(2 * .pi * unit())
        }
    }
}
