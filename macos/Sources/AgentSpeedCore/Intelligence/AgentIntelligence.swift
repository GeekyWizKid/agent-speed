import Foundation

/// One agent's, set's or group's 智力 (METRICS 6.9 *Values*), in exactly one form: live (the score of a task in progress),
/// resting (the last task's final score while idle, shown pale) or none.
public enum AgentIntelligence: Equatable, Sendable {
    case live(Double)
    case resting(Double)
    case none

    public init(_ value: TaskSetValue) {
        switch (value.form, value.score) {
        case (.live, let score?): self = .live(score)
        case (.resting, let score?): self = .resting(score)
        default: self = .none
        }
    }

    public init(_ value: TaskAgentValue) {
        switch (value.form, value.score) {
        case (.live, let score?): self = .live(score)
        case (.resting, let score?): self = .resting(score)
        default: self = .none
        }
    }

    /// The unrounded score.
    public var value: Double? {
        switch self {
        case .live(let score), .resting(let score): score
        case .none: nil
        }
    }

    /// As shown: an integer rounded half away from zero.
    public var shown: Int? { value.map(taskRoundHalfAway) }

    public var isLive: Bool {
        if case .live = self { return true }
        return false
    }
}
