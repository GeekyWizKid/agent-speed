import AgentSpeedCore
import AgentSpeedOrbModel
import SwiftUI

/// A group row's hover tooltip (DESIGN 2c Interaction): project and source, model, the effort and speed lines of
/// section 4, and the group speed with its sub-agents' share; then the live metrics lines (Live metrics, *Tooltip*).
/// The engine keeps no per-agent total for today, so the prototype's "today's output" line has no data to show.
enum BoardTooltip {
    static let delay: TimeInterval = 0.35

    /// `money`: the group's cost per second, formatted as 費用 shows it.
    @MainActor
    static func lines(_ group: BoardGroup, metric: SpeedMetric, l10n: Localizer, readouts: BoardReadouts = .none,
                      money: BoardMoney? = nil) -> [String] {
        let root = group.root
        let source = l10n(root.source.titleKey)
        var lines = [(root.project.isEmpty ? source : root.project) + " · " + source]
        if let model = root.model, !model.isEmpty { lines.append(model) }
        let capsule = EffortCapsule(root.modes)
        if capsule.effort != nil || capsule.speed != nil { lines += capsule.tooltipLines(l10n) }
        let speed = SpeedFormat.speed(group.live)
        lines.append(group.subs.isEmpty ? l10n("mac.boards.tooltip.speed", speed)
            : l10n("mac.boards.tooltip.speedSubs", speed, SpeedFormat.speed(group.subLive)))
        lines += metricLines(group, l10n: l10n, readouts: readouts, money: money)
        return lines
    }

    /// While 智力 is on: the group's live index and thinking share, the root's grade; while 費用 is on and the group is
    /// live: its cost as shown, then why part or all of it is missing. The disclaimer is the caption.
    @MainActor
    static func metricLines(_ group: BoardGroup, l10n: Localizer, readouts: BoardReadouts, money: BoardMoney?) -> [String] {
        var lines: [String] = []
        let live = group.live > BoardGrouping.idle
        if readouts.intelligence {
            if case .live = group.intelligence, let shown = group.intelligence.shown {
                let records = ([group.root] + group.subs).contains { $0.source.recordsReasoningApart }
                let share = records ? AgentSignalText.percent(group.sums.intelligence.thinkingShare) : "–"
                lines.append(l10n("intel.tooltip", String(shown), share))
            }
            if let grade = group.root.grade { lines.append(AgentSignalText.grade(grade, l10n)) }
        }
        if readouts.cost, live, let money {
            switch money.state {
            case .tag:
                lines.append(l10n("boards.tooltip.subscription", l10n(group.root.source.titleKey)))
            case .unknown:
                lines.append(l10n("boards.tooltip.cost", l10n("cost.unknown")))
            case .value:
                lines.append(l10n("boards.tooltip.cost", (money.atLeast ? "≥" : "") + money.number + " " + money.unit))
                if money.atLeast { lines.append(l10n("cost.partial")) }
            }
        }
        return lines
    }

    /// The tooltip's closing caption while 智力 is on: `intel.disclaimer`.
    @MainActor
    static func caption(readouts: BoardReadouts, l10n: Localizer) -> String? {
        readouts.intelligence ? l10n("intel.disclaimer") : nil
    }
}

/// The tooltip's look: the source chip's material (DESIGN 2b) as a small card, 11 pt text, the first line semibold,
/// the caption (`intel.disclaimer`) smaller and quieter under a little space.
struct BoardTooltipView: View {
    let lines: [String]
    var caption: String?

    @Environment(\.colorScheme) private var colorScheme

    var body: some View {
        let dark = colorScheme == .dark
        VStack(alignment: .leading, spacing: 2) {
            ForEach(Array(lines.enumerated()), id: \.offset) { index, line in
                Text(line)
                    .font(.system(size: 11, weight: index == 0 ? .semibold : .regular))
                    .foregroundStyle(index == 0 ? AnyShapeStyle(.primary) : AnyShapeStyle(.secondary))
                    .monospacedDigit()
            }
            if let caption {
                Text(caption)
                    .font(.system(size: 10))
                    .foregroundStyle(.tertiary)
                    .padding(.top, 3)
            }
        }
        .lineLimit(1)
        .fixedSize()
        .padding(.horizontal, 9)
        .padding(.vertical, 7)
        .background(
            RoundedRectangle(cornerRadius: 8, style: .continuous)
                .fill(dark ? Color(.sRGB, red: 20 / 255, green: 22 / 255, blue: 30 / 255, opacity: 0.92)
                    : Color(.sRGB, red: 1, green: 1, blue: 1, opacity: 0.96))
        )
        .overlay(
            RoundedRectangle(cornerRadius: 8, style: .continuous)
                .strokeBorder(dark ? Color.white.opacity(0.14) : Color.black.opacity(0.1), lineWidth: 0.5)
        )
        .shadow(color: .black.opacity(dark ? 0.35 : 0.14), radius: 5, y: 2)
        .padding(8)
        .accessibilityHidden(true)
    }
}
