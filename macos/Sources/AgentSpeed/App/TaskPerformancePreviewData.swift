import AgentSpeedCore
import Foundation

/// Fake but internally consistent task records for the `taskhistory` previews, after the prototype's
/// `task-score-data.js`: every shown number (median, quartiles, bins, rates, means) is derived from per source and
/// model records the way METRICS 6.11 *Comparison* computes them.
enum TaskPerformancePreviewData {
    struct Record {
        var source: SourceKind
        var model: String
        var tasks: Int
        /// Scored tasks per 10-point bin; scores spread evenly inside each bin.
        var bins: [Int]
        var success: Int
        var stalled: Int
        var fixFailures: Int
        var fixAttempts: Int
        var recoverAttempts: Int
        var recovered: Int
        var passes: Int
        var passCenterMs: Double
    }

    static let records: [Record] = [
        Record(source: .codex, model: "gpt-6.1-sol", tasks: 74, bins: [1, 1, 2, 3, 4, 5, 8, 12, 12, 10], success: 47,
               stalled: 7, fixFailures: 11, fixAttempts: 52, recoverAttempts: 34, recovered: 19, passes: 50,
               passCenterMs: 280_000),
        Record(source: .codex, model: "gpt-6.1-terra", tasks: 54, bins: [1, 2, 2, 3, 4, 5, 6, 7, 5, 3], success: 28,
               stalled: 6, fixFailures: 12, fixAttempts: 41, recoverAttempts: 27, recovered: 12, passes: 30,
               passCenterMs: 430_000),
        Record(source: .claudeCode, model: "claude-opus-5-5", tasks: 132, bins: [2, 3, 5, 7, 9, 11, 17, 20, 19, 16],
               success: 78, stalled: 18, fixFailures: 30, fixAttempts: 101, recoverAttempts: 42, recovered: 33,
               passes: 84, passCenterMs: 170_000),
        Record(source: .claudeCode, model: "claude-sonnet-5-5", tasks: 82, bins: [3, 4, 5, 6, 7, 8, 9, 9, 6, 5],
               success: 40, stalled: 16, fixFailures: 24, fixAttempts: 66, recoverAttempts: 30, recovered: 19,
               passes: 46, passCenterMs: 240_000),
        Record(source: .claudeDesktop, model: "claude-fable-5-1", tasks: 9, bins: [0, 0, 0, 1, 1, 0, 2, 1, 1, 0],
               success: 4, stalled: 1, fixFailures: 2, fixAttempts: 6, recoverAttempts: 3, recovered: 2, passes: 4,
               passCenterMs: 350_000),
        Record(source: .omp, model: "claude-opus-5-5", tasks: 5, bins: [0, 0, 0, 1, 0, 1, 1, 1, 0, 0], success: 2,
               stalled: 1, fixFailures: 1, fixAttempts: 4, recoverAttempts: 2, recovered: 1, passes: 3,
               passCenterMs: 500_000),
        Record(source: .pi, model: "gpt-6.1-terra", tasks: 31, bins: [0, 1, 2, 3, 3, 4, 4, 3, 2, 1], success: 12,
               stalled: 5, fixFailures: 9, fixAttempts: 22, recoverAttempts: 15, recovered: 6, passes: 14,
               passCenterMs: 610_000),
        Record(source: .pi, model: "", tasks: 7, bins: [0, 1, 0, 1, 1, 0, 1, 0, 0, 0], success: 1, stalled: 2,
               fixFailures: 3, fixAttempts: 4, recoverAttempts: 0, recovered: 0, passes: 0, passCenterMs: 0),
    ]

    /// Every source of `records`, or only `narrowedTo`'s.
    static func comparison(_ records: [Record] = records, narrowedTo: SourceKind? = nil) -> TaskPerformanceComparison {
        let kept = records.filter { narrowedTo == nil || $0.source == narrowedTo }
        let bySource = SourceKind.allCases.compactMap { source -> TaskPerformanceSource? in
            let own = kept.filter { $0.source == source }
            return own.isEmpty ? nil : TaskPerformanceSource(source: source, stats: summarise(own))
        }
        var ids: [String] = []
        for record in kept where !ids.contains(record.model) { ids.append(record.model) }
        let byModel = ids.map { id -> TaskPerformanceModelRow in
            let own = kept.filter { $0.model == id }
            let sources = SourceKind.allCases.filter { source in own.contains { $0.source == source } }
            return TaskPerformanceModelRow(model: id, sources: sources, stats: summarise(own))
        }
        .sorted(by: TaskModelTable.standardOrder)
        return TaskPerformanceComparison(bySource: bySource, byModel: byModel)
    }

    static func summarise(_ records: [Record]) -> TaskPerformanceStats {
        let scores = records.flatMap(scores).sorted()
        let passes = records.flatMap(passTimes).sorted()
        let sum = { (value: (Record) -> Int) in records.reduce(0) { $0 + value($1) } }
        var histogram = Array(repeating: 0, count: 10)
        for score in scores { histogram[TaskPerformanceText.bin(of: score)] += 1 }
        let tasks = sum(\.tasks)
        let fixAttempts = sum(\.fixAttempts)
        let recovered = sum(\.recovered)
        return TaskPerformanceStats(
            tasks: tasks, scoredTasks: scores.count,
            medianScore: quantile(scores, 0.5), lowerQuartile: quantile(scores, 0.25), upperQuartile: quantile(scores, 0.75),
            histogram: histogram,
            successRate: scores.isEmpty ? nil : Double(sum(\.success)) / Double(scores.count),
            stallRate: tasks == 0 ? nil : Double(sum(\.stalled)) / Double(tasks),
            fixFailureRate: fixAttempts == 0 ? nil : Double(sum(\.fixFailures)) / Double(fixAttempts),
            recoverAttempts: recovered == 0 ? nil : Double(sum(\.recoverAttempts)) / Double(recovered),
            timeToFirstPassMs: quantile(passes, 0.5),
            successes: sum(\.success), stalled: sum(\.stalled), fixFailures: sum(\.fixFailures), fixAttempts: fixAttempts
        )
    }

    /// `q(p) = s[⌊h⌋] + (h − ⌊h⌋)(s[⌊h⌋ + 1] − s[⌊h⌋])`, `h = (n − 1) p`.
    static func quantile(_ sorted: [Double], _ p: Double) -> Double? {
        guard !sorted.isEmpty else { return nil }
        let h = Double(sorted.count - 1) * p
        let low = Int(h.rounded(.down))
        let high = min(low + 1, sorted.count - 1)
        return sorted[low] + (h - Double(low)) * (sorted[high] - sorted[low])
    }

    private static func scores(_ record: Record) -> [Double] {
        record.bins.enumerated().flatMap { bin, count in
            (0..<count).map { index in
                Double(bin) * 10 + (Double(index) + 0.5) / Double(count) * (bin == 9 ? 10 : 9.98)
            }
        }
    }

    /// First-pass times spread around the record's centre (a golden-ratio sequence), as the prototype does.
    private static func passTimes(_ record: Record) -> [Double] {
        (0..<record.passes).map { index in
            let spread = (Double(index) * 0.6180339887).truncatingRemainder(dividingBy: 1)
            return (record.passCenterMs * (0.35 + 1.3 * spread)).rounded()
        }
    }
}
