import AgentSpeedCore
import Foundation

/// A root agent and all its descendants (DESIGN 2c, Ranking).
public struct BoardGroup: Equatable, Sendable, Identifiable {
    /// The root agent's id.
    public var id: String { root.id }
    public var root: AgentStatus
    /// Every descendant, by live value, latest activity, id.
    public var subs: [AgentStatus]
    /// The sum of the members' live values in the board's metric.
    public var live: Double
    /// The descendants' part of `live`.
    public var subLive: Double
    /// The latest activity of any member.
    public var lastActive: Date
    /// Members in phase `generating`.
    public var generating: Int
}

/// Groups a board's agents by their root (DESIGN 2c, Content 2; METRICS 6.7 parent links).
public enum BoardGrouping {
    /// Below this live value (tok/s) a group or agent counts as idle (DESIGN 2c, Ranking stability).
    public static let idle = 0.05

    /// `agents` is the board's agents list (METRICS 6.7 filtered by 6.8, so a parent outside the set is already
    /// cleared). A sub-agent whose parent is not listed is its own root; cycles resolve to the agent itself. Groups
    /// come in the order of their roots in `agents`.
    public static func groups(_ agents: [AgentStatus], metric: SpeedMetric) -> [BoardGroup] {
        var index: [String: Int] = [:]
        for (position, agent) in agents.enumerated() where index[agent.id] == nil { index[agent.id] = position }
        func root(_ start: Int) -> Int {
            var current = start
            var visited: Set<Int> = [start]
            while let parent = agents[current].parentId, let next = index[parent], next != current {
                guard visited.insert(next).inserted else { return start }
                current = next
            }
            return current
        }
        var order: [Int] = []
        var members: [Int: [Int]] = [:]
        for position in agents.indices where index[agents[position].id] == position {
            let top = root(position)
            if top == position {
                order.append(position)
            } else {
                members[top, default: []].append(position)
            }
        }
        return order.map { top in
            let rootAgent = agents[top]
            let subs = (members[top] ?? []).map { agents[$0] }.sorted { lhs, rhs in
                let a = lhs.live.value(for: metric), b = rhs.live.value(for: metric)
                if a != b { return a > b }
                if lhs.lastActivity != rhs.lastActivity { return lhs.lastActivity > rhs.lastActivity }
                return lhs.id < rhs.id
            }
            let subLive = subs.reduce(0) { $0 + max(0, $1.live.value(for: metric)) }
            let all = [rootAgent] + subs
            return BoardGroup(
                root: rootAgent,
                subs: subs,
                live: max(0, rootAgent.live.value(for: metric)) + subLive,
                subLive: subLive,
                lastActive: all.map(\.lastActivity).max() ?? rootAgent.lastActivity,
                generating: all.filter { $0.phase == .generating }.count
            )
        }
    }

    /// What the stable ranking needs of each group.
    public static func rankEntries(_ groups: [BoardGroup]) -> [BoardRankEntry] {
        groups.map { BoardRankEntry(id: $0.id, live: $0.live, lastActiveMs: $0.lastActive.timeIntervalSince1970 * 1000) }
    }
}
