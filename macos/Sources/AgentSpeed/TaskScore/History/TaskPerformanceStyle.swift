import AgentSpeedCore
import SwiftUI

/// Type of the 任務表現 section (DESIGN 12, *Usage History: task performance*), in the history window's language.
enum TaskPerformanceFont {
    static let title = Font.system(size: 15, weight: .semibold)
    static let vs = Font.system(size: 11)
    static let onlyOne = Font.system(size: 11.5)
    static let sourceName = Font.system(size: 13, weight: .semibold)
    static let counts = Font.system(size: 11.5).monospacedDigit()
    static let median = Font.system(size: 34, weight: .semibold).monospacedDigit()
    static let medianCompact = Font.system(size: 24, weight: .semibold).monospacedDigit()
    static let caption = Font.system(size: 11)
    static let axis = Font.system(size: 9).monospacedDigit()
    static let metric = Font.system(size: 12, weight: .medium)
    static let lowerBetter = Font.system(size: 10)
    static let value = Font.system(size: 13).monospacedDigit()
    static let compactValue = Font.system(size: 12.5).monospacedDigit()
    static let compactHeader = Font.system(size: 10.5, weight: .semibold)
    static let compactName = Font.system(size: 12.5, weight: .semibold)
    static let compactCounts = Font.system(size: 11).monospacedDigit()
    static let modelID = Font.system(size: 12.5, weight: .medium)
    static let listLine = Font.system(size: 11).monospacedDigit()
    static let chip = Font.system(size: 10, weight: .medium)
    static let footnote = Font.system(size: 11)
}

enum TaskPerformanceStyle {
    /// The card's padding, as the effort section's.
    static let horizontalPadding = Spacing.m
    static let topPadding: CGFloat = 14
    static let bottomPadding = Spacing.m
    /// The butterfly: a value column at each outer edge, 10 pt between columns, rows of at least 36 pt.
    static let valueColumn: CGFloat = 64
    static let gap: CGFloat = 10
    static let rowHeight: CGFloat = 36
    static let trackHeight: CGFloat = 6
    static let distributionHeight: CGFloat = 28
    /// Narrow: the two value columns of the comparison table and the summary cards.
    static let compactValueColumn: CGFloat = 72
    static let compactCardRadius: CGFloat = 10
    /// One source: the label column before the bars.
    static let singleLabelColumn: CGFloat = 180
    static let singleLabelColumnNarrow: CGFloat = 136
    static let stripWidth: CGFloat = 48
}

/// Colors on the history card, both appearances; Increase Contrast strengthens tracks and fills.
enum TaskPerformancePalette {
    static func track(dark: Bool, increasedContrast: Bool) -> Color {
        TaskScorePalette.track(dark: dark, increasedContrast: increasedContrast)
    }

    /// A comparison track's fill: the source color (dark at 85 %).
    static func fill(_ source: SourceKind, dark: Bool, increasedContrast: Bool) -> Color {
        source.tint(dark: dark).opacity(dark && !increasedContrast ? 0.85 : 1)
    }

    /// A distribution bar: the source color at 70 %, the median's bin at 100 %.
    static func bar(_ source: SourceKind, median: Bool, dark: Bool, increasedContrast: Bool) -> Color {
        source.tint(dark: dark).opacity(median ? 1 : (increasedContrast ? 0.82 : 0.7))
    }

    /// The metric names of the centre column: the label color at 85 %.
    static func metricName(increasedContrast: Bool) -> Color {
        Color(nsColor: .labelColor).opacity(increasedContrast ? 1 : 0.85)
    }

    /// The narrow summary cards: the label color at 5 %.
    static func compactCard(dark: Bool, increasedContrast: Bool) -> Color {
        Color(nsColor: .labelColor).opacity(increasedContrast ? 0.1 : (dark ? 0.06 : 0.05))
    }

    static func tick(increasedContrast: Bool) -> Color {
        Color(nsColor: .labelColor).opacity(increasedContrast ? 1 : 0.8)
    }
}

enum TaskPerformanceMotion {
    /// Values and states crossfade.
    static let fade = Animation.easeOut(duration: 0.2)
    /// Bars spring to their new lengths, settling within 0.35 s (DESIGN 6).
    static let bars = Animation.spring(response: 0.3, dampingFraction: 0.88)

    static func bars(reduceMotion: Bool) -> Animation { reduceMotion ? fade : bars }
}

/// How a compared value is drawn (DESIGN 12, *Better*).
enum TaskValueEmphasis: Equatable {
    /// No side is marked: regular, the label color.
    case plain
    /// The better value: semibold, the label color.
    case better
    /// The other value: regular, secondary.
    case other
    /// `–`: tertiary.
    case absent

    static func of(_ side: TaskPerformanceSide, better: TaskPerformanceSide?, hasValue: Bool) -> TaskValueEmphasis {
        guard hasValue else { return .absent }
        guard let better else { return .plain }
        return better == side ? .better : .other
    }

    var weight: Font.Weight { self == .better ? .semibold : .regular }

    var style: AnyShapeStyle {
        switch self {
        case .plain, .better: AnyShapeStyle(.primary)
        case .other: AnyShapeStyle(.historySecondary)
        case .absent: AnyShapeStyle(.tertiary)
        }
    }
}
