import Foundation

/// The 6.9 values of a group of agents at one instant, evaluated from their whole streams (METRICS 6.9 *Values*,
/// *Dumps*): each agent's status and value, the agents list (6.7), the highest-score value of each source set and of
/// each board group. The probe's dumps and the end-to-end fixture use it; the live engine computes the same values
/// incrementally (`SpeedEngine`).
struct TaskValueReport {
    struct Agent {
        var id: String
        var source: SourceKind
        /// The parser's phase (before the staleness rule).
        var phase: AgentPhase
        var lastLineMs: Int64?
        /// The resolved parent (6.7), which may not be listed.
        var parentId: String?
        var events: [TaskEvent]
    }

    struct Row {
        var agent: Agent
        /// After the 300-second staleness rule.
        var phase: AgentPhase
        var status: AgentTaskStatus
        var value: TaskAgentValue
    }

    struct SetValue {
        /// nil: every source.
        var sources: [SourceKind]?
        var value: TaskSetValue
    }

    struct Group {
        var root: String
        var members: [String]
        var value: TaskSetValue
    }

    static let staleMs: Int64 = 300_000
    static let listedMs: Int64 = 600_000

    var rows: [Row] = []
    /// The agents list, by id (UTF-16 order).
    var listed: [String] = []
    var sets: [SetValue] = []
    /// By root id (UTF-16 order).
    var groups: [Group] = []

    /// `sets`: the source sets to evaluate (nil inside: all sources); nil: all sources, then each scored source with
    /// listed agents.
    init(agents: [Agent], now: Int64, models: [SourceKind: TaskModels], sets requested: [[SourceKind]?]? = nil) {
        for agent in agents {
            var phase = agent.phase
            if phase != .idle, let last = agent.lastLineMs, now - last > Self.staleMs { phase = .idle }
            let status = AgentTaskStatus(events: agent.events, now: now, models: models[agent.source] ?? .prior)
            rows.append(Row(agent: agent, phase: phase, status: status, value: status.value(idle: phase == .idle)))
        }
        let tree = Self.listed(rows, now: now)
        let shown = rows.compactMap { row -> (row: Row, parentId: String?)? in
            guard let parent = tree[TaskOrdinalKey(row.agent.id)] else { return nil }
            return (row, parent)
        }.sorted { AgentParents.ordinal($0.row.agent.id, precedes: $1.row.agent.id) }
        listed = shown.map(\.row.agent.id)
        func value(of members: [(row: Row, parentId: String?)]) -> TaskSetValue {
            TaskSetValue.highest(members.map { ($0.row.agent.id, $0.row.agent.lastLineMs, $0.row.value) })
        }
        let candidates: [[SourceKind]?] = requested ?? ([nil] + SourceKind.allCases.filter(TaskScoring.scores).map { [$0] })
        for sources in candidates {
            let members = shown.filter { sources?.contains($0.row.agent.source) ?? true }
            if requested == nil, sources != nil, members.isEmpty { continue }
            sets.append(SetValue(sources: sources, value: value(of: members)))
        }
        let byId = Dictionary(shown.map { (TaskOrdinalKey($0.row.agent.id), $0) }, uniquingKeysWith: { first, _ in first })
        groups = TaskGroups.boardGroups(shown.map { ($0.row.agent.id, $0.parentId) })
            .map { group in
                Group(root: group.root, members: group.members,
                      value: value(of: group.members.compactMap { byId[TaskOrdinalKey($0)] }))
            }
            .sorted { AgentParents.ordinal($0.root, precedes: $1.root) }
    }

    /// 6.7 steps 1–4: the listed agents and each one's tree parent (`.some(nil)` for a root).
    private static func listed(_ rows: [Row], now: Int64) -> [TaskOrdinalKey: String?] {
        var byId: [TaskOrdinalKey: Row] = [:]
        for row in rows where byId[TaskOrdinalKey(row.agent.id)] == nil { byId[TaskOrdinalKey(row.agent.id)] = row }
        var chosen = Set<TaskOrdinalKey>()
        for row in rows where row.phase == .generating || row.agent.lastLineMs.map({ now - $0 <= listedMs }) == true {
            chosen.insert(TaskOrdinalKey(row.agent.id))
        }
        for id in Array(chosen) {
            var seen: Set<TaskOrdinalKey> = [id]
            var current = byId[id]?.agent.parentId.map(TaskOrdinalKey.init)
            while let next = current, byId[next] != nil, !seen.contains(next) {
                chosen.insert(next)
                seen.insert(next)
                current = byId[next]?.agent.parentId.map(TaskOrdinalKey.init)
            }
        }
        var tree: [TaskOrdinalKey: String?] = [:]
        for id in chosen {
            let parent = byId[id]?.agent.parentId
            // `updateValue`: assigning a nil parent through the subscript would remove the root.
            tree.updateValue(parent.flatMap { chosen.contains(TaskOrdinalKey($0)) ? $0 : nil }, forKey: id)
        }
        return tree
    }
}
