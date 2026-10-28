import AgentSpeedCore
import SwiftUI

/// Narrow: the side headers as two equal cards side by side, then a three-column table under the two sources' names
/// (DESIGN 12, *Widths*).
struct TaskCompactComparison: View {
    let pair: TaskPerformancePair
    let comparison: TaskPerformanceComparison
    let l10n: Localizer
    var reduceMotion = false

    var body: some View {
        let first = comparison.stats(for: pair.first)
        let second = comparison.stats(for: pair.second)
        VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .top, spacing: Spacing.xs) {
                TaskSummaryCard(source: pair.first, stats: first, l10n: l10n, reduceMotion: reduceMotion)
                TaskSummaryCard(source: pair.second, stats: second, l10n: l10n, reduceMotion: reduceMotion)
            }
            .fixedSize(horizontal: false, vertical: true)
            header
                .padding(.top, Spacing.s)
            ForEach(TaskPerformanceMetric.allCases, id: \.self) { metric in
                Hairline()
                TaskCompactRow(metric: metric, pair: pair, first: first, second: second, l10n: l10n)
            }
            Hairline()
        }
    }

    private var header: some View {
        HStack(alignment: .bottom, spacing: Spacing.xs) {
            Spacer(minLength: 0)
            TaskCompactSourceName(source: pair.first, l10n: l10n)
                .frame(width: TaskPerformanceStyle.compactValueColumn, alignment: .trailing)
            TaskCompactSourceName(source: pair.second, l10n: l10n)
                .frame(width: TaskPerformanceStyle.compactValueColumn, alignment: .trailing)
        }
        .padding(.bottom, 6)
        .accessibilityHidden(true)
    }
}

/// A column title of the narrow table: the dot inline before the name, which may wrap onto a second line.
private struct TaskCompactSourceName: View {
    let source: SourceKind
    let l10n: Localizer

    @Environment(\.colorScheme) private var colorScheme

    var body: some View {
        (Text(Image(systemName: "circle.fill"))
            .font(.system(size: 5.5))
            .foregroundStyle(source.tint(dark: colorScheme == .dark))
            .baselineOffset(1.5)
            + Text(verbatim: "\u{2009}")
            + Text(l10n(source.titleKey)))
            .font(TaskPerformanceFont.compactHeader)
            .foregroundStyle(.historySecondary)
            .multilineTextAlignment(.trailing)
            .lineLimit(2)
            .fixedSize(horizontal: false, vertical: true)
    }
}

/// One metric of the narrow table: the name (wrapping, `history.tasks.lowerBetter` under it) and both values
/// right-aligned on its first baseline, the better one semibold.
private struct TaskCompactRow: View {
    let metric: TaskPerformanceMetric
    let pair: TaskPerformancePair
    let first: TaskPerformanceStats?
    let second: TaskPerformanceStats?
    let l10n: Localizer

    @Environment(\.colorSchemeContrast) private var contrast

    var body: some View {
        let better = TaskPerformanceText.better(metric, first, second, l10n)
        HStack(alignment: .firstTextBaseline, spacing: Spacing.xs) {
            VStack(alignment: .leading, spacing: 1) {
                Text(l10n(metric.titleKey))
                    .font(.system(size: 12.5))
                    .foregroundStyle(TaskPerformancePalette.metricName(increasedContrast: contrast == .increased))
                    .fixedSize(horizontal: false, vertical: true)
                if metric.lowerIsBetter {
                    Text(l10n("history.tasks.lowerBetter"))
                        .font(TaskPerformanceFont.lowerBetter)
                        .foregroundStyle(.tertiary)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .help(l10n(metric.hintKey))
            value(.first, first, better)
            value(.second, second, better)
        }
        .padding(.vertical, 7)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(TaskPerformanceText.compareWords(metric, pair: pair, first: first, second: second, l10n))
    }

    private func value(_ side: TaskPerformanceSide, _ stats: TaskPerformanceStats?, _ better: TaskPerformanceSide?) -> some View {
        let text = TaskPerformanceText.value(metric, stats, l10n)
        return TaskValueText(text: text ?? TaskPerformanceText.absent,
                             emphasis: .of(side, better: better, hasValue: text != nil),
                             font: TaskPerformanceFont.compactValue,
                             tooltip: TaskPerformanceText.tooltip(metric, stats, l10n))
            .frame(width: TaskPerformanceStyle.compactValueColumn, alignment: .trailing)
    }
}

/// A narrow side header: a card (radius 10, the label color at 5 %, padding 10) with the dot and name, the median
/// with its caption, the counts wrapping and the distribution at the card's full width.
struct TaskSummaryCard: View {
    let source: SourceKind
    let stats: TaskPerformanceStats?
    let l10n: Localizer
    var reduceMotion = false

    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.colorSchemeContrast) private var contrast

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            TaskSourceLabel(source: source, l10n: l10n, font: TaskPerformanceFont.compactName)
                .accessibilityHidden(true)
            if let stats, stats.tasks > 0 {
                HStack(alignment: .firstTextBaseline, spacing: 5) {
                    let median = TaskPerformanceText.median(stats)
                    Text(median)
                        .font(TaskPerformanceFont.medianCompact)
                        .foregroundStyle(median == TaskPerformanceText.absent ? AnyShapeStyle(.tertiary) : AnyShapeStyle(.primary))
                        .contentTransition(.opacity)
                        .animation(TaskPerformanceMotion.fade, value: median)
                    Text(l10n("history.tasks.metric.median.short"))
                        .font(TaskPerformanceFont.caption)
                        .foregroundStyle(.historySecondary)
                }
                .lineLimit(1)
                .padding(.top, 5)
                .accessibilityHidden(true)
                TaskFlowLayout(spacing: 0, lineSpacing: 1) {
                    ForEach(Array(TaskFlowText.parts(TaskPerformanceText.counts(stats, l10n)).enumerated()), id: \.offset) { _, part in
                        Text(part)
                            .font(TaskPerformanceFont.compactCounts)
                            .foregroundStyle(.historySecondary)
                            .accessibilityHidden(true)
                    }
                    if TaskPerformanceText.isLowSample(stats) {
                        TaskLowSampleChip(stats: stats, l10n: l10n)
                            .padding(.leading, 5)
                    }
                }
                .padding(.top, 3)
                if stats.scoredTasks > 0 {
                    Spacer(minLength: 0)
                    TaskScoreDistribution(stats: stats, source: source, l10n: l10n, reduceMotion: reduceMotion)
                        .padding(.top, 8)
                }
            } else {
                Text(l10n("history.tasks.side.empty"))
                    .font(TaskPerformanceFont.compactCounts)
                    .foregroundStyle(.historySecondary)
                    .fixedSize(horizontal: false, vertical: true)
                    .padding(.top, 8)
                    .accessibilityHidden(true)
            }
        }
        .padding(10)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .background(
            RoundedRectangle(cornerRadius: TaskPerformanceStyle.compactCardRadius, style: .continuous)
                .fill(TaskPerformancePalette.compactCard(dark: colorScheme == .dark, increasedContrast: contrast == .increased))
        )
        .accessibilityElement(children: .contain)
        .accessibilityLabel(TaskPerformanceText.sideWords(source, stats, l10n))
    }
}
