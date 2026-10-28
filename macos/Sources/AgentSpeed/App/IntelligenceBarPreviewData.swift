import AgentSpeedCore
import Foundation

/// The menu bar block's sample moments (DESIGN 12, Values and forms; DESIGN 10, Menu bar and taskbar): the agents of
/// all enabled sources with their task states, modelled on the prototype's menu bar section, and their thinking sums.
@MainActor
enum IntelligenceBarPreviewData {
    struct Scenario {
        var name: String
        var agents: [AgentStatus]
        /// Output tok/s in the window (`Σ w`): 0 is nothing generated.
        var output: Double
        var thinking: Double
        /// How long the values have shown; a zero thinking rate goes after its 3 s hold.
        var held: TimeInterval = 0

        /// The set's sums: the thinking rate and the highest score over `agents` (METRICS 6.9, Sets and groups).
        var sums: IntelligenceSums {
            var sums = IntelligenceSums()
            sums.output = output
            sums.thinking = thinking
            sums.thinkingBase = output
            sums.score = agents.reduce(TaskSetValue.empty) { $0.merged(with: $1.intelligence.score) }
            return sums
        }
    }

    static let hold: TimeInterval = 3

    /// Every form and proof state of 智力, with 思考 shown, read as 0 within its hold, and hidden after it; then the
    /// widest numbers.
    static let scenarios: [Scenario] = [
        Scenario(name: "none · quiet", agents: [agent("a", "atlas", .codex, .live, nil, nil)], output: 0, thinking: 0),
        Scenario(name: "none · reading", agents: [agent("a", "atlas", .codex, .live, nil, nil)], output: 180, thinking: 128),
        Scenario(name: "live 86 · passing · top of 4", agents: crew, output: 532, thinking: 128),
        Scenario(name: "live 9 · unverified · top of 2", agents: [
            agent("e", "ember", .claudeCode, .live, 8.6, .unverified), agent("f", "fjord", .codex, .live, 4, .failing),
        ], output: 180, thinking: 64),
        Scenario(name: "live 54 · pending", agents: [agent("g", "gale", .codex, .live, 54, .pending)], output: 96,
                 thinking: 4.2),
        Scenario(name: "live 100 · pending", agents: [agent("h", "halo", .claudeCode, .live, 99.6, .pending)],
                 output: 2_600, thinking: 1_280),
        Scenario(name: "live 72 · thinking 0", agents: [agent("n", "nova", .codex, .live, 72, .passing)], output: 40,
                 thinking: 0),
        Scenario(name: "live 72 · thinking 0 · 3 s", agents: [agent("n", "nova", .codex, .live, 72, .passing)], output: 40,
                 thinking: 0, held: hold),
        Scenario(name: "live 9 · unverified · 3 s", agents: [agent("e", "ember", .claudeCode, .live, 8.6, .unverified)],
                 output: 40, thinking: 0, held: hold),
        Scenario(name: "resting 72 · top of 2", agents: resting, output: 0, thinking: 0),
        Scenario(name: "resting 72 · top of 2 · 3 s", agents: resting, output: 0, thinking: 0, held: hold),
        Scenario(name: "resting 61 · pending · 3 s", agents: [agent("p", "pier", .codex, .resting, 61, .pending)],
                 output: 0, thinking: 0, held: hold),
        Scenario(name: "live 100 · 12.8k", agents: [agent("h", "halo", .claudeCode, .live, 100, .passing)],
                 output: 26_000, thinking: 12_800),
        Scenario(name: "live 100 · 1.20M", agents: [agent("h", "halo", .claudeCode, .live, 100, .passing)],
                 output: 2_400_000, thinking: 1_200_000),
    ]

    /// The prototype's four live agents: nova supplies 86.
    private static var crew: [AgentStatus] {
        [
            agent("n", "nova", .codex, .live, 85.7, .passing), agent("o", "orbit", .claudeCode, .live, 41.2, .failing),
            agent("q", "quill", .claudeCode, .live, 61.4, .passing), agent("e", "ember", .codex, .live, 8.6, .unverified),
        ]
    }

    private static var resting: [AgentStatus] {
        [agent("n", "nova", .codex, .resting, 72.3, .passing), agent("o", "orbit", .claudeCode, .resting, 25.1, .failing)]
    }

    /// One agent whose current task has `score` in `state` (nil: a task that has only read so far).
    static func agent(_ id: String, _ project: String, _ source: SourceKind, _ form: TaskValueForm, _ score: Double?,
                      _ state: TaskState?) -> AgentStatus {
        let path = "/preview/bar/\(id).jsonl"
        var result = TaskResult(TaskEvaluation([TaskEvent](), now: nil))
        result.state = state ?? .none
        result.score = score
        result.shown = score.map(TaskScoreText.rounded)
        let value = TaskAgentValue(form: score == nil ? .none : form, score: score, shown: result.shown, state: result.state)
        var intelligence = IntelligenceSums()
        intelligence.score = TaskSetValue(id: path, lastLineMs: nil, value: value)
        let live = form == .live ? 120.0 : 0
        return AgentStatus(
            id: path, source: source, project: project, model: source == .codex ? "gpt-5.5" : "claude-opus-5-5",
            phase: form == .live ? .generating : .idle, isSubagent: false, live: Rate(output: live, total: live * 26),
            recentSpeed: live > 0 ? live : nil, lastActivity: Date(), intelligence: intelligence,
            task: AgentTaskStatus(taskStartMs: nil, result: result, predictions: nil, models: .prior)
        )
    }
}
