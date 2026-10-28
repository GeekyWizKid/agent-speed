import AgentSpeedCore

/// The aggregates of one source or one model over the window's range (METRICS 6.11 *Comparison*).
struct TaskPerformanceStats: Equatable, Sendable {
    var tasks: Int
    var scoredTasks: Int
    /// Unrounded quantiles of the scored tasks' scores; nil without a scored task.
    var medianScore: Double?
    var lowerQuartile: Double?
    var upperQuartile: Double?
    /// Ten counts: `[0, 10)` … `[80, 90)` and `[90, 100]`.
    var histogram: [Int]
    var successRate: Double?
    var stallRate: Double?
    var fixFailureRate: Double?
    /// The mean attempts of a recovered episode.
    var recoverAttempts: Double?
    var timeToFirstPassMs: Double?
    /// The rates' numerators and the fix-failure denominator, for their tooltips (`history.tasks.rate.*`); nil when the
    /// data does not carry them.
    var successes: Int?
    var stalled: Int?
    var fixFailures: Int?
    var fixAttempts: Int?

    static let empty = TaskPerformanceStats(
        tasks: 0, scoredTasks: 0, medianScore: nil, lowerQuartile: nil, upperQuartile: nil,
        histogram: Array(repeating: 0, count: 10), successRate: nil, stallRate: nil, fixFailureRate: nil,
        recoverAttempts: nil, timeToFirstPassMs: nil
    )
}

/// `bySource`: one source with records in the range.
struct TaskPerformanceSource: Equatable, Sendable, Identifiable {
    var source: SourceKind
    var stats: TaskPerformanceStats

    var id: SourceKind { source }
}

/// `byModel`: one model id across the sources, "" for an unknown model.
struct TaskPerformanceModelRow: Equatable, Sendable, Identifiable {
    var model: String
    /// The sources with records of that model, in source order.
    var sources: [SourceKind]
    var stats: TaskPerformanceStats

    var id: String { model }
}

/// The comparison over one range: per source in source order, per model by tasks descending, then the id's UTF-8 bytes.
struct TaskPerformanceComparison: Equatable, Sendable {
    var bySource: [TaskPerformanceSource]
    var byModel: [TaskPerformanceModelRow]

    static let empty = TaskPerformanceComparison(bySource: [], byModel: [])

    func stats(for source: SourceKind) -> TaskPerformanceStats? {
        bySource.first { $0.source == source }?.stats
    }
}

/// What the Usage History window hands the 任務表現 section (DESIGN 12, *Usage History: task performance*).
struct TaskPerformanceInput: Equatable, Sendable {
    /// Over every scored source, or only over `narrowedTo`.
    var comparison: TaskPerformanceComparison
    /// The window's source picker, when one source is chosen.
    var narrowedTo: SourceKind?
    /// `history.analyzing` runs (the first launch re-analyses every log).
    var analyzing = false
}

/// The two sources compared side by side; the window remembers it.
struct TaskPerformancePair: Equatable, Sendable {
    var first: SourceKind
    var second: SourceKind
}
