import Foundation

/// The 6.11 fields of one completed task that live reporting counts (METRICS 11.10 *Counting*): numbers only.
public struct ReportTaskRecord: Equatable, Sendable {
    /// Actions of the task; a task without any has no 6.11 record and adds nothing.
    public var actions: Int
    public var score: Double?
    public var success: Bool
    public var stalled: Bool
    public var fixAttempts: Int
    public var fixFailures: Int
    public var recovered: Int
    public var recoverAttempts: Int

    public init(actions: Int, score: Double?, success: Bool, stalled: Bool, fixAttempts: Int = 0, fixFailures: Int = 0,
                recovered: Int = 0, recoverAttempts: Int = 0) {
        self.actions = actions
        self.score = score
        self.success = success
        self.stalled = stalled
        self.fixAttempts = fixAttempts
        self.fixFailures = fixFailures
        self.recovered = recovered
        self.recoverAttempts = recoverAttempts
    }

    /// A task's events read so far, evaluated as its 6.11 record is (`success` is a passing state).
    init(events: some Sequence<TaskEvent>) {
        let evaluation = TaskEvaluation(events, now: nil)
        let counts = evaluation.counts
        self.init(actions: counts.actions, score: evaluation.score, success: evaluation.state == .passing,
                  stalled: !evaluation.stallEnds.isEmpty, fixAttempts: counts.fixAttempts,
                  fixFailures: counts.fixFailures, recovered: counts.recovered, recoverAttempts: counts.recoverAttempts)
    }
}

/// One `taskStats` row (METRICS 11.10 *Task statistics*): the tasks of one model and source counted since the
/// previous build.
public struct ReportTaskRow: Equatable, Sendable {
    public static let binCount = 10

    public var completed = 0
    public var scored = 0
    /// Σ of the scores rounded half away from zero, as shown.
    public var scoreSum = 0
    /// `[0, 10)` … `[80, 90)` and `[90, 100]` over the rounded scores.
    public var scoreBins = [Int](repeating: 0, count: ReportTaskRow.binCount)
    public var successes = 0
    public var stalls = 0
    public var fixAttempts = 0
    public var fixFailures = 0
    public var recovered = 0
    public var recoverAttempts = 0

    public init() {}

    /// What one counted task adds; nil for a task without actions.
    public init?(_ record: ReportTaskRecord) {
        guard record.actions >= 1 else { return nil }
        completed = 1
        if let score = record.score {
            let shown = taskRoundHalfAway(score)
            scored = 1
            scoreSum = shown
            scoreBins[min(Self.binCount - 1, max(0, shown / 10))] = 1
            successes = record.success ? 1 : 0
        }
        stalls = record.stalled ? 1 : 0
        fixAttempts = record.fixAttempts
        fixFailures = record.fixFailures
        recovered = record.recovered
        recoverAttempts = record.recoverAttempts
    }

    public mutating func add(_ other: ReportTaskRow) {
        completed += other.completed
        scored += other.scored
        scoreSum += other.scoreSum
        for index in scoreBins.indices { scoreBins[index] += other.scoreBins[index] }
        successes += other.successes
        stalls += other.stalls
        fixAttempts += other.fixAttempts
        fixFailures += other.fixFailures
        recovered += other.recovered
        recoverAttempts += other.recoverAttempts
    }

    func json(source: SourceKind) -> ReportJSON {
        .object([
            "source": .string(source.rawValue), "completed": .int(completed), "scored": .int(scored),
            "scoreSum": .int(scoreSum), "scoreBins": .array(scoreBins.map { .int($0) }), "successes": .int(successes),
            "stalls": .int(stalls), "fixAttempts": .int(fixAttempts), "fixFailures": .int(fixFailures),
            "recovered": .int(recovered), "recoverAttempts": .int(recoverAttempts),
        ])
    }
}

/// Task rows per resolved model id and source.
public typealias ReportTaskStats = [String: [SourceKind: ReportTaskRow]]

/// The task ledger of METRICS 11.10: the rows counted since the previous build. A build with the extension active
/// takes it whole; otherwise only a break empties it. Never persisted.
public struct ReportTaskLedger: Equatable, Sendable {
    /// Completed tasks one report carries at most; later completions are dropped, not deferred.
    public static let maxCompleted = 1000

    public private(set) var rows: ReportTaskStats = [:]
    public private(set) var completed = 0

    public init() {}

    public var isEmpty: Bool { rows.isEmpty }

    /// Counts one completed task's row under its model and source; false when the ledger is full.
    @discardableResult
    public mutating func add(_ row: ReportTaskRow, model: String, source: SourceKind) -> Bool {
        guard completed < Self.maxCompleted else { return false }
        rows[model, default: [:]][source, default: ReportTaskRow()].add(row)
        completed += row.completed
        return true
    }

    /// A build: the rows, and an empty ledger.
    public mutating func take() -> ReportTaskStats {
        defer { clear() }
        return rows
    }

    /// A break.
    public mutating func clear() {
        rows = [:]
        completed = 0
    }
}

/// One completion as the engine publishes it (METRICS 11.10 *Counting*): the task's row under the resolved id of its
/// model and its agent's source, with the watch epoch the task was watched in.
public struct ReportTaskCompletion: Equatable, Sendable {
    /// Position in the engine's log, from 1.
    public var index: Int
    public var epoch: Int
    public var modelId: String
    public var source: SourceKind
    public var row: ReportTaskRow

    public init(index: Int, epoch: Int, modelId: String, source: SourceKind, row: ReportTaskRow) {
        self.index = index
        self.epoch = epoch
        self.modelId = modelId
        self.source = source
        self.row = row
    }
}

/// The engine's recent completions. Snapshots carry the latest ones, so a reader that missed a snapshot still takes
/// each completion exactly once (by `index`); `id` tells one engine's log from the next one's.
public struct ReportTaskCompletionLog: Equatable, Sendable {
    public static let capacity = 1024

    public let id: UInt64
    public private(set) var entries: [ReportTaskCompletion] = []
    /// Completions appended so far: the last entry's index.
    public private(set) var count = 0

    public init(id: UInt64 = UInt64.random(in: 1...UInt64.max)) {
        self.id = id
    }

    public mutating func append(epoch: Int, modelId: String, source: SourceKind, row: ReportTaskRow) {
        count += 1
        entries.append(ReportTaskCompletion(index: count, epoch: epoch, modelId: modelId, source: source, row: row))
        if entries.count > Self.capacity { entries.removeFirst(entries.count - Self.capacity) }
    }

    /// The entries after `index`, oldest first.
    public func entries(after index: Int) -> ArraySlice<ReportTaskCompletion> {
        guard let first = entries.firstIndex(where: { $0.index > index }) else { return [] }
        return entries[first...]
    }
}

/// The live values of one resolved model id (METRICS 11.10 *Body*, *Membership*).
public struct ReportTaskModel: Equatable, Sendable {
    /// The set value of the agents whose current model resolves to this id.
    public var value: TaskSetValue
    /// `liveThinking` of the model sums of every raw model that resolves to this id (tok/s, unrounded).
    public var thinking: Double
    /// The sources of the agents live with this id (not idle, with a score).
    public var liveSources: Set<SourceKind>

    public init(value: TaskSetValue = .empty, thinking: Double = 0, liveSources: Set<SourceKind> = []) {
        self.value = value
        self.thinking = thinking
        self.liveSources = liveSources
    }
}

/// What each snapshot carries for the `taskScore` extension (METRICS 11.10): the value and thinking speed of all
/// enabled sources, those of each resolved model id, and the recent completions.
public struct ReportTaskFacts: Equatable, Sendable {
    /// The highest score of every agent of all enabled sources (6.9 *Sets and groups*).
    public var value: TaskSetValue
    /// `liveThinking` of all enabled sources (tok/s, unrounded).
    public var thinking: Double
    public var models: [String: ReportTaskModel]
    public var completions: ReportTaskCompletionLog

    public init(value: TaskSetValue = .empty, thinking: Double = 0, models: [String: ReportTaskModel] = [:],
                completions: ReportTaskCompletionLog = ReportTaskCompletionLog(id: 0)) {
        self.value = value
        self.thinking = thinking
        self.models = models
        self.completions = completions
    }

    public static let empty = ReportTaskFacts()
}

/// A `taskScore` object as sent (METRICS 11.10): the score rounded half away from zero, null exactly when the form is
/// none, and the thinking speed rounded to 0.1.
public struct ReportTaskScoreValue: Equatable, Sendable {
    public var score: Int?
    public var form: TaskValueForm
    public var thinkingTokS: Double

    public init(_ value: TaskSetValue, thinking: Double) {
        let shown = value.form == .none ? nil : value.score.map { min(max(taskRoundHalfAway($0), 0), 100) }
        form = shown == nil ? .none : value.form
        score = shown
        thinkingTokS = ReportBody.rounded(min(max(thinking.isFinite ? thinking : 0, 0), ReportBody.maxTokS), 1)
    }

    var json: ReportJSON {
        .object(["score": score.map { .int($0) } ?? .null, "form": .string(form.rawValue),
                 "thinkingTokS": .decimal(thinkingTokS, decimals: 1)])
    }
}

/// The `taskScore` objects a report would carry (the changed rule of METRICS 11.10 *WebSocket*).
public struct ReportTaskScoreValues: Equatable, Sendable {
    public var top: ReportTaskScoreValue
    /// By entry id, `other` included.
    public var entries: [String: ReportTaskScoreValue]

    public init(top: ReportTaskScoreValue, entries: [String: ReportTaskScoreValue]) {
        self.top = top
        self.entries = entries
    }
}

/// When the extension is active (METRICS 11.10 *Active*).
public enum ReportTaskScoreRule {
    /// The document advertises `taskScore: 1`, lists `models` with `maxModels` ≥ 1, and the user's models switch is on.
    public static func isActive(_ document: ReportDiscovery, switches: Set<ReportSection>) -> Bool {
        document.extensions?.taskScore == true && document.sections.contains(.models) && document.limits.maxModels >= 1
            && switches.contains(.models)
    }

    /// A task whose first event is older than this when it is read is history (METRICS 11.10 *Watch*).
    public static let firstEventAgeMs: Int64 = 120_000

    /// A task is watched when its first event is read while the watch is on and is at most 2 minutes old.
    public static func watches(firstEventMs t: Int64, nowMs: Int64) -> Bool {
        let (age, overflow) = nowMs.subtractingReportingOverflow(t)
        return overflow ? nowMs < t : age <= firstEventAgeMs
    }
}
