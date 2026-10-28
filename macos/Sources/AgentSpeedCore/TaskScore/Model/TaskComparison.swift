import Foundation

/// The aggregates of one source or model over a date range (METRICS 6.11 *Comparison*).
public struct TaskComparisonRow: Equatable, Sendable {
    /// Source rows only.
    public var source: SourceKind?
    /// Model rows only: the model id, "" for an unknown model.
    public var model: String?
    /// Model rows only: the sources with records of that model, in source order.
    public var sources: [SourceKind]
    public var tasks: Int
    public var scoredTasks: Int
    public var medianScore: Double?
    public var medianShown: Int?
    public var lowerQuartile: Double?
    public var upperQuartile: Double?
    /// Ten counts: `[0, 10)` … `[80, 90)` and `[90, 100]`.
    public var histogram: [Int]
    /// Successful records among the scored ones: the numerator of `successRate`.
    public var successes: Int
    /// Stalled records: the numerator of `stallRate`.
    public var stalls: Int
    /// Σ `fixFailures` and Σ `fixAttempts`: the two sides of `fixFailureRate`.
    public var fixFailures: Int
    public var fixAttempts: Int
    /// Σ `recovered`: the denominator of `recoverAttempts`.
    public var recovered: Int
    public var successRate: Double?
    public var stallRate: Double?
    public var fixFailureRate: Double?
    /// The mean attempts of a recovered episode.
    public var recoverAttempts: Double?
    public var timeToFirstPassMs: Double?

    init(source: SourceKind?, model: String?, sources: [SourceKind], records: [TaskRecord]) {
        self.source = source
        self.model = model
        self.sources = sources
        let scores = records.compactMap(\.score)
        let scored = records.filter { $0.score != nil }
        let fixAttempts = records.reduce(0) { $0 + $1.counts.fixAttempts }
        let recovered = records.reduce(0) { $0 + $1.counts.recovered }
        let firstPass = records.compactMap { record in record.firstPassMs.map { $0 - record.startMs } }
        tasks = records.count
        scoredTasks = scored.count
        medianScore = TaskComparison.quantile(scores, 0.5)
        medianShown = medianScore.map(taskRoundHalfAway)
        lowerQuartile = TaskComparison.quantile(scores, 0.25)
        upperQuartile = TaskComparison.quantile(scores, 0.75)
        histogram = TaskComparison.histogram(scores)
        successes = scored.filter(\.success).count
        stalls = records.filter(\.stalled).count
        fixFailures = records.reduce(0) { $0 + $1.counts.fixFailures }
        self.fixAttempts = fixAttempts
        self.recovered = recovered
        successRate = scored.isEmpty ? nil : taskDivide(successes, scored.count)
        stallRate = records.isEmpty ? nil : taskDivide(stalls, records.count)
        fixFailureRate = fixAttempts == 0 ? nil : taskDivide(fixFailures, fixAttempts)
        recoverAttempts = recovered == 0 ? nil
            : taskDivide(records.reduce(0) { $0 + $1.counts.recoverAttempts }, recovered)
        timeToFirstPassMs = TaskComparison.quantile(firstPass, 0.5)
    }
}

/// The task performance comparison of the Usage History window: per source in source order, and per model by tasks
/// descending, then the id's UTF-8 bytes.
public struct TaskComparison: Equatable, Sendable {
    public var bySource: [TaskComparisonRow]
    public var byModel: [TaskComparisonRow]

    public init(bySource: [TaskComparisonRow], byModel: [TaskComparisonRow]) {
        self.bySource = bySource
        self.byModel = byModel
    }

    static let histogramEdges: [Double] = [10, 20, 30, 40, 50, 60, 70, 80, 90]

    /// The complete records whose `day` lies in [`first`, `last`], of every source or only of `source`.
    public static func compare(_ records: [TaskRecord], first: String, last: String,
                               source: SourceKind?) -> TaskComparison {
        let chosen = records.filter { record in
            record.complete && !TaskPythonText.precedes(record.day, first) && !TaskPythonText.precedes(last, record.day)
                && (source == nil || source == record.source)
        }
        var bySource: [TaskComparisonRow] = []
        for kind in SourceKind.allCases {
            let part = chosen.filter { $0.source == kind }
            if !part.isEmpty { bySource.append(TaskComparisonRow(source: kind, model: nil, sources: [], records: part)) }
        }
        var groups: [(model: String, records: [TaskRecord])] = []
        var positions: [TaskOrdinalKey: Int] = [:]
        for record in chosen {
            let key = TaskOrdinalKey(record.model ?? "")
            if let position = positions[key] {
                groups[position].records.append(record)
            } else {
                positions[key] = groups.count
                groups.append((key.text, [record]))
            }
        }
        let byModel = groups.map { group in
            TaskComparisonRow(source: nil, model: group.model,
                              sources: SourceKind.allCases.filter { kind in group.records.contains { $0.source == kind } },
                              records: group.records)
        }.sorted { lhs, rhs in
            lhs.tasks != rhs.tasks ? lhs.tasks > rhs.tasks : TaskPythonText.precedes(lhs.model ?? "", rhs.model ?? "")
        }
        return TaskComparison(bySource: bySource, byModel: byModel)
    }

    /// Linear interpolation between order statistics, `h = (n − 1)·p`; nil for no values.
    static func quantile(_ values: [Double], _ p: Double) -> Double? {
        let sorted = values.sorted()
        guard !sorted.isEmpty else { return nil }
        let h = Double(sorted.count - 1) * p
        let i = Int(h.rounded(.down))
        let fraction = h - Double(i)
        return fraction == 0 ? sorted[i] : sorted[i] + fraction * (sorted[i + 1] - sorted[i])
    }

    /// The same over integers (Python keeps them integers until the interpolation).
    static func quantile(_ values: [Int64], _ p: Double) -> Double? {
        let sorted = values.sorted()
        guard !sorted.isEmpty else { return nil }
        let h = Double(sorted.count - 1) * p
        let i = Int(h.rounded(.down))
        let fraction = h - Double(i)
        return fraction == 0 ? Double(sorted[i]) : Double(sorted[i]) + fraction * Double(sorted[i + 1] - sorted[i])
    }

    /// A score's bin is the number of the edges 10 … 90 that it reaches, compared exactly.
    static func histogram(_ scores: [Double]) -> [Int] {
        var bins = [Int](repeating: 0, count: histogramEdges.count + 1)
        for score in scores { bins[histogramEdges.reduce(0) { $0 + (score >= $1 ? 1 : 0) }] += 1 }
        return bins
    }
}
