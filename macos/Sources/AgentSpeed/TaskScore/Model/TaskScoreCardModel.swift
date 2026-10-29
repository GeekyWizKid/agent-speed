import AgentSpeedCore

/// Everything the score card shows (DESIGN 12, *Score card*), mapped from an agent's current task and its row.
struct TaskScoreCardModel: Equatable, Sendable {
    struct Header: Equatable, Sendable {
        var source: SourceKind
        var project: String
        var model: String?
        /// A board group row's sub-agents (`boards.subCount`); 0 elsewhere.
        var subAgents = 0
    }

    enum Body: Equatable, Sendable {
        /// The source records no tool calls (METRICS 6.9), so its tasks have no score.
        case unsupported
        case scored(TaskScoreCardScore)
    }

    var header: Header
    var body: Body
    /// The row's former tooltip lines, in their order, already formatted.
    var facts: [String] = []

    var score: TaskScoreCardScore? {
        if case .scored(let score) = body { return score }
        return nil
    }
}

/// The scored part of a card: the task's state, value, dimensions, facts, predictions and behaviour.
struct TaskScoreCardScore: Equatable, Sendable {
    var form: TaskScoreForm
    var state: TaskScoreState
    /// Unrounded; nil while none.
    var score: Double?
    /// N on a board hero's card (`task.card.top` from 2); 1 elsewhere.
    var topCount = 1
    var dimensions: TaskScoreDimensions = .zero
    /// `vHeld`: while `pending`, the V credit a passing re-run restores (40, or 20 after a build-only pass).
    var heldCredit: Double?
    var openFailure: TaskOpenFailureFacts?
    var lastPass: TaskLastPassFacts?
    /// Nil while the agent is not live with a score.
    var predictions: TaskScorePredictions?
    var signals: WorkSignals = .none
    /// The task's oscillating effective edits.
    var oscillations = 0

    /// The number as shown (METRICS 6.9 *Score*: rounded half away from zero).
    var shown: Int? { score.map(TaskScoreText.rounded) }

    /// Predictions describe work in progress: shown only while live with a score.
    var showsPredictions: Bool { form == .live && score != nil && predictions != nil }
}
