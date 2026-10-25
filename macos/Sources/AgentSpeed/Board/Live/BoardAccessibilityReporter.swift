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
        let label = l10n("a11y.board", sources, tick.valueText, tick.title)
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
        tick.rows.compactMap { row -> NSAccessibilityElement? in
            guard case .group(let index) = row.kind, index < tick.groups.count else { return nil }
            let group = tick.groups[index]
            let box = layout?.groupRows[group.id]?.row ?? .zero
            let frame = CGRect(x: frameInScreen.minX + box.minX, y: frameInScreen.maxY - box.maxY,
                               width: box.width, height: box.height)
            let element = NSAccessibilityElement.element(withRole: .staticText, frame: frame,
                                                         label: Self.rowLabel(group, rank: index + 1, l10n: l10n),
                                                         parent: parent) as? NSAccessibilityElement
            return element
        }
    }

    /// `第 1 名，tihu，每秒 7.88k 個 token，推理強度 xhigh，加速 Fast，100 個子 agent`.
    static func rowLabel(_ group: BoardGroup, rank: Int, l10n: Localizer) -> String {
        var parts = [l10n("a11y.board.row", rank, project(of: group, l10n), SpeedFormat.speed(group.live))]
        let capsule = EffortCapsule(group.root.modes)
        if capsule.effort != nil || capsule.speed != nil { parts.append(capsule.accessibilityText(l10n)) }
        if !group.subs.isEmpty { parts.append(l10n("boards.subCount", group.subs.count)) }
        let separator = l10n.language == .ja ? "、" : l10n.language.usesLatinCase ? ", " : "，"
        return parts.joined(separator: separator)
    }

    static func project(of group: BoardGroup, _ l10n: Localizer) -> String {
        group.root.project.isEmpty ? l10n(group.root.source.titleKey) : group.root.project
    }
}
