import Foundation

/// The state of a task (METRICS 6.9 *States*).
public enum TaskState: String, Sendable, CaseIterable {
    case none, unverified, passing, failing, pending
}

/// The five dimensions of the score (METRICS 6.9 *Dimensions*).
public struct TaskDimensions: Equatable, Hashable, Sendable {
    public var v: Double
    public var c: Double
    public var r: Double
    public var e: Double
    public var k: Double

    public init(v: Double, c: Double, r: Double, e: Double, k: Double) {
        self.v = v
        self.c = c
        self.r = r
        self.e = e
        self.k = k
    }

    public static let zero = TaskDimensions(v: 0, c: 0, r: 0, e: 0, k: 0)
}

/// The counts of a task (METRICS 6.11 *Task records*).
public struct TaskCounts: Equatable, Hashable, Sendable {
    public var actions = 0
    public var counted = 0
    public var edits = 0
    public var oscillations = 0
    public var loopActions = 0
    public var runs = 0
    public var failingRuns = 0
    public var episodes = 0
    public var recovered = 0
    public var recoverAttempts = 0
    public var fixAttempts = 0
    public var fixFailures = 0

    public init(actions: Int = 0, counted: Int = 0, edits: Int = 0, oscillations: Int = 0, loopActions: Int = 0,
                runs: Int = 0, failingRuns: Int = 0, episodes: Int = 0, recovered: Int = 0, recoverAttempts: Int = 0,
                fixAttempts: Int = 0, fixFailures: Int = 0) {
        self.actions = actions
        self.counted = counted
        self.edits = edits
        self.oscillations = oscillations
        self.loopActions = loopActions
        self.runs = runs
        self.failingRuns = failingRuns
        self.episodes = episodes
        self.recovered = recovered
        self.recoverAttempts = recoverAttempts
        self.fixAttempts = fixAttempts
        self.fixFailures = fixFailures
    }

    /// The JSON keys, in the reference's order.
    public static let names = ["actions", "counted", "edits", "oscillations", "loopActions", "runs", "failingRuns",
                               "episodes", "recovered", "recoverAttempts", "fixAttempts", "fixFailures"]

    /// The values in the order of `names`.
    public var values: [Int] {
        [actions, counted, edits, oscillations, loopActions, runs, failingRuns, episodes, recovered, recoverAttempts,
         fixAttempts, fixFailures]
    }

    /// From values in the order of `names`; nil unless there are exactly twelve.
    public init?(values: [Int]) {
        guard values.count == Self.names.count else { return nil }
        self.init(actions: values[0], counted: values[1], edits: values[2], oscillations: values[3],
                  loopActions: values[4], runs: values[5], failingRuns: values[6], episodes: values[7],
                  recovered: values[8], recoverAttempts: values[9], fixAttempts: values[10], fixFailures: values[11])
    }
}

/// `openFailure` (METRICS 6.9 *Task facts*): the newest failing run that is still the last element of its check.
public struct TaskOpenFailure: Equatable, Hashable, Sendable {
    public var suite: String?
    public var commandClass: TaskCommandClass
    public var key: TaskDigest
    /// Its error count `F`.
    public var errors: Int
    /// Its check's `F_peak`.
    public var peak: Int
    /// `F` comes from fingerprints or a `failedCount` above 0 rather than the minimum of 1.
    public var counted: Bool
    /// An effective edit followed it.
    public var edited: Bool

    public init(suite: String?, commandClass: TaskCommandClass, key: TaskDigest, errors: Int, peak: Int,
                counted: Bool, edited: Bool) {
        self.suite = suite
        self.commandClass = commandClass
        self.key = key
        self.errors = errors
        self.peak = peak
        self.counted = counted
        self.edited = edited
    }
}

/// `lastPass` (METRICS 6.9 *Task facts*): the last run of the current block (passing) or of the latest non-empty
/// block (pending).
public struct TaskLastPass: Equatable, Hashable, Sendable {
    public var suite: String?
    public var commandClass: TaskCommandClass
    public var key: TaskDigest
    /// That block has no passing test run.
    public var buildOnly: Bool

    public init(suite: String?, commandClass: TaskCommandClass, key: TaskDigest, buildOnly: Bool) {
        self.suite = suite
        self.commandClass = commandClass
        self.key = key
        self.buildOnly = buildOnly
    }
}

/// `task_result` of `task_score.py`: everything 6.9 gives about one task at one instant.
public struct TaskResult: Equatable, Sendable {
    public var state: TaskState
    public var score: Double?
    /// The score rounded half away from zero.
    public var shown: Int?
    public var dims: TaskDimensions
    public var ig: Double
    public var loop: Double
    public var osc: Double
    public var counts: TaskCounts
    public var firstPassMs: Int64?
    /// 1-based counted indices that end a stall window.
    public var stallEnds: [Int]
    public var vHeld: Double?
    public var openFailure: TaskOpenFailure?
    public var lastPass: TaskLastPass?

    public init(_ evaluation: TaskEvaluation) {
        state = evaluation.state
        score = evaluation.score
        shown = evaluation.score.map(taskRoundHalfAway)
        dims = evaluation.dims
        ig = evaluation.ig
        loop = evaluation.loop
        osc = evaluation.osc
        counts = evaluation.counts
        firstPassMs = evaluation.firstPassMs
        stallEnds = evaluation.stallEnds
        vHeld = evaluation.vHeld
        openFailure = evaluation.openFailure
        lastPass = evaluation.lastPass
    }
}
