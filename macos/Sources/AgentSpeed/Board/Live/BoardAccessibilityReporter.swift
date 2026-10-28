import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit

/// VoiceOver for one board (DESIGN 2c Accessibility): the board is a list labelled `a11y.board` (its sources, value
/// and title), each shown group row an item labelled `a11y.board.row` with its effort, speed and sub-agent count.
/// Labels update at most once per second; a new leader is announced politely at most once per 10 s.
@MainActor
final class BoardAccessibilityReporter {
    static let interval: TimeInterval = 1

    private weak var host: BoardHostView?
    private var publishedAt: TimeInterval = -.infinity
    private var announcer = BoardAnnouncer()
    private var rows: [NSAccessibilityElement] = []

    init(host: BoardHostView) {
        self.host = host
        host.setAccessibilityElement(true)
        host.setAccessibilityRole(.list)
    }

    /// After each tick: announces a new leader, then refreshes the labels when a second has passed (or `force`).
    func update(model: AppModel, runtime: BoardRuntime, layout: BoardContentLayout?, frameInScreen: CGRect,
                now: TimeInterval, force: Bool = false) {
        guard let host, let tick = runtime.tick else { return }
        let l10n = model.l10n
        if let leader = runtime.frame.events.first(where: { $0.kind == .newLeader }),
           let group = tick.groups.first(where: { $0.id == leader.id }), announcer.mayAnnounce(now: now) {
            NSAccessibility.post(element: host, notification: .announcementRequested, userInfo: [
                .announcement: l10n("a11y.board.newLeader", Self.project(of: group, l10n)),
                .priority: NSAccessibilityPriorityLevel.medium.rawValue,
            ])
        }
        guard force || now - publishedAt >= Self.interval else { return }
        publishedAt = now
        let sources = model.boards.sourcesText(of: runtime.config, l10n: l10n)
        let label = Self.join([l10n("a11y.board", sources, tick.valueText, tick.title)]
            + Self.metricWords(tick.metrics, currency: model.cost.currencyName, l10n: l10n), l10n)
        if host.accessibilityLabel() != label {
            host.setAccessibilityLabel(label)
            NSAccessibility.post(element: host, notification: .titleChanged)
        }
        host.setAccessibilityHelp(l10n("boards.title"))
        rows = rowElements(tick: tick, layout: layout, frameInScreen: frameInScreen, l10n: l10n, parent: host,
                           size: runtime.config.size, currency: model.cost.currencyName)
        host.setAccessibilityChildren(rows)
    }

    private func rowElements(tick: BoardTick, layout: BoardContentLayout?, frameInScreen: CGRect, l10n: Localizer,
                             parent: NSView, size: BoardSize, currency: String) -> [NSAccessibilityElement] {
        tick.rows.compactMap { row -> NSAccessibilityElement? in
            guard case .group(let index) = row.kind, index < tick.groups.count else { return nil }
            let group = tick.groups[index]
            let box = layout?.groupRows[group.id]?.row ?? .zero
            let frame = CGRect(x: frameInScreen.minX + box.minX, y: frameInScreen.maxY - box.maxY,
                               width: box.width, height: box.height)
            let cost = tick.metrics.readouts.twoLineRows(size) && group.live > BoardGrouping.idle ? tick.metrics.rowCosts[group.id] : nil
            let element = NSAccessibilityElement.element(withRole: .staticText, frame: frame,
                                                         label: Self.rowLabel(group, rank: index + 1, l10n: l10n,
                                                                              intelligence: tick.metrics.readouts.intelligence,
                                                                              cost: cost, currency: currency),
                                                         parent: parent) as? NSAccessibilityElement
            return element
        }
    }

    /// `第 1 名，tihu，每秒 7.88k 個 token，智力 123，推理強度 xhigh，加速 Fast，100 個子 agent，費用每秒 0.012 美元`: with 智力
    /// on, its live index or grade right after the speed; with a cost line, the cost at the end.
    static func rowLabel(_ group: BoardGroup, rank: Int, l10n: Localizer, intelligence: Bool = false, cost: BoardMoney? = nil,
                         currency: String = "") -> String {
        var parts = [l10n("a11y.board.row", rank, project(of: group, l10n), SpeedFormat.speed(group.live))]
        if intelligence, let words = AgentSignalText.intelligenceAccessibility(group.intelligence, l10n) { parts.append(words) }
        let capsule = EffortCapsule(group.root.modes)
        if capsule.effort != nil || capsule.speed != nil { parts.append(capsule.accessibilityText(l10n)) }
        if !group.subs.isEmpty { parts.append(l10n("boards.subCount", group.subs.count)) }
        if let cost {
            switch cost.state {
            case .value:
                let number = cost.atLeast ? l10n("a11y.orb.costAtLeast", cost.number) : cost.number
                parts.append(l10n("a11y.board.rowCost", number, currency))
            case .unknown: parts.append(l10n("cost.unknown"))
            case .tag: parts.append(l10n("cost.subscription.tag"))
            }
        }
        return join(parts, l10n)
    }

    /// The board label's live metrics words (DESIGN 2c Live metrics, *Accessibility*), in order: the intelligence form
    /// (with 思考 while it shows), or 思考 alone, `intel.none` while there is no value; then the cost.
    static func metricWords(_ metrics: BoardMetricsTick, currency: String, l10n: Localizer) -> [String] {
        let readouts = metrics.readouts
        var words: [String] = []
        if readouts.intelligence {
            if let shown = metrics.intelligence {
                words.append(metrics.showsThinking ? l10n("a11y.orb.intel", String(shown), metrics.thinking)
                    : l10n("a11y.orb.intel.only", String(shown)))
            } else {
                words.append(l10n("intel.none"))
            }
        } else if readouts.thinking {
            words.append(l10n("a11y.board.thinking", metrics.quiet ? "0" : metrics.thinking))
        }
        if readouts.cost {
            switch metrics.cost.state {
            case .value:
                let number = metrics.cost.atLeast ? l10n("a11y.orb.costAtLeast", metrics.cost.number) : metrics.cost.number
                words.append(l10n("a11y.board.cost", number, currency))
            case .unknown: words.append(l10n("a11y.board.costUnknown"))
            case .tag: words.append(l10n("cost.subscriptionOnly"))
            }
        }
        return words
    }

    static func join(_ parts: [String], _ l10n: Localizer) -> String {
        let separator = l10n.language == .ja ? "、" : l10n.language.usesLatinCase ? ", " : "，"
        return parts.joined(separator: separator)
    }

    static func project(of group: BoardGroup, _ l10n: Localizer) -> String {
        group.root.project.isEmpty ? l10n(group.root.source.titleKey) : group.root.project
    }
}
