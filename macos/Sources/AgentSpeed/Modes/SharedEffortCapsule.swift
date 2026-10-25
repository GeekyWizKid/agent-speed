import AgentSpeedCore

/// What a group of agents shows as one capsule (DESIGN 2b, Context menu item 1): the agents that are generating, or
/// all of them when none is; their shared effort and speed when they agree, `mixed` when they differ.
enum SharedEffortCapsule: Equatable {
    case capsule(EffortCapsule)
    case mixed

    /// Nil when nothing is known (or nothing would be drawn).
    init?(agents: [AgentStatus]) {
        let generating = agents.filter { $0.phase == .generating }
        let known = Set((generating.isEmpty ? agents : generating).map { EffortCapsule($0.modes) }
            .filter { $0.effort != nil || $0.speed != nil })
        guard let first = known.first else { return nil }
        if known.count > 1 {
            self = .mixed
        } else if first.isVisible {
            self = .capsule(first)
        } else {
            return nil
        }
    }
}
