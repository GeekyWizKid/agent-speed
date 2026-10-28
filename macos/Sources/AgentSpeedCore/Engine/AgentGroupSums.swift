import Foundation

/// A board group's live sums (METRICS 6.9 *Group intelligence*, 9.5 *Per group*): its members' agent sums added,
/// never an average of their values. Subscriptions (9.4) apply per member: an excluded member adds nothing to the
/// cost or its tokens, and a group without a billable member has no displayed cost.
public struct AgentGroupSums: Equatable, Sendable {
    /// Σ of the members' own intelligence sums.
    public var intelligence = IntelligenceSums.zero
    /// Σ of the billable members' live cost.
    public var cost = LiveCost.zero
    /// Live output (tok/s) of the billable members: the tokens `cost.knownOutput` is a share of.
    public var costOutput = 0.0
    public var billableMembers = 0

    public init() {}

    public static let zero = AgentGroupSums()

    /// Adds one member.
    public mutating func add(_ agent: AgentStatus, billable: Bool) {
        intelligence.add(agent.intelligence)
        guard billable else { return }
        billableMembers += 1
        cost.add(agent.liveCost)
        costOutput += agent.live.output
    }

    /// The group's form: live over its sums, else the root's grade, else none (even when a sub-agent has a grade).
    public func intelligence(rootGrade: IntelligenceGrade?) -> AgentIntelligence {
        AgentIntelligence(intelligence, grade: rootGrade)
    }

    /// `liveCostKnownShare_G`: Σ known tokens ÷ Σ tokens over the billable members (1 when they add up to 0).
    public var costKnownShare: Double { cost.knownShare(liveOutput: costOutput) }

    /// The group's cost as a surface shows it (the same states as a source set's, 9.5).
    public var costState: CostReading.State {
        if billableMembers == 0 { return .subscriptionOnly }
        if costOutput > 0, cost.knownOutput <= 0 { return .nothingPriced }
        if costKnownShare < CostReading.partialThreshold, cost.microUsdPerSecond > 0 { return .partial }
        return .priced
    }
}
