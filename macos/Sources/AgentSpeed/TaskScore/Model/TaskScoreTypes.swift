import AgentSpeedCore

/// The state of a task as the surfaces draw it (METRICS 6.9 *States*).
enum TaskScoreState: String, CaseIterable, Sendable {
    case none, unverified, failing, passing, pending

    /// `unverified` and `pending`: the number has no verified result behind it, so it carries the proof mark
    /// (DESIGN 12, *Proof mark*).
    var isUnproven: Bool { self == .unverified || self == .pending }

    /// No verification result yet: the card's V, C, R and K show `task.state.unverified` over dotted tracks.
    var hasNoResult: Bool { self == .none || self == .unverified }

    var titleKey: String { "task.state.\(rawValue)" }
}

/// The form of a value (METRICS 6.9 *Values*): live in the surface's number color, resting pale, none `–`.
enum TaskScoreForm: String, CaseIterable, Sendable {
    case live, resting, none
}

/// The class a check is named by when the parsers know no suite for it.
enum TaskCheckClass: String, CaseIterable, Sendable {
    case test, build, check

    var titleKey: String { "task.check.\(rawValue)" }
}

/// A check as the card names it: its fixed `suite` (`cargo test`, `pytest`, …), else its class. Never command text.
struct TaskCheckName: Equatable, Sendable {
    var suite: String?
    var kind: TaskCheckClass

    init(suite: String?, kind: TaskCheckClass) {
        self.suite = suite?.isEmpty == true ? nil : suite
        self.kind = kind
    }
}

/// One surface's value: an agent's own, or a set's or group's highest (METRICS 6.9 *Values*, *Sets and groups*).
struct TaskScoreValue: Equatable, Sendable {
    var form: TaskScoreForm
    /// The number as shown, rounded half away from zero; nil while none.
    var shown: Int?
    /// The supplying agent's state: the proof mark follows it.
    var state: TaskScoreState?
    /// N, the set's agents with a score in that form (1 for an agent of its own).
    var count: Int
    /// The supplying agent, named by tooltips and the card.
    var agentId: String?

    static let none = TaskScoreValue(form: .none, shown: nil, state: nil, count: 0, agentId: nil)

    var isUnproven: Bool { form != .none && state?.isUnproven == true }
}

/// The five dimensions (METRICS 6.9 *Dimensions*), unrounded, in the order V C R E K.
struct TaskScoreDimensions: Equatable, Sendable {
    var verify: Double
    var converge: Double
    var recover: Double
    var efficiency: Double
    var complete: Double

    static let zero = TaskScoreDimensions(verify: 0, converge: 0, recover: 0, efficiency: 0, complete: 0)

    subscript(_ dimension: TaskScoreDimension) -> Double {
        switch dimension {
        case .verify: verify
        case .converge: converge
        case .recover: recover
        case .efficiency: efficiency
        case .complete: complete
        }
    }
}

enum TaskScoreDimension: Int, CaseIterable, Sendable {
    case verify, converge, recover, efficiency, complete

    var maximum: Int {
        switch self {
        case .verify: 40
        case .converge: 25
        case .recover: 15
        case .efficiency: 10
        case .complete: 10
        }
    }

    var titleKey: String {
        switch self {
        case .verify: "task.dim.verify"
        case .converge: "task.dim.converge"
        case .recover: "task.dim.recover"
        case .efficiency: "task.dim.efficiency"
        case .complete: "task.dim.complete"
        }
    }

    var meaningKey: String { titleKey + ".desc" }
}

/// `openFailure` (METRICS 6.9 *Task facts*): `failing` only.
struct TaskOpenFailureFacts: Equatable, Sendable {
    var check: TaskCheckName
    /// Its `F`.
    var errors: Int
    /// Its check's `F_peak`.
    var peak: Int
    /// `F` comes from fingerprints or a `failedCount` rather than the minimum of 1.
    var counted: Bool
    /// An effective edit followed it.
    var edited: Bool
}

/// `lastPass` (METRICS 6.9 *Task facts*): `passing` and `pending` only.
struct TaskLastPassFacts: Equatable, Sendable {
    var check: TaskCheckName
    /// Its block has no passing test run (V, or the held credit, is 20).
    var buildOnly: Bool
}

/// Whether a prediction's model was calibrated on the user's own tasks, and on how many (METRICS 6.11 *Calibration*).
struct TaskCalibration: Equatable, Sendable {
    /// Tasks below which a model always keeps its prior.
    static let minimumTasks = 40

    var calibrated: Bool
    /// N, the tasks with at least one row for that model.
    var tasks: Int
}

struct TaskScorePrediction: Equatable, Sendable {
    /// From 0 to 1.
    var probability: Double
    var calibration: TaskCalibration
}

/// METRICS 6.11 *Predictions*: success and stall always, fix fails only while `failing`.
struct TaskScorePredictions: Equatable, Sendable {
    var success: TaskScorePrediction
    var stall: TaskScorePrediction
    var fixFails: TaskScorePrediction?
}
