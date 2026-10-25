import AgentSpeedCore
import AgentSpeedOrbModel
import Foundation

/// What one board shows (DESIGN 2c Content; METRICS 6.8): its `BoardLive` state, its own tier and title trackers
/// (section 1 hysteresis and hold) with the General redline and title theme, its click-to-expand group, and the tick
/// its content and effects draw. `onTick` runs after every engine tick and after an expansion change.
@MainActor
final class BoardRuntime {
    let id: String
    private(set) var config: BoardConfig
    private(set) var live: BoardLive
    private(set) var tick: BoardTick?
    /// The frame behind `tick`, with the raw ranking events (before Celebrate new peaks folds new leaders).
    private(set) var frame = BoardLiveFrame()
    private(set) var expansion = BoardExpansion()
    let gauge = LiveGauge()
    var onTick: ((BoardRuntime) -> Void)?
    private var pendingEnginePeak = false

    init(config: BoardConfig) {
        id = config.id
        self.config = config
        live = BoardLive(sources: config.sources)
    }

    var layoutMetrics: BoardLayoutMetrics { .of(config.style, config.size) }

    func update(config: BoardConfig) {
        self.config = config
        live.setSources(config.sources)
    }

    /// The engine's all-sources `newPeak`; an all-sources board celebrates it on its next tick.
    func engineReachedNewPeak() {
        pendingEnginePeak = true
    }

    /// Feeds one snapshot (every engine tick, and once more when a setting the tick depends on changes).
    func ingest(_ snapshot: SpeedSnapshot, model: AppModel, now: TimeInterval) {
        let previewing = model.store.isPreviewing
        expansion.expire(now: now, present: Set(frame.groups.map(\.id)))
        let metric = model.settings.metric
        frame = live.ingest(snapshot, metric: metric, layout: layoutMetrics, expanded: expansion.group,
                            engineNewPeak: pendingEnginePeak, previewing: previewing)
        pendingEnginePeak = false
        let redline = model.settings.redline(for: metric)
        if gauge.redline != redline { gauge.redline = redline }
        gauge.ingest(frame.value, now: now)
        tick = makeTick(model: model, snapshot: snapshot, previewing: previewing)
        onTick?(self)
    }

    // MARK: Expansion

    /// A click on a group row toggles its expansion (DESIGN 2c Layout per size).
    func toggleExpansion(of group: String, model: AppModel, now: TimeInterval) {
        expansion.toggle(group, now: now)
        replan(model: model)
    }

    /// The pointer over the board keeps an expansion open.
    func touch(now: TimeInterval) {
        expansion.touch(now: now)
    }

    private func replan(model: AppModel) {
        frame = live.replan(layout: layoutMetrics, expanded: expansion.group)
        guard var tick else { return }
        tick.rows = frame.rows
        tick.events = []
        tick.newPeak = false
        self.tick = tick
        onTick?(self)
    }

    // MARK: Tick

    private func makeTick(model: AppModel, snapshot: SpeedSnapshot, previewing: Bool) -> BoardTick {
        let l10n = model.l10n
        let settings = model.settings
        let redline = settings.redline(for: frame.metric)
        let unavailableKey: String? = switch frame.state {
        case .available: nil
        case .allDisabled: "source.disabled"
        case .allNotFound: "source.notFound"
        }
        let title = unavailableKey.map { l10n($0) }
            ?? model.titles.text(gauge.title, in: model.titles.theme(forOrb: nil), l10n: l10n)
        let celebrate = settings.celebratePeaks
        let events = celebrate ? frame.events : frame.events.map { event in
            var plain = event
            if plain.kind == .newLeader { plain.kind = .overtake }
            return plain
        }
        return BoardTick(
            value: frame.value,
            valueText: unavailableKey == nil ? SpeedFormat.speed(frame.value) : "–",
            metric: frame.metric,
            redline: redline,
            heat: HeatPalette.heat(value: frame.value, redline: redline),
            overdrive: HeatPalette.overdrive(value: frame.value, redline: redline),
            title: title,
            generating: frame.generating,
            tools: frame.tools,
            history: frame.history,
            groups: frame.groups,
            rows: frame.rows,
            events: events,
            newPeak: frame.newPeak && celebrate,
            todayText: l10n("boards.today", SpeedFormat.tokens(frame.todayOutput)),
            costText: costText(model: model, snapshot: snapshot, previewing: previewing),
            peakText: l10n("boards.peak", SpeedFormat.speed(frame.peak))
        )
    }

    /// Today's cost of the board's billable sources (DESIGN 9), as the dashboard writes it; nil when nothing is
    /// billable or the preview runs (it has no prices).
    private func costText(model: AppModel, snapshot: SpeedSnapshot, previewing: Bool) -> String? {
        guard !previewing, frame.state == .available else { return nil }
        let cost = model.cost
        let reading = OrbSubset.cost(snapshot, sources: config.sources.sourceSet, billable: cost.billable)
        guard reading.state != .subscriptionOnly else { return nil }
        guard reading.todayRequests > 0 else { return cost.amountText(microUsd: 0) }
        let text = cost.tallyText(reading.today, requests: reading.todayRequests)
        return text == "–" ? nil : text
    }
}
