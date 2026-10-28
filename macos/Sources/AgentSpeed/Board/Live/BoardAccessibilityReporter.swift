import Accessibility
import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit

/// VoiceOver for one board (DESIGN 2c Accessibility): the board is a list labelled `a11y.board` (its sources, value
/// and title), each shown group row an item labelled `a11y.board.row` with its 智力 words, effort, speed and sub-agent
/// count. While `intelligence` is on, a row carries its score card as extra content (VoiceOver's "more content") and
/// the custom action `a11y.task.card`, which shows the card. Labels update at most once per second; a new leader is
/// announced politely at most once per 10 s.
@MainActor
final class BoardAccessibilityReporter {
    static let interval: TimeInterval = 1

    private weak var host: BoardHostView?
    private var publishedAt: TimeInterval = -.infinity
    private var announcer = BoardAnnouncer()
    private var rows: [NSAccessibilityElement] = []
    /// The custom action `a11y.task.card` on a row (its key): shows its card, or hides it when it shows.
    var onCardAction: ((String) -> Void)?

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
        rows = rowElements(tick: tick, layout: layout, frameInScreen: frameInScreen, l10n: l10n, parent: host)
        host.setAccessibilityChildren(rows)
    }

    private func rowElements(tick: BoardTick, layout: BoardContentLayout?, frameInScreen: CGRect, l10n: Localizer,
                             parent: NSView) -> [NSAccessibilityElement] {
        let intelligence = tick.metrics.readouts.intelligence
        return tick.rows.compactMap { row -> NSAccessibilityElement? in
            guard case .group(let index) = row.kind, index < tick.groups.count else { return nil }
            let group = tick.groups[index]
            let box = layout?.groupRows[group.id]?.row ?? .zero
            let element = BoardRowAccessibilityElement()
            element.setAccessibilityRole(.staticText)
            element.setAccessibilityParent(parent)
            element.setAccessibilityFrame(CGRect(x: frameInScreen.minX + box.minX, y: frameInScreen.maxY - box.maxY,
                                                 width: box.width, height: box.height))
            element.setAccessibilityLabel(Self.rowLabel(group, rank: index + 1, l10n: l10n, intelligence: intelligence))
            guard intelligence else { return element }
            let title = l10n("a11y.task.card")
            element.accessibilityCustomContent = Self.cardContent(BoardScoreCards.group(group, l10n: l10n), l10n: l10n)
            let key = row.key
            element.setAccessibilityCustomActions([NSAccessibilityCustomAction(name: title) { [weak self] in
                MainActor.assumeIsolated { self?.onCardAction?(key) }
                return true
            }])
            return element
        }
    }

    /// The card's lines as a row's extra content, in the card's order (DESIGN 12 *Accessibility*).
    static func cardContent(_ model: TaskScoreCardModel, l10n: Localizer) -> [AXCustomContent] {
        let title = l10n("a11y.task.card")
        return TaskScoreCardText.accessibilityLines(model, l10n).enumerated().map { index, line in
            let content = AXCustomContent(label: title, value: line)
            content.importance = index == 0 ? .high : .default
            return content
        }
    }

    /// `第 1 名，tihu，每秒 7.88k 個 token，智力 86，3 個 agent 中最高，推理強度 xhigh，加速 Fast，100 個子 agent`: with 智力 on,
    /// its score words right after the speed (nothing for none). Rows name no cost (the board's label carries 費用).
    static func rowLabel(_ group: BoardGroup, rank: Int, l10n: Localizer, intelligence: Bool = false) -> String {
        var parts = [l10n("a11y.board.row", rank, project(of: group, l10n), SpeedFormat.speed(group.live))]
        if intelligence, let words = TaskScoreText.words(TaskScoreValue(group.intelligenceReading), l10n) {
            parts.append(l10n("a11y.agent.intel", words))
        }
        let capsule = EffortCapsule(group.root.modes)
        if capsule.effort != nil || capsule.speed != nil { parts.append(capsule.accessibilityText(l10n)) }
        if !group.subs.isEmpty { parts.append(l10n("boards.subCount", group.subs.count)) }
        return join(parts, l10n)
    }

    /// The board label's live metrics words (DESIGN 2c Live metrics, *Accessibility*), in order: the intelligence form
    /// with the score words (with 思考 while it shows), or 思考 alone, `intel.none` in place of the 智力 words while it is
    /// none; then the cost.
    static func metricWords(_ metrics: BoardMetricsTick, currency: String, l10n: Localizer) -> [String] {
        let readouts = metrics.readouts
        var words: [String] = []
        if readouts.intelligence {
            // While 智力 is none, `intel.none` replaces only its words; a shown 思考 is still read.
            words.append(OrbAccessibilityReporter.intelligenceSentence(
                TaskScoreValue(metrics.intelligence), thinking: metrics.showsThinking ? metrics.thinking : nil, l10n))
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

/// A row element that carries its score card's lines as VoiceOver's "more content".
final class BoardRowAccessibilityElement: NSAccessibilityElement, AXCustomContentProvider {
    var accessibilityCustomContent: [AXCustomContent]! = []
}
