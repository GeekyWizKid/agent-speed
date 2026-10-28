import AgentSpeedCore
import SwiftUI

/// Narrow: the by-model table as a list. Line 1 the dots, the id, the median and its strip; line 2 success · stall ·
/// fix failure; line 3 scored and tasks · attempts · first pass (DESIGN 12, *Widths*).
struct TaskModelListView: View {
    let rows: [TaskPerformanceModelRow]
    let l10n: Localizer

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            ForEach(Array(TaskModelTable.sort(rows, by: .standard).enumerated()), id: \.element.id) { index, row in
                if index > 0 { Hairline() }
                TaskModelListRow(row: row, l10n: l10n)
            }
        }
    }
}

private struct TaskModelListRow: View {
    let row: TaskPerformanceModelRow
    let l10n: Localizer

    var body: some View {
        let low = TaskPerformanceText.isLowSample(row.stats)
        VStack(alignment: .leading, spacing: 3) {
            HStack(spacing: 7) {
                TaskModelNameCell(row: row, low: low, l10n: l10n,
                                  font: .system(size: 12.5, weight: low ? .medium : .semibold))
                    .frame(maxWidth: .infinity, alignment: .leading)
                HStack(spacing: 8) {
                    let median = TaskPerformanceText.median(row.stats)
                    Text(median)
                        .font(.system(size: 12.5, weight: low ? .medium : .semibold).monospacedDigit())
                        .foregroundStyle(median == TaskPerformanceText.absent ? AnyShapeStyle(.tertiary)
                            : (low ? AnyShapeStyle(.historySecondary) : AnyShapeStyle(.primary)))
                    TaskIQRStrip(stats: row.stats, source: row.sources.first, width: 36)
                }
                .fixedSize()
            }
            line([
                pair(l10n(TaskPerformanceMetric.success.titleKey), TaskPerformanceText.shown(.success, row.stats, l10n)),
                pair(l10n(TaskPerformanceMetric.stall.titleKey), TaskPerformanceText.shown(.stall, row.stats, l10n)),
                pair(l10n(TaskPerformanceMetric.fixFail.titleKey), TaskPerformanceText.shown(.fixFail, row.stats, l10n)),
            ])
            line([
                TaskPerformanceText.counts(row.stats, l10n),
                pair(l10n(TaskPerformanceMetric.attempts.shortKey), TaskPerformanceText.shown(.attempts, row.stats, l10n)),
                pair(l10n(TaskPerformanceMetric.firstPass.shortKey), TaskPerformanceText.shown(.firstPass, row.stats, l10n)),
            ])
        }
        .padding(.vertical, 9)
        .padding(.horizontal, 2)
        .frame(maxWidth: .infinity, alignment: .leading)
        .contentShape(Rectangle())
        .contextMenu {
            Button(l10n("mac.history.copy")) { TaskModelCopy.copy([row], l10n) }
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(TaskModelTable.accessibleLabel(row, l10n))
        .accessibilityValue(TaskModelTable.accessibleValue(row, columns: TaskModelColumn.copied, l10n))
    }

    private func pair(_ label: String, _ value: String) -> String {
        "\(label) \(value)"
    }

    /// Items joined by ` · `, wrapping only between items.
    private func line(_ items: [String]) -> some View {
        TaskFlowLayout(spacing: 0, lineSpacing: 1) {
            ForEach(Array(items.enumerated()), id: \.offset) { index, item in
                Text(index < items.count - 1 ? "\(item) ·\u{00A0}" : item)
                    .font(TaskPerformanceFont.listLine)
                    .foregroundStyle(.historySecondary)
            }
        }
    }
}
