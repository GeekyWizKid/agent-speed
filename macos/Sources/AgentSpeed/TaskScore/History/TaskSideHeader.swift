import AgentSpeedCore
import SwiftUI

/// A source's header over the comparison (wide and medium) and in the single-source layout: the dot and name, the
/// counts with the low-sample chip, the median with its distribution. The first side mirrors toward the centre
/// column: right-aligned, its dot after the name, its distribution before the median.
struct TaskSideHeader: View {
    let source: SourceKind
    let stats: TaskPerformanceStats?
    /// `.first` is the mirrored left side.
    let side: TaskPerformanceSide
    let distributionWidth: CGFloat
    let l10n: Localizer
    var reduceMotion = false

    private var mirrored: Bool { side == .first }

    var body: some View {
        VStack(alignment: mirrored ? .trailing : .leading, spacing: 3) {
            TaskSourceLabel(source: source, l10n: l10n, dotTrailing: mirrored)
                .accessibilityHidden(true)
            if let stats, stats.tasks > 0 {
                countsLine(stats)
                HStack(alignment: .bottom, spacing: Spacing.s) {
                    if mirrored { distribution(stats) }
                    median(stats)
                    if !mirrored { distribution(stats) }
                }
                .padding(.top, 6)
            } else {
                Text(l10n("history.tasks.side.empty"))
                    .font(TaskPerformanceFont.counts)
                    .foregroundStyle(.historySecondary)
                    .accessibilityHidden(true)
            }
        }
        .multilineTextAlignment(mirrored ? .trailing : .leading)
        .accessibilityElement(children: .contain)
        .accessibilityLabel(TaskPerformanceText.sideWords(source, stats, l10n))
    }

    private func countsLine(_ stats: TaskPerformanceStats) -> some View {
        HStack(alignment: .center, spacing: 6) {
            Text(TaskPerformanceText.counts(stats, l10n))
                .font(TaskPerformanceFont.counts)
                .foregroundStyle(.historySecondary)
                .lineLimit(1)
                .minimumScaleFactor(0.85)
                .fixedSize(horizontal: false, vertical: true)
                .contentTransition(.opacity)
                .animation(TaskPerformanceMotion.fade, value: stats.tasks)
                .accessibilityHidden(true)
            if TaskPerformanceText.isLowSample(stats) {
                TaskLowSampleChip(stats: stats, l10n: l10n)
            }
        }
        // The chip never makes one side's line taller than the other's.
        .frame(height: 16)
    }

    private func median(_ stats: TaskPerformanceStats) -> some View {
        let value = TaskPerformanceText.median(stats)
        return VStack(alignment: mirrored ? .trailing : .leading, spacing: 1) {
            Text(value)
                .font(TaskPerformanceFont.median)
                .tracking(-0.4)
                .foregroundStyle(value == TaskPerformanceText.absent ? AnyShapeStyle(.tertiary) : AnyShapeStyle(.primary))
                .lineLimit(1)
                .fixedSize()
                .contentTransition(.opacity)
                .animation(TaskPerformanceMotion.fade, value: value)
                // The digits have no descenders: tuck the caption up under their baseline.
                .padding(.bottom, -3)
            Text(l10n("history.tasks.metric.median.short"))
                .font(TaskPerformanceFont.caption)
                .foregroundStyle(.historySecondary)
                .lineLimit(1)
                .fixedSize()
        }
        .accessibilityHidden(true)
    }

    @ViewBuilder
    private func distribution(_ stats: TaskPerformanceStats) -> some View {
        if stats.scoredTasks > 0 {
            TaskScoreDistribution(stats: stats, source: source, width: distributionWidth, l10n: l10n,
                                  reduceMotion: reduceMotion)
        }
    }
}
