import AgentSpeedCore
import AppKit
import SwiftUI

/// One agent: phase badge, looping mark, project, source and model, effort capsule, intelligence, live speed. The phase
/// in words, the model's grade and the work signals are in the tooltip and for VoiceOver.
struct AgentRow: View {
    let model: AppModel
    let agent: AgentStatus
    /// Visual nesting level in the parent tree; roots are 0.
    let depth: Int
    /// Snapshot time, the reference for "x minutes ago".
    let now: Date
    /// The shared effort capsule column of the visible rows.
    var capsuleColumn = AgentCapsuleColumn.none
    /// The intelligence cell's width, the same for every row.
    let intelligenceWidth: CGFloat

    /// Below this a truncated detail would show only a clipped sliver of its first glyph, so it steps aside.
    static let minimumDetail: CGFloat = 20

    @Environment(\.colorScheme) private var colorScheme
    @State private var detailFits = true

    var body: some View {
        let l10n = model.l10n
        let sourceName = l10n(agent.source.titleKey)
        let project = agent.project.isEmpty ? sourceName : agent.project
        // Model first: when space runs out the source name truncates, and the badge color still carries it.
        let detail = [agent.model, sourceName].compactMap { $0?.isEmpty == false ? $0 : nil }.joined(separator: " · ")
        // Attributed traffic today (DESIGN 10, Agents): the line ends with `· ↓ 2.10 MB`, kept whole while the model and
        // source names give way; both directions in the tooltip.
        let traffic = agentTraffic
        let received = traffic.flatMap { $0.rx > 0 ? "↓ " + TrafficFormat.total($0.rx) : nil }
        let trafficSuffix = received.map { " · " + $0 }
        let speed = model.value(of: agent.live)
        let status = status(l10n)
        let capsule = EffortCapsule(agent.modes)
        let modeLines = capsule.effort != nil || capsule.speed != nil ? capsule.tooltipLines(l10n) : []
        let live = LiveSpeedValue(
            speed: speed,
            phase: agent.phase,
            l10n: l10n,
            reduceMotion: model.accessibility.reduceMotion
        )
        let projectLabel = agent.isSubagent ? "\(project), \(l10n("agents.subagent"))" : project
        let looping = AgentSignalText.looping(agent.signals, l10n)
        let reduceMotion = model.accessibility.reduceMotion
        let intelligence = agent.intelligenceForm

        HStack(spacing: Spacing.s) {
            PhaseBadge(
                phase: agent.phase,
                tint: agent.source.tint(dark: colorScheme == .dark),
                animated: !model.accessibility.reduceMotion
            )
            .anchorPreference(key: BadgeAnchors.self, value: .bounds) { [agent.id: $0] }

            HStack(alignment: .firstTextBaseline, spacing: 6) {
                HStack(alignment: .firstTextBaseline, spacing: LoopingMark.gap) {
                    if looping != nil { LoopingMark().transition(.opacity) }
                    Text(project)
                        .font(DashboardFont.rowTitle)
                        .lineLimit(1)
                }
                .layoutPriority(1)
                .animation(LoopingMark.animation(appearing: looping != nil, reduceMotion: reduceMotion), value: looping != nil)
                HStack(spacing: 0) {
                    let readable = min(Self.minimumDetail, Self.detailWidth(detail) - 1)
                    Text(detail)
                        .lineLimit(1)
                        .truncationMode(.tail)
                        .opacity(detailFits ? 1 : 0)
                        .onGeometryChange(for: Bool.self) { $0.size.width >= readable } action: { detailFits = $0 }
                    if let trafficSuffix {
                        Text(trafficSuffix)
                            .lineLimit(1)
                            .fixedSize()
                    }
                }
                .font(DashboardFont.detail)
                .foregroundStyle(.dashboardSecondary)
            }
            .frame(maxWidth: .infinity, alignment: .leading)

            HStack(spacing: AgentCapsuleColumn.gap) {
                if capsuleColumn.width > 0 {
                    EffortCapsuleView(
                        capsule: capsule,
                        compact: capsuleColumn.compact,
                        width: capsuleColumn.width,
                        reduceMotion: model.accessibility.reduceMotion,
                        shimmers: model.effectsLevel != .off
                    )
                    .animation(.easeInOut(duration: 0.2), value: capsuleColumn)
                }
                HStack(alignment: .firstTextBaseline, spacing: AgentIntelligenceColumn.gap) {
                    AgentIntelligenceCell(intelligence: intelligence, width: intelligenceWidth)
                    live
                }
            }
        }
        .frame(height: DashboardLayout.rowHeight)
        .padding(.leading, CGFloat(depth) * DashboardLayout.subagentIndent)
        .contentShape(Rectangle())
        .help(([looping, traffic.map { "\(project) · \(status)\n" + l10n("traffic.agent.tooltip", TrafficText(model: model).pair($0)) }
            ?? "\(project) · \(status)"] + modeLines + AgentSignalText.intelligence(agent, l10n)
            + AgentSignalText.closing(agent.signals, l10n)).compactMap { $0 }.joined(separator: "\n"))
        .accessibilityElement(children: .ignore)
        // The intelligence follows the live value; the label ends with the capsule's effort and speed lines (DESIGN 4).
        .accessibilityLabel(
            [projectLabel, looping, detail, received, live.accessibilityText,
             AgentSignalText.intelligenceAccessibility(intelligence, l10n),
             modeLines.isEmpty ? nil : capsule.accessibilityText(l10n)]
                .compactMap { $0?.isEmpty == false ? $0 : nil }
                .joined(separator: ", ")
        )
        .accessibilityValue(status)
    }

    /// The detail's untruncated width in the row's detail font.
    private static func detailWidth(_ detail: String) -> CGFloat {
        (detail as NSString).size(withAttributes: [.font: NSFont.monospacedDigitSystemFont(ofSize: 11, weight: .regular)]).width
    }

    /// Today's traffic attributed to this agent's tree (roots only; sub-agents run inside their parent's process).
    private var agentTraffic: TrafficBytes? {
        guard model.settings.trafficEnabled, agent.parentId == nil,
              let traffic = model.traffic.snapshot.agents[agent.id] else { return nil }
        let bytes = traffic.today.total(includeTools: model.settings.trafficIncludeTools)
        return bytes.isZero ? nil : bytes
    }

    private func status(_ l10n: Localizer) -> String {
        switch agent.phase {
        case .generating: return l10n("phase.generating")
        case .tools: return l10n("phase.tools")
        case .idle:
            let seconds = Int(now.timeIntervalSince(agent.lastActivity).rounded(.down))
            let ago: String
            if seconds < 10 {
                ago = l10n("time.justNow")
            } else if seconds < 60 {
                ago = l10n("time.secondsAgo", seconds)
            } else {
                ago = l10n("time.minutesAgo", seconds / 60)
            }
            return "\(l10n("phase.idle")) · \(ago)"
        }
    }
}
