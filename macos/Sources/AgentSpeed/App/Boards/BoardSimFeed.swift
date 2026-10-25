import AgentSpeedOrbModel
import AppKit

/// Feeds a board from `BoardSimulation` one engine tick at a time, as the prototypes' pages do: lays out the
/// placeholder content (only when asked, so a board nobody sees costs no layout) and plays the tick's heat, overtakes,
/// new leader, number punch and new peak on the effects.
@MainActor
final class BoardSimFeed {
    var simulation: BoardSimulation
    var content: BoardPlaceholderContent
    let redline: Double
    private(set) var snapshot: BoardSimulation.Snapshot
    private var cached: BoardPlaceholderContent.Layout?
    private var lastLive: Double?
    private var lastPeak: Double?

    init(simulation: BoardSimulation, content: BoardPlaceholderContent, redline: Double = 2000) {
        self.simulation = simulation
        self.content = content
        self.redline = redline
        snapshot = self.simulation.snapshot()
    }

    /// Advances one engine tick.
    func step() {
        simulation.advance()
        snapshot = simulation.snapshot()
        cached = nil
    }

    /// The content of the current tick, laid out once.
    var layout: BoardPlaceholderContent.Layout {
        if let cached { return cached }
        let layout = content.layout(snapshot, redline: redline)
        cached = layout
        return layout
    }

    /// Plays the current tick on the effects: heat, then (with `events`) the events at the rows' new places.
    func play(on fx: inout BoardEffects, events: Bool = true) {
        let live = snapshot.live
        fx.setHeat(HeatPalette.heat(value: live, redline: redline),
                   overdrive: HeatPalette.overdrive(value: live, redline: redline), active: snapshot.generating > 0)
        defer {
            lastLive = live
            lastPeak = snapshot.peak
        }
        guard events else { return }
        let layout = self.layout
        for event in snapshot.events {
            guard let anchor = layout.groupRows[event.id],
                  let group = snapshot.groups.first(where: { $0.id == event.id }) else { continue }
            let color = RGBColor(hex: BoardSimulation.sources[group.root.source] ?? 0xFFFFFF).simd
            let passed = event.to < snapshot.groups.count
                ? layout.groupRows[snapshot.groups[event.to].id].map { Double($0.rank.y) } : nil
            if event.kind == .newLeader {
                fx.newLeader(row: anchor.row, rank: anchor.rank, color: color, passedRankY: passed)
            } else {
                fx.overtake(row: anchor.row, rank: anchor.rank, color: color, passedRankY: passed)
            }
        }
        if let lastLive, lastLive > 0, live > lastLive * 1.08 + 20 {
            fx.punch(number: layout.number, rise: (live - lastLive) / max(1, lastLive))
        }
        if let lastPeak, snapshot.peak > lastPeak + 1 {
            fx.newPeak(at: CGPoint(x: layout.number.midX, y: layout.number.midY))
        }
    }
}
