import AgentSpeedCore
import SwiftUI

/// 評分構成: five rows, each a bold label with `value / max`, a thin bar and a one-line meaning.
struct TaskScoreCardDimensions: View {
    let score: TaskScoreCardScore
    let l10n: Localizer

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            TaskCardSectionTitle(title: l10n("task.dims.title"))
            VStack(alignment: .leading, spacing: TaskCardLayout.dimensionSpacing) {
                ForEach(TaskScoreCardText.dimensionRows(score)) { row in
                    TaskScoreCardDimensionRow(row: row, l10n: l10n)
                }
            }
        }
    }
}

private struct TaskScoreCardDimensionRow: View {
    let row: TaskDimensionRow
    let l10n: Localizer

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                Text(l10n(row.dimension.titleKey))
                    .font(TaskCardFont.dimensionLabel)
                    .lineLimit(1)
                    .layoutPriority(1)
                Spacer(minLength: 0)
                value
            }
            TaskDimensionBar(row: row)
                .padding(.top, 5)
            Text(l10n(row.dimension.meaningKey))
                .font(TaskCardFont.meaning)
                .foregroundStyle(.taskCardDescription)
                .lineSpacing(1)
                .fixedSize(horizontal: false, vertical: true)
                .padding(.top, 4)
        }
    }

    @ViewBuilder
    private var value: some View {
        switch row.value {
        case .unverified:
            Text(l10n("task.state.unverified"))
                .font(TaskCardFont.dimensionWord)
                .foregroundStyle(.tertiary)
                .lineLimit(1)
        case .pending:
            Text(l10n("task.state.pending"))
                .font(TaskCardFont.dimensionWord)
                .foregroundStyle(.secondary)
                .lineLimit(1)
        case .absent:
            Text(verbatim: "–")
                .font(TaskCardFont.dimensionValue)
                .foregroundStyle(.tertiary)
        case .value(let value):
            (Text(verbatim: "\(value)")
                + Text(verbatim: " / \(row.dimension.maximum)").foregroundStyle(.tertiary))
                .font(TaskCardFont.dimensionValue)
                .lineLimit(1)
                .contentTransition(.numericText())
        }
    }
}

/// 預測: equal cells while the agent is live with a score, each with its percentage, label and the model's flag, then
/// the note on calibration. Neutral colors: a high stall probability is a number, not an alarm.
struct TaskScoreCardPredictions: View {
    let cells: [TaskPredictionCell]
    let source: SourceKind
    let l10n: Localizer

    @Environment(\.colorScheme) private var colorScheme

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            TaskCardSectionTitle(title: l10n("task.predict.title"))
            HStack(alignment: .top, spacing: 8) {
                ForEach(cells) { cell in
                    VStack(alignment: .leading, spacing: 0) {
                        Text(cell.percent)
                            .font(TaskCardFont.percent)
                            .contentTransition(.numericText())
                        Text(cell.label)
                            .font(TaskCardFont.cellLabel)
                            .foregroundStyle(.secondary)
                            .padding(.top, 2)
                        Text(cell.flag)
                            .font(TaskCardFont.flag)
                            .foregroundStyle(.tertiary)
                            .padding(.top, 3)
                    }
                    .lineLimit(1)
                    .minimumScaleFactor(0.8)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(.vertical, 8)
                    .padding(.horizontal, 10)
                    .background(
                        RoundedRectangle(cornerRadius: 8, style: .continuous)
                            .fill(TaskScorePalette.cellFill(dark: colorScheme == .dark))
                    )
                }
            }
            Text(TaskScoreCardText.predictionNote(source, l10n))
                .font(TaskCardFont.meaning)
                .foregroundStyle(.taskCardDescription)
                .lineSpacing(1)
                .fixedSize(horizontal: false, vertical: true)
                .padding(.top, 7)
        }
    }
}

/// 行為: looping (the amber mark of section 4), repeating, oscillating edits and the summary of recent actions.
struct TaskScoreCardSignals: View {
    let lines: [TaskSignalLine]
    let l10n: Localizer

    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.colorSchemeContrast) private var contrast

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            TaskCardSectionTitle(title: l10n("task.signals.title"))
            VStack(alignment: .leading, spacing: 6) {
                ForEach(lines) { line in
                    HStack(alignment: .firstTextBaseline, spacing: 6) {
                        Image(systemName: glyph(line.kind))
                            .font(TaskCardFont.signalGlyph)
                            .foregroundStyle(glyphStyle(line.kind))
                            .frame(width: 16, alignment: .center)
                        Text(line.text)
                            .font(TaskCardFont.signal)
                            .foregroundStyle(line.kind == .looping ? AnyShapeStyle(.primary) : AnyShapeStyle(.secondary))
                            .fixedSize(horizontal: false, vertical: true)
                    }
                }
            }
        }
    }

    private func glyph(_ kind: TaskSignalLine.Kind) -> String {
        switch kind {
        case .looping: "arrow.triangle.2.circlepath"
        case .repeating: "repeat"
        case .oscillation: "arrow.left.arrow.right"
        case .summary: "list.bullet"
        }
    }

    private func glyphStyle(_ kind: TaskSignalLine.Kind) -> AnyShapeStyle {
        guard kind == .looping else { return AnyShapeStyle(.secondary) }
        let dark = colorScheme == .dark
        return AnyShapeStyle(DashboardPalette.legible(DashboardPalette.amber(dark: dark), dark: dark,
                                                      minimum: contrast == .increased ? 4.5 : 3).color)
    }
}
