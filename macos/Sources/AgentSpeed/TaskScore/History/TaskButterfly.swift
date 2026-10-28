import AgentSpeedCore
import AppKit
import SwiftUI

/// Wide and medium: the two side headers mirrored about the centre column, a hairline, then one butterfly row per
/// metric (DESIGN 12, *Side headers*, *Comparison rows*).
struct TaskButterfly: View {
    let pair: TaskPerformancePair
    let comparison: TaskPerformanceComparison
    let width: TaskPerformanceWidth
    let l10n: Localizer
    var reduceMotion = false

    var body: some View {
        let first = comparison.stats(for: pair.first)
        let second = comparison.stats(for: pair.second)
        VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .top, spacing: TaskPerformanceStyle.gap) {
                TaskSideHeader(source: pair.first, stats: first, side: .first, distributionWidth: width.distribution,
                               l10n: l10n, reduceMotion: reduceMotion)
                    .frame(minWidth: 0, maxWidth: .infinity, alignment: .trailing)
                Color.clear
                    .frame(width: width.centre, height: 1)
                    .accessibilityHidden(true)
                TaskSideHeader(source: pair.second, stats: second, side: .second, distributionWidth: width.distribution,
                               l10n: l10n, reduceMotion: reduceMotion)
                    .frame(minWidth: 0, maxWidth: .infinity, alignment: .leading)
            }
            .padding(.bottom, 14)
            Hairline()
                .padding(.bottom, 6)
            ForEach(TaskPerformanceMetric.allCases, id: \.self) { metric in
                TaskButterflyRow(metric: metric, pair: pair, first: first, second: second, centre: width.centre,
                                 l10n: l10n, reduceMotion: reduceMotion)
            }
        }
    }
}

/// One metric: each side's value at its outer edge, its track filled from the centre outward, the metric's name in
/// the centre column. One accessibility element.
struct TaskButterflyRow: View {
    let metric: TaskPerformanceMetric
    let pair: TaskPerformancePair
    let first: TaskPerformanceStats?
    let second: TaskPerformanceStats?
    let centre: CGFloat
    let l10n: Localizer
    var reduceMotion = false

    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.colorSchemeContrast) private var contrast

    var body: some View {
        let dark = colorScheme == .dark
        let increased = contrast == .increased
        let better = TaskPerformanceText.better(metric, first, second, l10n)
        let a = first.flatMap { $0.tasks > 0 ? metric.value($0) : nil }
        let b = second.flatMap { $0.tasks > 0 ? metric.value($0) : nil }
        let scale = TaskPerformanceText.scale(metric, a, b)
        HStack(spacing: TaskPerformanceStyle.gap) {
            value(.first, stats: first, better: better)
                .frame(width: TaskPerformanceStyle.valueColumn, alignment: .trailing)
            TaskMetricTrack(fraction: TaskPerformanceText.fraction(a, scale: scale),
                            color: TaskPerformancePalette.fill(pair.first, dark: dark, increasedContrast: increased),
                            fromTrailing: true, reduceMotion: reduceMotion)
                .frame(minWidth: 0, maxWidth: .infinity)
            TaskMetricName(metric: metric, l10n: l10n, alignment: .center, centre: centre)
                .frame(width: centre)
            TaskMetricTrack(fraction: TaskPerformanceText.fraction(b, scale: scale),
                            color: TaskPerformancePalette.fill(pair.second, dark: dark, increasedContrast: increased),
                            reduceMotion: reduceMotion)
                .frame(minWidth: 0, maxWidth: .infinity)
            value(.second, stats: second, better: better)
                .frame(width: TaskPerformanceStyle.valueColumn, alignment: .leading)
        }
        .frame(minHeight: TaskPerformanceStyle.rowHeight)
        // A minimum-height frame would let a stack squeeze the row back to 36 pt under a two-line name.
        .fixedSize(horizontal: false, vertical: true)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(TaskPerformanceText.compareWords(metric, pair: pair, first: first, second: second, l10n))
    }

    private func value(_ side: TaskPerformanceSide, stats: TaskPerformanceStats?, better: TaskPerformanceSide?) -> some View {
        let text = TaskPerformanceText.value(metric, stats, l10n)
        return TaskValueText(text: text ?? TaskPerformanceText.absent,
                             emphasis: .of(side, better: better, hasValue: text != nil),
                             tooltip: TaskPerformanceText.tooltip(metric, stats, l10n))
    }
}

/// A metric's full name (12 pt medium, the label color at 85 %, up to two lines) with `history.tasks.lowerBetter`
/// under the lower-is-better ones; the metric's hint is its tooltip.
struct TaskMetricName: View {
    let metric: TaskPerformanceMetric
    let l10n: Localizer
    var alignment: HorizontalAlignment = .center
    /// The centre column's width, within which the name wraps evenly.
    var centre: CGFloat = 140

    private static let nameFont = NSFont.systemFont(ofSize: 12, weight: .medium)

    @Environment(\.colorSchemeContrast) private var contrast

    var body: some View {
        let textAlignment: TextAlignment = alignment == .center ? .center : .leading
        VStack(alignment: alignment, spacing: 1) {
            name(textAlignment)
            if metric.lowerIsBetter {
                Text(l10n("history.tasks.lowerBetter"))
                    .font(TaskPerformanceFont.lowerBetter)
                    .foregroundStyle(.tertiary)
                    .lineLimit(1)
                    .minimumScaleFactor(0.85)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .padding(.vertical, 3)
        .frame(maxWidth: .infinity, alignment: Alignment(horizontal: alignment, vertical: .center))
        .contentShape(Rectangle())
        .help(l10n(metric.hintKey))
    }

    @ViewBuilder
    private func name(_ textAlignment: TextAlignment) -> some View {
        let text = Text(l10n(metric.titleKey))
            .font(TaskPerformanceFont.metric)
            .foregroundStyle(TaskPerformancePalette.metricName(increasedContrast: contrast == .increased))
            .multilineTextAlignment(textAlignment)
            .fixedSize(horizontal: false, vertical: true)
        if alignment == .center {
            text.frame(width: TaskTextBalance.width(l10n(metric.titleKey), font: Self.nameFont, within: centre))
        } else {
            text.lineLimit(2)
        }
    }
}
