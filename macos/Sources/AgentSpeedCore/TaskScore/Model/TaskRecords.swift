import Foundation

/// A snapshot row for the success and stall models (METRICS 6.11 *Snapshots*).
public struct TaskSnapshotRow: Equatable, Sendable {
    public var n: Int
    public var passing: Double
    public var failing: Double
    public var conv: Double
    public var ig: Double
    public var loop: Double
    public var osc: Double
    public var size: Double
    public var yStall: Int

    public init(n: Int, passing: Double, failing: Double, conv: Double, ig: Double, loop: Double, osc: Double,
                size: Double, yStall: Int) {
        self.n = n
        self.passing = passing
        self.failing = failing
        self.conv = conv
        self.ig = ig
        self.loop = loop
        self.osc = osc
        self.size = size
        self.yStall = yStall
    }

    init(n: Int, features: TaskSnapshotFeatures, yStall: Int) {
        self.init(n: n, passing: features.passing, failing: features.failing, conv: features.conv, ig: features.ig,
                  loop: features.loop, osc: features.osc, size: features.size, yStall: yStall)
    }

    /// `[n, passing, failing, conv, ig, loop, osc, size, yStall]`, as stored.
    public var values: [Double] { [Double(n), passing, failing, conv, ig, loop, osc, size, Double(yStall)] }

    /// From stored values; nil unless there are nine and `n` and `yStall` are integers.
    public init?(values: [Double]) {
        guard values.count == 9, let n = Int(exactly: values[0]), let yStall = Int(exactly: values[8]) else {
            return nil
        }
        self.init(n: n, passing: values[1], failing: values[2], conv: values[3], ig: values[4], loop: values[5],
                  osc: values[6], size: values[7], yStall: yStall)
    }

    var features: TaskSnapshotFeatures {
        TaskSnapshotFeatures(passing: passing, failing: failing, conv: conv, ig: ig, loop: loop, osc: osc, size: size)
    }
}

/// A fix row for the fix model (METRICS 6.11 *Fix rows*).
public struct TaskFixRow: Equatable, Sendable {
    public var attempts: Double
    public var errors: Double
    public var trend: Double
    public var osc: Double
    public var ig: Double
    public var yFix: Int

    public init(attempts: Double, errors: Double, trend: Double, osc: Double, ig: Double, yFix: Int) {
        self.attempts = attempts
        self.errors = errors
        self.trend = trend
        self.osc = osc
        self.ig = ig
        self.yFix = yFix
    }

    init(_ features: TaskFixFeatures, yFix: Int) {
        self.init(attempts: features.attempts, errors: features.errors, trend: features.trend, osc: features.osc,
                  ig: features.ig, yFix: yFix)
    }

    /// `[attempts, errors, trend, osc, ig, yFix]`, as stored.
    public var values: [Double] { [attempts, errors, trend, osc, ig, Double(yFix)] }

    /// From stored values; nil unless there are six and `yFix` is an integer.
    public init?(values: [Double]) {
        guard values.count == 6, let yFix = Int(exactly: values[5]) else { return nil }
        self.init(attempts: values[0], errors: values[1], trend: values[2], osc: values[3], ig: values[4], yFix: yFix)
    }

    var features: TaskFixFeatures {
        TaskFixFeatures(attempts: attempts, errors: errors, trend: trend, osc: osc, ig: ig)
    }
}

/// The record of one task with at least one action (METRICS 6.11 *Task records*).
public struct TaskRecord: Equatable, Sendable {
    public var agentId: String
    public var source: SourceKind
    /// The local date of `endMs` (8).
    public var day: String
    /// The record's index among the agent's records, from 0.
    public var ordinal: Int
    public var startMs: Int64
    public var endMs: Int64
    public var complete: Bool
    public var model: String?
    public var effort: String?
    public var state: TaskState
    public var score: Double?
    public var dims: TaskDimensions
    public var counts: TaskCounts
    public var firstPassMs: Int64?
    public var success: Bool
    public var stalled: Bool
    public var snapshots: [TaskSnapshotRow]
    public var fixRows: [TaskFixRow]

    public init(agentId: String, source: SourceKind, day: String, ordinal: Int, startMs: Int64, endMs: Int64,
                complete: Bool, model: String? = nil, effort: String? = nil, state: TaskState = .none,
                score: Double? = nil, dims: TaskDimensions = .zero, counts: TaskCounts = TaskCounts(),
                firstPassMs: Int64? = nil, success: Bool = false, stalled: Bool = false,
                snapshots: [TaskSnapshotRow] = [], fixRows: [TaskFixRow] = []) {
        self.agentId = agentId
        self.source = source
        self.day = day
        self.ordinal = ordinal
        self.startMs = startMs
        self.endMs = endMs
        self.complete = complete
        self.model = model
        self.effort = effort
        self.state = state
        self.score = score
        self.dims = dims
        self.counts = counts
        self.firstPassMs = firstPassMs
        self.success = success
        self.stalled = stalled
        self.snapshots = snapshots
        self.fixRows = fixRows
    }

    static let snapshotSizes = [1, 2, 4, 8, 16, 32, 64, 128, 256, 512]
    static let completeIdleMs: Int64 = 30 * 60_000

    /// `analysisMs − lastLineMs ≥ 30 min`, without overflowing.
    static func idle(_ analysisMs: Int64, since lastLineMs: Int64) -> Bool {
        let (gap, overflow) = analysisMs.subtractingReportingOverflow(lastLineMs)
        return overflow ? analysisMs > lastLineMs : gap >= completeIdleMs
    }

    /// The records of one agent's complete stream (history analysis): every task with at least one action. `day`
    /// gives the local date of a task's `endMs`.
    public static func records(events: [TaskEvent], analysisMs: Int64, lastLineMs: Int64, models: [TaskModelNote],
                               agentId: String, source: SourceKind, day: (Int64) -> String) -> [TaskRecord] {
        let tasks = TaskSplit.split(events)
        var records: [TaskRecord] = []
        for (index, task) in tasks.enumerated() {
            guard task.events.contains(where: { !$0.isPrompt }) else { continue }
            let evaluation = TaskEvaluation(task.events, now: nil)
            let end = TaskSplit.end(task)
            var model: String?
            var effort: String?
            for note in models where note.t <= end {
                model = note.model
                effort = note.effort
            }
            let lastStall = evaluation.stallEnds.last ?? 0
            var snapshots: [TaskSnapshotRow] = []
            for n in snapshotSizes {
                guard n <= evaluation.counted.count else { break }
                let through = evaluation.acts[evaluation.counted[n - 1]].event
                let prefix = TaskEvaluation(task.events.prefix(through + 1), now: nil)
                guard prefix.state != .none else { continue }
                snapshots.append(TaskSnapshotRow(n: n, features: TaskSnapshotFeatures(prefix),
                                                 yStall: lastStall > n ? 1 : 0))
            }
            let fixRows = evaluation.allFixFeatures().map { TaskFixRow($0.features, yFix: evaluation.fixLabel($0.run)) }
            records.append(TaskRecord(
                agentId: agentId, source: source, day: day(end), ordinal: records.count, startMs: task.startMs,
                endMs: end, complete: index < tasks.count - 1 || idle(analysisMs, since: lastLineMs),
                model: model, effort: effort, state: evaluation.state, score: evaluation.score, dims: evaluation.dims,
                counts: evaluation.counts, firstPassMs: evaluation.firstPassMs, success: evaluation.state == .passing,
                stalled: !evaluation.stallEnds.isEmpty, snapshots: snapshots, fixRows: fixRows))
        }
        return records
    }
}
