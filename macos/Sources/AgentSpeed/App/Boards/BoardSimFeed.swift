import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit

/// Feeds a board from `BoardSimulation` one engine tick at a time, as the prototypes' pages do: makes the tick's
/// `BoardTick`, lays it out with the style's real content builder (only when asked, so a board nobody sees costs no
/// layout) and plays the tick's heat, overtakes, new leader, number punch and new peak on the effects.
@MainActor
final class BoardSimFeed {
    var simulation: BoardSimulation
    let builder: BoardContentBuilding
    let style: BoardStyle
    let size: BoardSize
    let l10n: Localizer
    let redline: Double
    private(set) var snapshot: BoardSimulation.Snapshot
    private(set) var tick: BoardTick
    private var cached: BoardContentLayout?
    private var lastLive: Double?
    private var lastPeak: Double?

    init(simulation: BoardSimulation, style: BoardStyle, size: BoardSize, dark: Bool, l10n: Localizer, redline: Double = 2000,
         highContrast: Bool = false) {
        self.simulation = simulation
        self.style = style
        self.size = size
        self.l10n = l10n
        self.redline = redline
        builder = BoardContentBuilders.make(style: style, size: size, dark: dark, l10n: l10n, capsule: nil, highContrast: highContrast)
        snapshot = self.simulation.snapshot()
        tick = BoardSimTick.make(snapshot, style: style, size: size, redline: redline, previousPeak: nil, l10n: l10n)
    }

    /// The harnesses' language: `AGENT_SPEED_BOARD_LANG` (zh-Hant, zh-Hans, en, ja), else zh-Hant as the prototypes.
    static func harnessLocalizer() -> Localizer {
        let raw = ProcessInfo.processInfo.environment["AGENT_SPEED_BOARD_LANG"] ?? "zh-Hant"
        return Localizer(preference: LanguagePreference(rawValue: raw) ?? .zhHant)
    }

    /// Advances one engine tick.
    func step() {
        let peak = snapshot.peak
        simulation.advance()
        snapshot = simulation.snapshot()
        let next = BoardSimTick.make(snapshot, style: style, size: size, redline: redline, previousPeak: peak, l10n: l10n)
        // An equal tick keeps its layout (as the app's boards do), so an idle board lays out nothing.
        guard next != tick else { return }
        tick = next
        cached = nil
    }

    /// The content of the current tick, laid out once.
    var layout: BoardContentLayout {
        if let cached { return cached }
        let layout = builder.layout(tick)
        cached = layout
        return layout
    }

    /// The current tick's items for a board; one-shot accents come with the first call only.
    func takeItems() -> [BoardContentItem] {
        _ = layout
        return cached?.takeItems() ?? []
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
        if BoardContentRules.punches(from: lastLive, to: live) {
            fx.punch(number: layout.number, rise: (live - (lastLive ?? 0)) / max(1, lastLive ?? 1))
        }
        if let lastPeak, snapshot.peak > lastPeak + 1 {
            fx.newPeak(at: CGPoint(x: layout.number.midX, y: layout.number.midY))
        }
    }
}

/// A `BoardSimulation` snapshot as the app's feed would make it: agents as `AgentStatus` grouped by `BoardGrouping`
/// in the simulation's ranked order, planned rows, a once-per-second history and the footer texts.
@MainActor
enum BoardSimTick {
    /// sim.js `TITLES`: the title key reached at each fraction of the redline.
    private static let titles: [(String, Double)] = [
        ("walk", 0.5 / 2000), ("bicycle", 0.02), ("ebike", 0.03), ("motorcycle", 0.04), ("car", 0.06), ("sportsCar", 0.08),
        ("bulletTrain", 0.12), ("raceCar", 0.17), ("maglev", 0.24), ("jetliner", 0.34), ("fighterJet", 0.5),
        ("hypersonic", 0.7), ("rocket", 1.0),
    ]

    static func make(_ snapshot: BoardSimulation.Snapshot, style: BoardStyle, size: BoardSize, redline: Double, previousPeak: Double?,
                     l10n: Localizer) -> BoardTick {
        let agents = snapshot.groups.flatMap { [$0.root] + $0.subs }.map(status)
        let grouped = Dictionary(uniqueKeysWithValues: BoardGrouping.groups(agents, metric: .output).map { ($0.id, $0) })
        let groups = snapshot.groups.compactMap { grouped[$0.id] }
        let liveByID = Dictionary(uniqueKeysWithValues: agents.map { ($0.id, $0.live.output) })
        let rows = BoardLayoutPlanner.plan(groups.map { group in BoardPlanGroup(group) { liveByID[$0] ?? 0 } },
                                           metrics: .of(style, size))
        let live = snapshot.live
        let title = titles.last { live >= $0.1 * redline }.map { "title." + $0.0 } ?? "tier.idle"
        // As the app's `BoardHistory`: the latest value of each simulated second, so the newest point follows every
        // tick and the older ones move once a second.
        var seconds = BoardHistory()
        let start = snapshot.time - Double(snapshot.history.count - 1) * 0.5
        for (index, value) in snapshot.history.enumerated() {
            seconds.record(value, atMs: Int64(((start + Double(index) * 0.5) * 1000).rounded()))
        }
        let history = seconds.values
        return BoardTick(
            value: live, valueText: SpeedFormat.speed(live), metric: .output, redline: redline,
            heat: HeatPalette.heat(value: live, redline: redline), overdrive: HeatPalette.overdrive(value: live, redline: redline),
            title: l10n(title), generating: snapshot.generating, tools: snapshot.tools, history: history,
            groups: groups, rows: rows, events: snapshot.events, newPeak: previousPeak.map { snapshot.peak > $0 + 1 } ?? false,
            todayText: SpeedFormat.tokens(Int(snapshot.today)), costText: String(format: "$%.2f", snapshot.cost),
            peakText: SpeedFormat.speed(snapshot.peak), peak: snapshot.peak)
    }

    private static func status(_ agent: BoardSimulation.Agent) -> AgentStatus {
        var facts: [ExecutionModeFact] = []
        if let effort = agent.effort,
           let fact = ExecutionModeFact(kind: .effort, rawValue: effort, evidence: .recorded, scope: .turn) { facts.append(fact) }
        if let speed = agent.speed,
           let fact = ExecutionModeFact(kind: .serviceTier, rawValue: speed, evidence: .recorded, scope: .turn) { facts.append(fact) }
        let phase: AgentPhase = switch agent.phase {
        case .generating: .generating
        case .tools: .tools
        case .idle: .idle
        }
        return AgentStatus(id: agent.id, source: SourceKind(rawValue: agent.source) ?? .claudeCode,
                           project: agent.parentID == nil ? agent.project : agent.label, model: agent.model, phase: phase,
                           isSubagent: agent.parentID != nil, live: Rate(output: agent.live, total: agent.live * 1.6),
                           recentSpeed: nil, lastActivity: Date(timeIntervalSince1970: 1_800_000_000 + agent.lastActive),
                           parentId: agent.parentID, modes: ExecutionModes(facts))
    }
}
