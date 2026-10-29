import Foundation

/// The success and stall features of a 6.9 evaluation (METRICS 6.11 *Features*).
public struct TaskSnapshotFeatures: Equatable, Sendable {
    public var passing: Double
    public var failing: Double
    public var conv: Double
    public var ig: Double
    public var loop: Double
    public var osc: Double
    public var size: Double

    public init(passing: Double, failing: Double, conv: Double, ig: Double, loop: Double, osc: Double, size: Double) {
        self.passing = passing
        self.failing = failing
        self.conv = conv
        self.ig = ig
        self.loop = loop
        self.osc = osc
        self.size = size
    }

    public init(_ evaluation: TaskEvaluation) {
        passing = evaluation.state == .passing ? 1.0 : 0.0
        failing = evaluation.state == .failing ? 1.0 : 0.0
        conv = evaluation.dims.c / TaskEvaluation.cMax
        ig = evaluation.ig
        loop = evaluation.loop
        osc = evaluation.osc
        size = Self.size(evaluation.counted.count)
    }

    /// `min(1, log2(1 + n) / 9)`.
    static func size(_ n: Int) -> Double {
        let value = log2(Double(1 + n)) / 9
        return value < 1.0 ? value : 1.0
    }

    /// The success model's features, in coefficient order.
    public var successValues: [Double] { [passing, failing, conv, ig, loop, osc, size] }
    /// The stall model's features, in coefficient order.
    public var stallValues: [Double] { [ig, loop, osc, failing, size] }
}

/// The fix features of a failing run as they stood right after it (METRICS 6.11 *Features*).
public struct TaskFixFeatures: Equatable, Sendable {
    public var attempts: Double
    public var errors: Double
    public var trend: Double
    public var osc: Double
    public var ig: Double

    public init(attempts: Double, errors: Double, trend: Double, osc: Double, ig: Double) {
        self.attempts = attempts
        self.errors = errors
        self.trend = trend
        self.osc = osc
        self.ig = ig
    }

    /// In coefficient order.
    public var values: [Double] { [attempts, errors, trend, osc, ig] }
}

/// P(success), P(stall) and P(fix fails) of an agent's current task (METRICS 6.11 *Predictions*).
public struct TaskPredictions: Equatable, Sendable {
    public var success: Double
    public var stall: Double
    /// `failing` only.
    public var fix: Double?
    /// Percentages rounded half away from zero.
    public var successShown: Int
    public var stallShown: Int
    public var fixShown: Int?
    public var features: TaskSnapshotFeatures
    public var fixFeatures: TaskFixFeatures?

    public init(success: Double, stall: Double, fix: Double?, features: TaskSnapshotFeatures,
                fixFeatures: TaskFixFeatures?) {
        self.success = success
        self.stall = stall
        self.fix = fix
        successShown = taskRoundHalfAway(success * 100)
        stallShown = taskRoundHalfAway(stall * 100)
        fixShown = fix.map { taskRoundHalfAway($0 * 100) }
        self.features = features
        self.fixFeatures = fixFeatures
    }

    /// The predictions of an agent's current task within its whole stream; nil while the task has no score.
    public static func predictions(events: [TaskEvent], now: Int64?, models: TaskModels) -> TaskPredictions? {
        guard let task = TaskSplit.current(events, now: now) else { return nil }
        return predictions(taskEvents: task.events, now: now, models: models)
    }

    /// The predictions from the current task's events alone (what the live engine keeps).
    public static func predictions(taskEvents: some Sequence<TaskEvent>, now: Int64?,
                                   models: TaskModels) -> TaskPredictions? {
        predictions(evaluation: TaskEvaluation(taskEvents, now: now), models: models)
    }

    /// The predictions from an evaluation of the current task.
    public static func predictions(evaluation: TaskEvaluation, models: TaskModels) -> TaskPredictions? {
        guard evaluation.score != nil else { return nil }
        let features = TaskSnapshotFeatures(evaluation)
        var fix: Double?
        var fixFeatures: TaskFixFeatures?
        if evaluation.state == .failing, let run = evaluation.openFailureRun {
            let found = evaluation.fixFeatures(run: run)
            fix = models.fix.probability(found.values)
            fixFeatures = found
        }
        return TaskPredictions(success: models.success.probability(features.successValues),
                               stall: models.stall.probability(features.stallValues), fix: fix, features: features,
                               fixFeatures: fixFeatures)
    }
}
