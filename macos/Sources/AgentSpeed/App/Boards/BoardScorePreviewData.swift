import AgentSpeedCore
import AgentSpeedOrbModel
import Foundation

/// Task scores for the boards' simulation and previews (METRICS 6.9): an agent's own value with a current task whose
/// state, dimensions and facts fit it, so its rows, hero and score card read as the engine's would.
@MainActor
enum BoardScorePreviewData {
    /// The board's 智力 in each form and proof state (DESIGN 12), for the previews.
    enum Scenario: String, CaseIterable {
        /// Live scores on most rows: passing, failing, a resting one, an unverified sub-agent, a pending one, none.
        case live
        /// Every agent idle after work: resting scores, pale.
        case resting
        /// No agent has a score: `–`, an empty arc and meter.
        case none
        /// The highest live score is unverified (the proof mark on the hero).
        case unverified
        /// The highest live score waits for a re-run (pending).
        case pending
    }

    /// `agent` with its own task score: live while it works, resting while idle; nil leaves it without a score. A
    /// source that records no tool calls has no task score at all (its card says so).
    static func scored(_ agent: AgentStatus, score: Double?, state: TaskState, idle: Bool? = nil) -> AgentStatus {
        var agent = agent
        let idle = idle ?? (agent.phase == .idle)
        guard TaskScoring.scores(agent.source), let score else {
            agent.task = TaskScoring.scores(agent.source) ? .empty : nil
            agent.intelligence.score = .empty
            return agent
        }
        let task = task(score: score, state: state, idle: idle)
        agent.task = task
        agent.intelligence.score = TaskSetValue(id: agent.id, lastLineMs: Int64(agent.lastActivity.timeIntervalSince1970 * 1000),
                                                value: task.value(idle: idle))
        return agent
    }

    /// A current task scoring `score` in `state`: its dimensions add up to the score as METRICS 6.9 builds them (V only
    /// once a check passed, its credit held while pending), with the facts and predictions of a task in progress.
    static func task(score: Double, state: TaskState, idle: Bool) -> AgentTaskStatus {
        var result = TaskResult(TaskEvaluation([TaskEvent](), now: nil))
        result.state = state
        result.score = score
        result.shown = taskRoundHalfAway(score)
        let buildOnly = state == .passing && score < 45
        let verify: Double = switch state {
        case .passing: buildOnly ? 20 : 40
        case .none, .unverified, .failing, .pending: 0
        }
        // The rest spreads over C R E K by their maxima (E alone while nothing was verified).
        let rest = max(0, score - verify)
        if state == .unverified || state == .none {
            result.dims = TaskDimensions(v: 0, c: 0, r: 0, e: min(10, rest), k: 0)
        } else {
            let share = min(1, rest / 60)
            result.dims = TaskDimensions(v: verify, c: 25 * share, r: 15 * share, e: 10 * share, k: 10 * share)
        }
        let key = TaskDigest(value: 0)
        switch state {
        case .passing:
            result.lastPass = TaskLastPass(suite: buildOnly ? nil : "swift test", commandClass: buildOnly ? .build : .test,
                                           key: key, buildOnly: buildOnly)
        case .pending:
            result.vHeld = 40
            result.lastPass = TaskLastPass(suite: "pytest", commandClass: .test, key: key, buildOnly: false)
        case .failing:
            result.openFailure = TaskOpenFailure(suite: "cargo test", commandClass: .test, key: key, errors: 3, peak: 9,
                                                 counted: true, edited: false)
        case .none, .unverified:
            break
        }
        let success = min(0.97, max(0.05, score / 100 + (state == .failing ? -0.1 : 0.05)))
        let predictions = idle ? nil : TaskPredictions(success: success, stall: max(0.01, (100 - score) / 260),
                                                        fix: state == .failing ? 0.42 : nil,
                                                        features: TaskSnapshotFeatures(passing: 0, failing: 0, conv: 0, ig: 0,
                                                                                       loop: 0, osc: 0, size: 0),
                                                        fixFeatures: nil)
        return AgentTaskStatus(taskStartMs: 0, result: result, predictions: predictions, models: .prior)
    }

    /// `tick` with the scores of `scenario` on its agents, its groups and its hero's 智力 made again from them.
    static func tick(_ tick: BoardTick, scenario: Scenario) -> BoardTick {
        var agents = tick.groups.flatMap(\.members)
        let roots = tick.groups.map(\.root.id)
        func assign(_ id: String?, _ score: Double?, _ state: TaskState, idle: Bool? = nil) {
            guard let id, let index = agents.firstIndex(where: { $0.id == id }) else { return }
            agents[index] = scored(agents[index], score: score, state: state, idle: idle)
        }
        func root(_ index: Int) -> String? { index < roots.count ? roots[index] : nil }
        func sub(_ index: Int, _ sub: Int = 0) -> String? {
            index < tick.groups.count && sub < tick.groups[index].subs.count ? tick.groups[index].subs[sub].id : nil
        }
        for agent in agents { assign(agent.id, nil, .none) }
        var idle = false
        switch scenario {
        case .live:
            assign(root(0), 41.2, .failing)
            assign(sub(0), 9.4, .unverified)
            assign(sub(0, 1), 63, .passing)
            assign(root(1), 85.7, .passing)
            assign(sub(1), 30, .passing)
            assign(root(2), 34.3, .pending)
            assign(root(4), 72.4, .passing, idle: true)
        case .resting:
            idle = true
            assign(root(0), 41.2, .failing)
            assign(sub(0), 9.4, .unverified)
            assign(root(1), 72.4, .passing)
            assign(root(2), 34.3, .pending)
            assign(root(4), 58, .passing)
        case .none:
            break
        case .unverified:
            assign(root(0), 8.6, .unverified)
            assign(sub(0), 6, .unverified)
            assign(root(2), 72.4, .passing, idle: true)
        case .pending:
            assign(root(0), 41.2, .failing)
            assign(root(1), 57.6, .pending)
            assign(sub(1), 30, .passing)
            assign(root(2), 34.3, .passing)
        }
        if idle {
            agents = agents.map { agent in
                var agent = agent
                agent.phase = .idle
                agent.live = .zero
                agent.intelligence.output = 0
                agent.intelligence.thinking = 0
                agent.intelligence.thinkingBase = 0
                if let task = agent.task, task.result?.score != nil {
                    agent.intelligence.score = TaskSetValue(id: agent.id, lastLineMs: 0, value: task.value(idle: true))
                    agent.task?.predictions = nil
                }
                return agent
            }
        }
        var next = tick
        let grouped = Dictionary(BoardGrouping.groups(agents, metric: tick.metric).map { ($0.id, $0) }, uniquingKeysWith: { a, _ in a })
        next.groups = tick.groups.compactMap { grouped[$0.id] }
        if idle {
            next.value = 0
            next.valueText = SpeedFormat.speed(0)
            next.heat = 0
            next.overdrive = 0
            next.generating = 0
            next.tools = 0
        }
        guard tick.metrics.readouts.hero else { return next }
        var sums = AgentGroupSums()
        for agent in agents { sums.add(agent) }
        next.metrics = .make(readouts: tick.metrics.readouts, intelligence: sums.intelligence, agents: agents, cost: tick.metrics.cost,
                             costUnit: tick.metrics.costUnit, thinkingHidden: tick.metrics.thinkingHidden)
        return next
    }
}
