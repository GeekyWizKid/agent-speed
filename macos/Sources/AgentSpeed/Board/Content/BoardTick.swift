import AgentSpeedCore
import AgentSpeedOrbModel
import CoreGraphics

/// What one engine tick tells a board (DESIGN 2c Content). The live feed (the app's boards controller, from the
/// snapshot) and the demo feed make it; a style's `BoardContentBuilding` draws it. Fields are only ever added.
struct BoardTick {
    /// `live_A` in the board's metric (METRICS 6.8) and its text (METRICS 7).
    var value: Double
    var valueText: String
    /// The metric of `value` and of every row's value (a sub-agent's is `live.value(for: metric)`).
    var metric: SpeedMetric
    var redline: Double
    var heat: Double
    var overdrive: Double
    /// The speed title in the board's title theme (DESIGN 1), localized.
    var title: String
    var generating: Int
    var tools: Int
    /// `live_A` once per second, oldest first, at most 60.
    var history: [Double]
    /// Ranked groups, first place first (DESIGN 2c Ranking stability), and the planned rows showing them.
    var groups: [BoardGroup]
    var rows: [BoardPlannedRow]
    /// The passes of this tick.
    var events: [BoardRankEvent]
    /// A new `peakToday_A` was reached on this tick.
    var newPeak: Bool
    /// Footer values, already formatted: today's output, today's cost (nil when unknown, section 9), `peakToday_A`.
    var todayText: String
    var costText: String?
    var peakText: String
    /// `peakToday_A` (neon marks it on its LED bar); 0 when unknown.
    var peak: Double = 0
}

/// A board's content for one tick, with the anchors the effects need (board points, y down).
struct BoardContentLayout {
    var items: [BoardContentItem]
    /// Group id → its row frame and the center of its rank, for overtake and new-leader effects.
    var groupRows: [String: (row: CGRect, rank: CGPoint)]
    /// The hero number, for punches, embers and new peaks.
    var number: CGRect
}

/// Draws a style's hero, rows and footer (DESIGN 2c Styles; prototypes in spec/prototypes/board).
@MainActor
protocol BoardContentBuilding {
    func layout(_ tick: BoardTick) -> BoardContentLayout
}
