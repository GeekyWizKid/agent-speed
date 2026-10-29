import AgentSpeedCore

/// A set's 智力 as the surfaces draw it (METRICS 6.9 *Sets and groups*, DESIGN 12): the highest score with what a
/// `TaskSetValue` leaves out — the supplying agent's state (the proof mark follows it) and N, the set's agents with a
/// score in that form (accessibility and the card's eyebrow).
public struct TaskSetReading: Equatable, Sendable {
    public var form: TaskValueForm
    public var shown: Int?
    public var state: TaskState?
    public var count: Int
    public var agentId: String?

    public static let none = TaskSetReading(form: .none, shown: nil, state: nil, count: 0, agentId: nil)

    public init(form: TaskValueForm, shown: Int?, state: TaskState?, count: Int, agentId: String?) {
        self.form = form
        self.shown = form == .none ? nil : shown
        self.state = state
        self.count = count
        self.agentId = agentId
    }

    /// `score` is the set's value over `agents` (the menu bar's agents list, an orb's or a board's set `A`, a group).
    public init(score: TaskSetValue, agents: [AgentStatus]) {
        guard score.form != .none, let shown = score.shown else {
            self = .none
            return
        }
        let supplier = score.agentId.flatMap { id in agents.first { $0.id == id } }
        let count = agents.reduce(0) { $0 + ($1.intelligence.score.form == score.form ? 1 : 0) }
        self.init(form: score.form, shown: shown, state: supplier?.task?.result?.state, count: max(1, count),
                  agentId: score.agentId)
    }

    /// One agent's own value (a dashboard row, a board sub row).
    public init(agent: AgentStatus) {
        self.init(score: agent.intelligence.score, agents: [agent])
    }

    /// `unverified` or `pending`: drawn with the proof mark.
    public var isUnproven: Bool { form != .none && (state == .unverified || state == .pending) }

    public var isResting: Bool { form == .resting }
}
