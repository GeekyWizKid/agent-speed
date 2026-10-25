import SwiftUI

/// The agents' shared effort capsule column (DESIGN 4, Agents): as wide as the widest capsule among the visible rows,
/// taking no space when none shows; every word drops when the column would leave a project name under 72 pt.
struct AgentCapsuleColumn: Equatable {
    var width: CGFloat
    var compact: Bool

    static let none = AgentCapsuleColumn(width: 0, compact: false)
    /// Between the column and the live value.
    static let gap: CGFloat = 8
    static let minimumName: CGFloat = 72

    init(width: CGFloat, compact: Bool) {
        self.width = width
        self.compact = compact
    }

    /// `rows` pairs each visible row's capsule with its indent level; `rowWidth` is the agents list's width.
    init(rows: [(capsule: EffortCapsule, depth: Int)], rowWidth: CGFloat) {
        let capsules = rows.map(\.capsule)
        let full = EffortCapsule.columnWidth(capsules, compact: false)
        let deepest = rows.map(\.depth).max() ?? 0
        if Self.nameWidth(rowWidth: rowWidth, depth: deepest, column: full) >= Self.minimumName {
            self.init(width: full, compact: false)
        } else {
            self.init(width: EffortCapsule.columnWidth(capsules, compact: true), compact: true)
        }
    }

    /// Room left for the name block: badge, two row gaps, the column and its gap, and the live value.
    static func nameWidth(rowWidth: CGFloat, depth: Int, column: CGFloat) -> CGFloat {
        let trailing = DashboardLayout.speedColumn + (column > 0 ? column + gap : 0)
        return rowWidth - CGFloat(depth) * DashboardLayout.subagentIndent - DashboardLayout.badgeWidth - Spacing.s * 2 - trailing
    }
}
