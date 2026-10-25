import AgentSpeedOrbModel
import AppKit

/// Feeds a board from `BoardSimulation` one engine tick at a time, as the prototypes' pages do: lays out the
/// placeholder content and plays the tick's heat, overtakes, new leader, number punch and new peak on the effects.
@MainActor
final class BoardSimFeed {
    struct Tick {
        var items: [BoardContentItem]
        var snapshot: BoardSimulation.Snapshot
        var layout: BoardPlaceholderContent.Layout
    }

    var simulation: BoardSimulation
    var content: BoardPlaceholderContent
    let redline: Double
    private var lastLive: Double?
    private var lastPeak: Double?

    init(simulation: BoardSimulation, content: BoardPlaceholderContent, redline: Double = 2000) {
        self.simulation = simulation
        self.content = content
        self.redline = redline
    }

    /// Advances one tick and returns what to show.
    func step() -> Tick {
        simulation.advance()
        return current()
    }

    /// The current state without advancing (the first frame).
    func current() -> Tick {
        let snapshot = simulation.snapshot()
        let layout = content.layout(snapshot, redline: redline)
        return Tick(items: layout.items, snapshot: snapshot, layout: layout)
    }

    /// Plays `tick` on the effects: heat, then the events at the rows' new places.
    func play(_ tick: Tick, on fx: inout BoardEffects) {
        let snapshot = tick.snapshot
        let live = snapshot.live
        fx.setHeat(HeatPalette.heat(value: live, redline: redline),
                   overdrive: HeatPalette.overdrive(value: live, redline: redline), active: snapshot.generating > 0)
        for event in snapshot.events {
            guard let anchor = tick.layout.groupRows[event.id],
                  let group = snapshot.groups.first(where: { $0.id == event.id }) else { continue }
            let color = RGBColor(hex: BoardSimulation.sources[group.root.source] ?? 0xFFFFFF).simd
            let passed = event.to < snapshot.groups.count ? tick.layout.groupRows[snapshot.groups[event.to].id].map { Double($0.rank.y) } : nil
            if event.kind == .newLeader {
                fx.newLeader(row: anchor.row, rank: anchor.rank, color: color, passedRankY: passed)
            } else {
                fx.overtake(row: anchor.row, rank: anchor.rank, color: color, passedRankY: passed)
            }
        }
        if let lastLive, lastLive > 0, live > lastLive * 1.08 + 20 {
            fx.punch(number: tick.layout.number, rise: (live - lastLive) / max(1, lastLive))
        }
        if let lastPeak, snapshot.peak > lastPeak + 1 {
            fx.newPeak(at: CGPoint(x: tick.layout.number.midX, y: tick.layout.number.midY))
        }
        lastLive = live
        lastPeak = snapshot.peak
    }
}

