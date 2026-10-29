import Foundation

/// The two values agents and responses show: reasoning effort and the derived speed, each nil when unknown.
public struct EffortAndSpeed: Equatable, Hashable, Sendable {
    public var effort: ExecutionModeAttribution?
    public var speed: ExecutionModeAttribution?

    public static let unknown = EffortAndSpeed(effort: nil, speed: nil)

    public init(effort: ExecutionModeAttribution?, speed: ExecutionModeAttribution?) {
        self.effort = effort
        self.speed = speed
    }

    public init(_ modes: ExecutionModes) {
        let effort = modes.attribution(for: .effort), speed = modes.attribution(for: .speed)
        self.init(effort: effort.isUnknown ? nil : effort, speed: speed.isUnknown ? nil : speed)
    }

    /// Bolts of the speed; 0 when unknown or without bolts.
    public var bolts: Int { speed.map { ModeDisplay.bolts($0.mode) } ?? 0 }

    /// Whether there is anything to draw: a known effort or a speed with bolts.
    public var isVisible: Bool { effort != nil || bolts > 0 }
}

extension ExecutionModes {
    public var effortAndSpeed: EffortAndSpeed { EffortAndSpeed(self) }
}
