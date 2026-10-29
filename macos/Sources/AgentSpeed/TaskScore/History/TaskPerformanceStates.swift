import SwiftUI

/// Re-analysing with no record yet: the section's shape as static placeholders in the track color — the two side
/// headers and five rows — with `history.tasks.analyzing` under them. Nothing moves (DESIGN 12, *States*).
struct TaskPerformancePlaceholder: View {
    let width: TaskPerformanceWidth
    let l10n: Localizer

    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.colorSchemeContrast) private var contrast

    var body: some View {
        let fill = TaskPerformancePalette.track(dark: colorScheme == .dark, increasedContrast: contrast == .increased)
        VStack(spacing: 0) {
            Group {
                if width == .narrow { narrow(fill) } else { butterfly(fill) }
            }
            .accessibilityHidden(true)
            Text(l10n("history.tasks.analyzing"))
                .font(TaskPerformanceFont.onlyOne)
                .foregroundStyle(.historySecondary)
                .multilineTextAlignment(.center)
                .frame(maxWidth: .infinity)
                .padding(.top, 14)
        }
        .accessibilityElement(children: .combine)
    }

    private func butterfly(_ fill: Color) -> some View {
        VStack(spacing: 0) {
            HStack(alignment: .top, spacing: TaskPerformanceStyle.gap) {
                side(fill, mirrored: true)
                    .frame(minWidth: 0, maxWidth: .infinity, alignment: .trailing)
                Color.clear.frame(width: width.centre, height: 1)
                side(fill, mirrored: false)
                    .frame(minWidth: 0, maxWidth: .infinity, alignment: .leading)
            }
            .padding(.bottom, 14)
            Rectangle().fill(fill).frame(height: 1)
                .padding(.bottom, 6)
            ForEach(0..<5, id: \.self) { index in
                HStack(spacing: TaskPerformanceStyle.gap) {
                    bar(fill, width: 30).frame(width: TaskPerformanceStyle.valueColumn, alignment: .trailing)
                    Capsule().fill(fill).frame(height: TaskPerformanceStyle.trackHeight)
                    bar(fill, width: [56, 48, 72, 84, 96][index]).frame(width: width.centre)
                    Capsule().fill(fill).frame(height: TaskPerformanceStyle.trackHeight)
                    bar(fill, width: 30).frame(width: TaskPerformanceStyle.valueColumn, alignment: .leading)
                }
                .frame(height: TaskPerformanceStyle.rowHeight)
            }
        }
    }

    private func side(_ fill: Color, mirrored: Bool) -> some View {
        VStack(alignment: mirrored ? .trailing : .leading, spacing: 7) {
            bar(fill, width: 74, height: 10)
            bar(fill, width: 132, height: 8)
            HStack(alignment: .bottom, spacing: Spacing.s) {
                if mirrored { bar(fill, width: width.distribution, height: 28) }
                bar(fill, width: 40, height: 30)
                if !mirrored { bar(fill, width: width.distribution, height: 28) }
            }
            .padding(.top, 4)
        }
    }

    private func narrow(_ fill: Color) -> some View {
        VStack(spacing: 0) {
            HStack(spacing: Spacing.xs) {
                ForEach(0..<2, id: \.self) { _ in
                    VStack(alignment: .leading, spacing: 7) {
                        bar(fill, width: 64, height: 10)
                        bar(fill, width: 36, height: 20)
                        bar(fill, width: 96, height: 8)
                        bar(fill, width: nil, height: 22).padding(.top, 4)
                    }
                    .padding(10)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(RoundedRectangle(cornerRadius: TaskPerformanceStyle.compactCardRadius, style: .continuous)
                        .strokeBorder(fill, lineWidth: 1))
                }
            }
            VStack(spacing: 0) {
                ForEach(0..<5, id: \.self) { index in
                    HStack(spacing: Spacing.xs) {
                        bar(fill, width: [52, 44, 64, 80, 96][index])
                        Spacer(minLength: 0)
                        bar(fill, width: 30).frame(width: TaskPerformanceStyle.compactValueColumn, alignment: .trailing)
                        bar(fill, width: 30).frame(width: TaskPerformanceStyle.compactValueColumn, alignment: .trailing)
                    }
                    .frame(height: 32)
                    .overlay(alignment: .top) { Rectangle().fill(fill).frame(height: 1) }
                }
            }
            .padding(.top, Spacing.s)
        }
    }

    private func bar(_ fill: Color, width: CGFloat?, height: CGFloat = 8) -> some View {
        RoundedRectangle(cornerRadius: min(4, height / 2), style: .continuous)
            .fill(fill)
            .frame(width: width, height: height)
            .frame(maxWidth: width == nil ? .infinity : nil)
    }
}

/// No record in the range for any scored source: the muted gauge, `history.tasks.empty` and its hint. The section
/// stays, so the feature can be found.
struct TaskPerformanceEmptyState: View {
    let l10n: Localizer

    var body: some View {
        VStack(spacing: 6) {
            RestingGauge(lineWidth: 1.75)
                .frame(width: 24, height: 24)
                .padding(.bottom, 4)
            Text(l10n("history.tasks.empty"))
                .font(.system(size: 13, weight: .semibold))
                .foregroundStyle(.historySecondary)
            Text(l10n("history.tasks.empty.hint"))
                .font(.system(size: 11.5))
                .foregroundStyle(.historySecondary)
                .lineSpacing(1.5)
                .frame(maxWidth: 360)
                .fixedSize(horizontal: false, vertical: true)
        }
        .multilineTextAlignment(.center)
        .frame(maxWidth: .infinity)
        .padding(.top, 10)
        .padding(.bottom, 6)
        .accessibilityElement(children: .combine)
    }
}
