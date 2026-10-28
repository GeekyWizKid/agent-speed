import Foundation

/// The form of a value (METRICS 6.9 *Values*).
public enum TaskValueForm: String, Sendable, CaseIterable {
    case live, resting, none

    /// live > resting > none.
    var rank: Int {
        switch self {
        case .live: 2
        case .resting: 1
        case .none: 0
        }
    }
}

/// One agent's value: its current task's score, live or resting by its phase (`agent_value`).
public struct TaskAgentValue: Equatable, Sendable {
    public var form: TaskValueForm
    public var score: Double?
    public var shown: Int?
    /// The current task's state; nil without a current task.
    public var state: TaskState?

    public init(form: TaskValueForm, score: Double?, shown: Int?, state: TaskState?) {
        self.form = form
        self.score = score
        self.shown = shown
        self.state = state
    }

    /// From an evaluation of the agent's current task (nil without one); `idle` is the phase after staleness.
    public init(evaluation: TaskEvaluation?, idle: Bool) {
        guard let evaluation, let score = evaluation.score else {
            self.init(form: .none, score: nil, shown: nil, state: evaluation?.state)
            return
        }
        self.init(form: idle ? .resting : .live, score: score, shown: taskRoundHalfAway(score), state: evaluation.state)
    }

    /// The value of an agent's whole stream at `now`.
    public static func agentValue(events: [TaskEvent], idle: Bool, now: Int64?) -> TaskAgentValue {
        let evaluation = TaskSplit.current(events, now: now).map { TaskEvaluation($0.events, now: now) }
        return TaskAgentValue(evaluation: evaluation, idle: idle)
    }
}

/// The value of a set or group: the highest live score, else the highest resting score, else none; `agentId` names
/// the agent that supplied it (`highest`).
public struct TaskSetValue: Equatable, Sendable {
    public var form: TaskValueForm
    public var score: Double?
    public var shown: Int?
    public var agentId: String?
    /// The supplying agent's `lastLineMs`, kept so values merge.
    public var lastLineMs: Int64?

    public init(form: TaskValueForm, score: Double?, shown: Int?, agentId: String?, lastLineMs: Int64?) {
        self.form = form
        self.score = score
        self.shown = shown
        self.agentId = agentId
        self.lastLineMs = lastLineMs
    }

    public static let empty = TaskSetValue(form: .none, score: nil, shown: nil, agentId: nil, lastLineMs: nil)

    /// One agent's value as a set of one.
    public init(id: String, lastLineMs: Int64?, value: TaskAgentValue) {
        guard value.form != .none, let score = value.score else {
            self = .empty
            return
        }
        self.init(form: value.form, score: score, shown: taskRoundHalfAway(score), agentId: id, lastLineMs: lastLineMs)
    }

    /// Ties on the score go to the latest `lastLineMs` (missing counts as 0), then to the smallest id (UTF-16
    /// ordinal); on a complete tie the first member wins.
    public static func highest(_ members: [(id: String, lastLineMs: Int64?, value: TaskAgentValue)]) -> TaskSetValue {
        members.reduce(.empty) { $0.merged(with: TaskSetValue(id: $1.id, lastLineMs: $1.lastLineMs, value: $1.value)) }
    }

    /// The value of the union of two disjoint sets: associative, so per-source values combine in any grouping.
    public func merged(with other: TaskSetValue) -> TaskSetValue {
        other.supersedes(self) ? other : self
    }

    private func supersedes(_ other: TaskSetValue) -> Bool {
        if form.rank != other.form.rank { return form.rank > other.form.rank }
        guard form != .none, let score, let otherScore = other.score else { return false }
        if score != otherScore { return score > otherScore }
        let latest = lastLineMs ?? 0
        let otherLatest = other.lastLineMs ?? 0
        if latest != otherLatest { return latest > otherLatest }
        guard let agentId, let otherId = other.agentId else { return false }
        return AgentParents.ordinal(agentId, precedes: otherId)
    }
}
