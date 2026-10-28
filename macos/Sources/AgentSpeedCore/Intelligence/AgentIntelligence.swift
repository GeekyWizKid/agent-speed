import Foundation

/// One agent's intelligence (METRICS 6.9, Agent intelligence), in exactly one form: live from its own window sums,
/// else the grade of its current model and effort, else none. The forms are never mixed: a live value stays the
/// formula's even when it is lower than the grade.
public enum AgentIntelligence: Equatable, Sendable {
    /// The agent generated in the window: the index over its own sums.
    case live(Double)
    /// Nothing in the window: what the agent would bring.
    case resting(IntelligenceGrade)
    /// Nothing in the window and no model known.
    case none

    public init(_ sums: IntelligenceSums, grade: IntelligenceGrade?) {
        if let index = sums.index {
            self = .live(index)
        } else if let grade {
            self = .resting(grade)
        } else {
            self = .none
        }
    }

    /// The live index or the grade.
    public var value: Double? {
        switch self {
        case .live(let index): index
        case .resting(let grade): Double(grade.value)
        case .none: nil
        }
    }

    /// As shown: an integer rounded half away from zero (a grade is one already).
    public var shown: Int? { value.map { Int($0.rounded()) } }

    public var isLive: Bool {
        if case .live = self { return true }
        return false
    }
}
