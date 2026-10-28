import Foundation

/// A board group's live sums (METRICS 6.9 *Group intelligence*): its members' agent sums added, never an average of
/// their values. Boards show no cost per group (METRICS 9.5).
public struct AgentGroupSums: Equatable, Sendable {
    /// Σ of the members' own intelligence sums.
    public var intelligence = IntelligenceSums.zero

    public init() {}

    public static let zero = AgentGroupSums()

    /// Adds one member.
    public mutating func add(_ agent: AgentStatus) {
        intelligence.add(agent.intelligence)
    }

    /// The group's form: live over its sums, else the root's grade, else none (even when a sub-agent has a grade).
    public func intelligence(rootGrade: IntelligenceGrade?) -> AgentIntelligence {
        AgentIntelligence(intelligence, grade: rootGrade)
    }
}
