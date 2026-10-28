import AgentSpeedCore
import SwiftUI

/// One source (DESIGN 12, *One source*): its side header full width, then one bar per metric — the name in a label
/// column, the bar, the value.
struct TaskSingleSource: View {
    let source: SourceKind
    let stats: TaskPerformanceStats?
    let width: TaskPerformanceWidth
    let l10n: Localizer
    var reduceMotion = false

    var body: some View {
        let labelColumn = width == .narrow ? TaskPerformanceStyle.singleLabelColumnNarrow : TaskPerformanceStyle.singleLabelColumn
        VStack(alignment: .leading, spacing: 0) {
            TaskSideHeader(source: source, stats: stats, side: .second, distributionWidth: width.distribution,
                           l10n: l10n, reduceMotion: reduceMotion)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.bottom, 14)
            Hairline()
                .padding(.bottom, 6)
            ForEach(TaskPerformanceMetric.allCases, id: \.self) { metric in
                TaskSingleRow(metric: metric, source: source, stats: stats, labelColumn: labelColumn, l10n: l10n,
                              reduceMotion: reduceMotion)
            }
        }
    }
}

private struct TaskSingleRow: View {
    let metric: TaskPerformanceMetric
    let source: SourceKind
    let stats: TaskPerformanceStats?
    let labelColumn: CGFloat
    let l10n: Localizer
    var reduceMotion = false

    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.colorSchemeContrast) private var contrast

    var body: some View {
        let value = stats.flatMap { $0.tasks > 0 ? metric.value($0) : nil }
        let text = TaskPerformanceText.value(metric, stats, l10n)
        HStack(spacing: TaskPerformanceStyle.gap) {
            TaskMetricName(metric: metric, l10n: l10n, alignment: .leading)
                .frame(width: labelColumn, alignment: .leading)
            TaskMetricTrack(fraction: TaskPerformanceText.fraction(value, scale: TaskPerformanceText.scale(metric, value)),
                            color: TaskPerformancePalette.fill(source, dark: colorScheme == .dark,
                                                               increasedContrast: contrast == .increased),
                            reduceMotion: reduceMotion)
                .frame(minWidth: 0, maxWidth: .infinity)
            TaskValueText(text: text ?? TaskPerformanceText.absent, emphasis: text == nil ? .absent : .plain,
                          tooltip: TaskPerformanceText.tooltip(metric, stats, l10n))
                .frame(width: TaskPerformanceStyle.valueColumn, alignment: .leading)
        }
        .frame(minHeight: TaskPerformanceStyle.rowHeight)
        // A minimum-height frame would let a stack squeeze the row back to 36 pt under a two-line name.
        .fixedSize(horizontal: false, vertical: true)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(TaskPerformanceText.singleWords(metric, stats, l10n))
    }
}
