import AgentSpeedCore
import AgentSpeedOrbModel

// The engine's task values (METRICS 6.9, 6.11) as the surfaces' presentation models.

extension TaskScoreState {
    init(_ state: TaskState) {
        switch state {
        case .none: self = .none
        case .unverified: self = .unverified
        case .passing: self = .passing
        case .failing: self = .failing
        case .pending: self = .pending
        }
    }
}

extension TaskScoreForm {
    init(_ form: TaskValueForm) {
        switch form {
        case .live: self = .live
        case .resting: self = .resting
        case .none: self = .none
        }
    }
}

extension TaskScoreValue {
    init(_ reading: TaskSetReading) {
        self.init(form: TaskScoreForm(reading.form), shown: reading.shown, state: reading.state.map(TaskScoreState.init),
                  count: reading.count, agentId: reading.agentId)
    }
}

extension TaskCheckClass {
    /// A check's class; `other` never names a check, so it reads as a check.
    init(_ commandClass: TaskCommandClass) {
        switch commandClass {
        case .test: self = .test
        case .build: self = .build
        case .check, .other: self = .check
        }
    }
}

extension TaskScoreCardModel {
    /// The card of `agent`: `facts` are its row's former tooltip lines; a group row passes its sub-agents, a board
    /// hero the set's N.
    init(agent: AgentStatus, facts: [String], subAgents: Int = 0, topCount: Int = 1) {
        let header = Header(source: agent.source, project: agent.project, model: agent.model, subAgents: subAgents)
        guard let task = agent.task else {
            self.init(header: header, body: .unsupported, facts: facts)
            return
        }
        self.init(header: header, body: .scored(TaskScoreCardScore(agent: agent, task: task, topCount: topCount)),
                  facts: facts)
    }
}

extension TaskScoreCardScore {
    init(agent: AgentStatus, task: AgentTaskStatus, topCount: Int = 1) {
        let result = task.result
        let form = agent.intelligence.score.form
        self.init(form: result?.score == nil ? .none : TaskScoreForm(form),
                  state: result.map { TaskScoreState($0.state) } ?? .none,
                  score: result?.score)
        self.topCount = topCount
        if let result {
            dimensions = TaskScoreDimensions(verify: result.dims.v, converge: result.dims.c, recover: result.dims.r,
                                             efficiency: result.dims.e, complete: result.dims.k)
            heldCredit = result.vHeld
            openFailure = result.openFailure.map {
                TaskOpenFailureFacts(check: TaskCheckName(suite: $0.suite, kind: TaskCheckClass($0.commandClass)),
                                     errors: $0.errors, peak: $0.peak, counted: $0.counted, edited: $0.edited)
            }
            lastPass = result.lastPass.map {
                TaskLastPassFacts(check: TaskCheckName(suite: $0.suite, kind: TaskCheckClass($0.commandClass)),
                                  buildOnly: $0.buildOnly)
            }
            oscillations = result.counts.oscillations
        }
        if let predictions = task.predictions {
            let flags = task.models
            func flag(_ model: TaskModelFlag) -> TaskCalibration {
                TaskCalibration(calibrated: model.calibrated, tasks: model.tasks)
            }
            self.predictions = TaskScorePredictions(
                success: TaskScorePrediction(probability: predictions.success, calibration: flag(flags.success)),
                stall: TaskScorePrediction(probability: predictions.stall, calibration: flag(flags.stall)),
                fixFails: predictions.fix.map { TaskScorePrediction(probability: $0, calibration: flag(flags.fix)) }
            )
        }
        signals = agent.signals
    }
}

extension TaskPerformanceComparison {
    init(_ comparison: TaskComparison) {
        bySource = comparison.bySource.compactMap { row in
            row.source.map { TaskPerformanceSource(source: $0, stats: TaskPerformanceStats(row)) }
        }
        byModel = comparison.byModel.map { row in
            TaskPerformanceModelRow(model: row.model ?? "", sources: row.sources, stats: TaskPerformanceStats(row))
        }
    }
}

extension TaskPerformanceStats {
    /// A comparison row with the numerators its rate tooltips show (METRICS 6.11 *Comparison*).
    init(_ row: TaskComparisonRow) {
        self.init(tasks: row.tasks, scoredTasks: row.scoredTasks, medianScore: row.medianScore,
                  lowerQuartile: row.lowerQuartile, upperQuartile: row.upperQuartile, histogram: row.histogram,
                  successRate: row.successRate, stallRate: row.stallRate, fixFailureRate: row.fixFailureRate,
                  recoverAttempts: row.recoverAttempts, timeToFirstPassMs: row.timeToFirstPassMs,
                  successes: row.successes, stalled: row.stalls, fixFailures: row.fixFailures,
                  fixAttempts: row.fixAttempts, recovered: row.recovered)
    }
}
