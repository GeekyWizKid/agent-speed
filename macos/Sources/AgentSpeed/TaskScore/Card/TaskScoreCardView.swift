import AgentSpeedCore
import SwiftUI

/// The score card (DESIGN 12, *Score card*): the breakdown behind the number. 300 pt wide, as tall as its content,
/// padding 14, blocks 12 apart, hairlines under the header and above the facts. It is a tooltip: never focusable, and
/// its content reaches VoiceOver as the row's extra content (`TaskScoreCardText.accessibilityLines`).
struct TaskScoreCardView: View {
    let model: TaskScoreCardModel
    let l10n: Localizer
    var fit: TaskScoreCardFit = .full

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            TaskScoreCardHeader(header: model.header, state: model.score?.state, l10n: l10n)
            TaskCardHairline()
                .padding(.vertical, TaskCardLayout.blockSpacing)
            switch model.body {
            case .unsupported:
                Text(l10n("task.unsupported"))
                    .font(TaskCardFont.status)
                    .foregroundStyle(.secondary)
                    .lineSpacing(1.5)
                    .fixedSize(horizontal: false, vertical: true)
                facts
            case .scored(let score):
                scored(score)
            }
        }
        .padding(TaskCardLayout.padding)
        .frame(width: TaskCardLayout.width, alignment: .topLeading)
        .fixedSize(horizontal: false, vertical: true)
        .accessibilityHidden(true)
    }

    @ViewBuilder
    private func scored(_ score: TaskScoreCardScore) -> some View {
        TaskScoreCardScoreBlock(score: score, l10n: l10n)
        TaskScoreCardDimensions(score: score, l10n: l10n, meanings: fit == .full)
            .padding(.top, TaskCardLayout.blockSpacing + 2)
        let cells = TaskScoreCardText.predictions(score, l10n)
        if !cells.isEmpty {
            TaskScoreCardPredictions(cells: cells, source: model.header.source, l10n: l10n)
                .padding(.top, TaskCardLayout.blockSpacing + 2)
                .transition(.opacity)
        }
        let signals = TaskScoreCardText.signals(score, l10n)
        if !signals.isEmpty {
            TaskScoreCardSignals(lines: signals, l10n: l10n)
                .padding(.top, TaskCardLayout.blockSpacing + 2)
                .transition(.opacity)
        }
        facts
        if fit != .noMeaningsOrFootnote {
            Text(l10n("task.card.footnote"))
                .font(TaskCardFont.footnote)
                .foregroundStyle(.taskCardDescription)
                .lineSpacing(1.5)
                .fixedSize(horizontal: false, vertical: true)
                .padding(.top, model.facts.isEmpty ? TaskCardLayout.blockSpacing + 2 : 10)
        }
    }

    @ViewBuilder
    private var facts: some View {
        if !model.facts.isEmpty {
            TaskCardHairline()
                .padding(.top, TaskCardLayout.blockSpacing)
                .padding(.bottom, 9)
            VStack(alignment: .leading, spacing: 2) {
                ForEach(Array(model.facts.enumerated()), id: \.offset) { _, fact in
                    Text(fact)
                        .font(TaskCardFont.fact)
                        .foregroundStyle(.secondary)
                        .lineLimit(2)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
        }
    }
}

/// DESIGN 12, *Fit*: a card taller than the visible screen minus 16 pt drops the dimensions' meanings first, then the
/// footnote. Its accessibility content stays complete.
enum TaskScoreCardFit: Int, CaseIterable, Sendable {
    case full, noMeanings, noMeaningsOrFootnote

    /// Room the card keeps from the visible frame's height.
    static let margin: CGFloat = 16
}

/// The card's surface where no system material can be drawn (offscreen previews) and under Reduce Transparency: the
/// solid fill of DESIGN 12, radius 12 and the 0.5 pt separator stroke.
struct TaskScoreCardSolidSurface: ViewModifier {
    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.colorSchemeContrast) private var contrast

    func body(content: Content) -> some View {
        let shape = RoundedRectangle(cornerRadius: TaskCardLayout.radius, style: .continuous)
        content
            .background(shape.fill(TaskScorePalette.solidCard(dark: colorScheme == .dark)))
            .overlay(shape.strokeBorder(Color(nsColor: .separatorColor), lineWidth: contrast == .increased ? 1 : 0.5))
            .clipShape(shape)
    }
}
