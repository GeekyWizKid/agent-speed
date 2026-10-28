import Foundation

/// Whether one prediction model of a source is calibrated (METRICS 6.11 *Predictions*: "prior" or "calibrated on N
/// tasks").
public struct TaskModelFlag: Equatable, Hashable, Sendable {
    public var calibrated: Bool
    /// The tasks the model was (or would be) calibrated on.
    public var tasks: Int

    public init(calibrated: Bool, tasks: Int) {
        self.calibrated = calibrated
        self.tasks = tasks
    }

    public init(_ model: TaskLogisticModel) {
        self.init(calibrated: model.calibrated, tasks: model.tasks)
    }
}

/// The three models' flags, in the order success, stall, fix.
public struct TaskModelFlags: Equatable, Hashable, Sendable {
    public var success: TaskModelFlag
    public var stall: TaskModelFlag
    public var fix: TaskModelFlag

    public init(_ models: TaskModels) {
        success = TaskModelFlag(models.success)
        stall = TaskModelFlag(models.stall)
        fix = TaskModelFlag(models.fix)
    }

    public static let prior = TaskModelFlags(.prior)
}

/// One agent's task score as the score card shows it (METRICS 6.9 *Values*, *Task facts*; 6.11 *Predictions*): its
/// current task's evaluation and predictions. The form (live or resting) follows the agent's phase: `value(idle:)`.
public struct AgentTaskStatus: Equatable, Sendable {
    /// When the current task started; nil without a current task.
    public var taskStartMs: Int64?
    /// The current task's state, score, dimensions and facts; nil without a current task.
    public var result: TaskResult?
    /// nil while the current task has no score.
    public var predictions: TaskPredictions?
    /// Which coefficients the predictions used.
    public var models: TaskModelFlags

    public init(taskStartMs: Int64?, result: TaskResult?, predictions: TaskPredictions?, models: TaskModelFlags) {
        self.taskStartMs = taskStartMs
        self.result = result
        self.predictions = predictions
        self.models = models
    }

    /// No current task.
    public static let empty = AgentTaskStatus(taskStartMs: nil, result: nil, predictions: nil, models: .prior)

    /// The agent's current task among `events` (everything with `t ≤ now`; nil: everything read), evaluated with the
    /// coefficients of its source.
    public init(events: [TaskEvent], now: Int64?, models: TaskModels) {
        guard let task = TaskSplit.current(events, now: now) else {
            self.init(taskStartMs: nil, result: nil, predictions: nil, models: TaskModelFlags(models))
            return
        }
        let evaluation = TaskEvaluation(task.events, now: now)
        self.init(taskStartMs: task.startMs, result: TaskResult(evaluation),
                  predictions: TaskPredictions.predictions(evaluation: evaluation, models: models),
                  models: TaskModelFlags(models))
    }

    /// Live while the agent is not idle (after the staleness rule), resting while idle; none without a score.
    public func value(idle: Bool) -> TaskAgentValue {
        guard let result, let score = result.score else {
            return TaskAgentValue(form: .none, score: nil, shown: nil, state: result?.state)
        }
        return TaskAgentValue(form: idle ? .resting : .live, score: score, shown: taskRoundHalfAway(score),
                              state: result.state)
    }
}

/// Board groups (DESIGN 2c, METRICS 6.9 *Sets and groups*): every root of a list (no parent, or a parent that is not
/// listed) with all its descendants, the root first and then depth first in list order.
enum TaskGroups {
    static func boardGroups(_ agents: [(id: String, parentId: String?)]) -> [(root: String, members: [String])] {
        let ids = Set(agents.map { TaskOrdinalKey($0.id) })
        var children: [TaskOrdinalKey: [String]] = [:]
        var roots: [String] = []
        for agent in agents {
            if let parent = agent.parentId, ids.contains(TaskOrdinalKey(parent)), !TaskPythonText.same(parent, agent.id) {
                children[TaskOrdinalKey(parent), default: []].append(agent.id)
            } else {
                roots.append(agent.id)
            }
        }
        return roots.map { root in
            var members: [String] = []
            var stack = [root]
            while let id = stack.popLast() {
                members.append(id)
                stack.append(contentsOf: (children[TaskOrdinalKey(id)] ?? []).reversed())
            }
            return (root, members)
        }
    }
}
