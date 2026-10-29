import AgentSpeedCore
import SwiftUI

/// A source's 7 pt dot.
struct TaskSourceDot: View {
    let source: SourceKind
    var size: CGFloat = 7

    @Environment(\.colorScheme) private var colorScheme

    var body: some View {
        Circle()
            .fill(source.tint(dark: colorScheme == .dark))
            .frame(width: size, height: size)
            .accessibilityHidden(true)
    }
}

/// The dot and the source's name; `dotTrailing` mirrors it for the left side header, the dot toward the centre.
struct TaskSourceLabel: View {
    let source: SourceKind
    let l10n: Localizer
    var font = TaskPerformanceFont.sourceName
    var dotTrailing = false

    var body: some View {
        HStack(spacing: 7) {
            if !dotTrailing { TaskSourceDot(source: source) }
            Text(l10n(source.titleKey))
                .font(font)
                .lineLimit(1)
                .truncationMode(.tail)
            if dotTrailing { TaskSourceDot(source: source) }
        }
    }
}

/// `樣本少` in a neutral capsule, section 9's reason chip; its tooltip says why.
struct TaskLowSampleChip: View {
    let stats: TaskPerformanceStats
    let l10n: Localizer

    @Environment(\.colorSchemeContrast) private var contrast

    var body: some View {
        let increased = contrast == .increased
        Text(l10n("history.tasks.sample.low"))
            .font(TaskPerformanceFont.chip)
            .foregroundStyle(.historySecondary)
            .lineLimit(1)
            .fixedSize()
            .padding(.horizontal, 5)
            .padding(.vertical, 1)
            .background(Capsule().fill(Color.primary.opacity(increased ? 0.14 : 0.09)))
            .overlay(Capsule().strokeBorder(Color.primary.opacity(increased ? 0.5 : 0.16), lineWidth: increased ? 1 : 0.5))
            .help(TaskPerformanceText.sampleHint(stats, l10n))
            .accessibilityElement(children: .ignore)
            .accessibilityLabel(l10n("history.tasks.sample.low"))
            .accessibilityHint(TaskPerformanceText.sampleHint(stats, l10n))
            .accessibilityAddTraits(.isStaticText)
    }
}

/// The dots of every source with records of a model, overlapping 4 pt apart, the first on top; each is cut out of
/// the ones under it by a 1 pt ring, so the row's own background shows through whatever its tint.
struct TaskModelDots: View {
    let sources: [SourceKind]

    @Environment(\.colorScheme) private var colorScheme

    static let diameter: CGFloat = 7
    static let step: CGFloat = 4
    static let ring: CGFloat = 1

    static func width(_ count: Int) -> CGFloat {
        diameter + step * CGFloat(max(count - 1, 0))
    }

    var body: some View {
        let dark = colorScheme == .dark
        let colors = sources.map { $0.tint(dark: dark) }
        Canvas { context, size in
            let y = (size.height - Self.diameter) / 2
            for index in colors.indices.reversed() {
                let rect = CGRect(x: Self.step * CGFloat(index), y: y, width: Self.diameter, height: Self.diameter)
                var cut = context
                cut.blendMode = .clear
                cut.fill(Path(ellipseIn: rect.insetBy(dx: -Self.ring, dy: -Self.ring)), with: .color(.black))
                context.fill(Path(ellipseIn: rect), with: .color(colors[index]))
            }
        }
        .frame(width: Self.width(sources.count) + Self.ring * 2, height: Self.diameter + Self.ring * 2)
        .padding(.horizontal, -Self.ring)
        .accessibilityHidden(true)
    }
}

/// The median cell's 48 × 4 pt strip: the track, the middle half in the first source's color at 45 % and a 1.5 pt
/// median tick in the label color.
struct TaskIQRStrip: View {
    let stats: TaskPerformanceStats
    let source: SourceKind?
    var width: CGFloat = TaskPerformanceStyle.stripWidth

    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.colorSchemeContrast) private var contrast

    var body: some View {
        let dark = colorScheme == .dark
        let increased = contrast == .increased
        let track = TaskPerformancePalette.track(dark: dark, increasedContrast: increased)
        let band = (source?.tint(dark: dark) ?? .secondary).opacity(increased ? 0.7 : 0.45)
        let tick = TaskPerformancePalette.tick(increasedContrast: increased)
        Canvas { context, size in
            let bar = CGRect(x: 0, y: (size.height - 4) / 2, width: size.width, height: 4)
            context.fill(Path(roundedRect: bar, cornerRadius: 2), with: .color(track))
            guard let q1 = stats.lowerQuartile, let q3 = stats.upperQuartile, let median = stats.medianScore else { return }
            let x = { (score: Double) in CGFloat(score.clamped(to: 0...100) / 100) * size.width }
            let lower = x(q1)
            let upper = max(x(q3), lower + 1)
            context.fill(Path(roundedRect: CGRect(x: lower, y: bar.minY, width: upper - lower, height: 4), cornerRadius: 2),
                         with: .color(band))
            let mid = min(max(x(median), 0.75), size.width - 0.75)
            context.fill(Path(roundedRect: CGRect(x: mid - 0.75, y: 0, width: 1.5, height: size.height), cornerRadius: 0.75),
                         with: .color(tick))
        }
        .frame(width: width, height: 8)
        .accessibilityHidden(true)
    }
}

/// A comparison bar: a 6 pt track filled from its inner edge. The fill springs to a new length; with Reduce Motion it
/// crossfades instead.
struct TaskMetricTrack: View {
    let fraction: Double
    let color: Color
    /// The left side fills from its trailing edge, toward the centre column.
    var fromTrailing = false
    var reduceMotion = false

    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.colorSchemeContrast) private var contrast

    var body: some View {
        let track = TaskPerformancePalette.track(dark: colorScheme == .dark, increasedContrast: contrast == .increased)
        ZStack {
            Capsule().fill(track)
            if reduceMotion {
                TrackFill(fraction: fraction, fromTrailing: fromTrailing)
                    .fill(color)
                    .id(fraction)
                    .transition(.opacity)
            } else {
                TrackFill(fraction: fraction, fromTrailing: fromTrailing)
                    .fill(color)
            }
        }
        .frame(height: TaskPerformanceStyle.trackHeight)
        .animation(TaskPerformanceMotion.bars(reduceMotion: reduceMotion), value: fraction)
        .accessibilityHidden(true)
    }
}

/// The filled part of a track; its length animates.
private struct TrackFill: Shape {
    var fraction: Double
    var fromTrailing: Bool

    var animatableData: Double {
        get { fraction }
        set { fraction = newValue }
    }

    func path(in rect: CGRect) -> Path {
        let width = rect.width * CGFloat(fraction.clamped(to: 0...1))
        guard width > 0.01 else { return Path() }
        let x = fromTrailing ? rect.maxX - width : rect.minX
        let radius = min(rect.height / 2, width / 2)
        return Path(roundedRect: CGRect(x: x, y: rect.minY, width: width, height: rect.height), cornerRadius: radius)
    }
}

/// The score distribution: 10 bars, 2 pt apart, top corners 1.5 pt, the median's bin at full strength, a 1 pt median
/// tick, `0` and `100` under the ends. Both axes run from 0 on the left to 100 on the right on either side.
struct TaskScoreDistribution: View {
    let stats: TaskPerformanceStats
    let source: SourceKind
    /// nil: the width it is offered (the narrow cards).
    var width: CGFloat?
    let l10n: Localizer
    var reduceMotion = false

    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.colorSchemeContrast) private var contrast

    private static let height = TaskPerformanceStyle.distributionHeight

    var body: some View {
        VStack(spacing: 2) {
            bars
            HStack {
                Text(verbatim: "0")
                Spacer(minLength: 0)
                Text(verbatim: "100")
            }
            .font(TaskPerformanceFont.axis)
            .foregroundStyle(.tertiary)
        }
        .frame(width: width)
        .frame(maxWidth: width == nil ? .infinity : nil)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(TaskPerformanceText.distributionWords(stats, l10n) ?? l10n("history.tasks.distribution"))
        .accessibilityAddTraits(.isImage)
    }

    private var bars: some View {
        let dark = colorScheme == .dark
        let increased = contrast == .increased
        let counts = Self.normalized(stats.histogram)
        let peak = max(1, counts.max() ?? 1)
        let medianBin = TaskPerformanceText.medianBin(stats)
        let columns = HStack(alignment: .bottom, spacing: 2) {
            ForEach(0..<10, id: \.self) { index in
                let count = counts[index]
                UnevenRoundedRectangle(topLeadingRadius: 1.5, topTrailingRadius: 1.5, style: .continuous)
                    .fill(TaskPerformancePalette.bar(source, median: index == medianBin, dark: dark, increasedContrast: increased))
                    .frame(height: count > 0 ? max(1.5, CGFloat(count) / CGFloat(peak) * Self.height) : 0)
                    .frame(maxWidth: .infinity)
            }
        }
        // Bars spring to their new heights; with Reduce Motion the old and new sets crossfade.
        return ZStack(alignment: .bottom) {
            if reduceMotion {
                columns.id(counts).transition(.opacity)
            } else {
                columns
            }
        }
        .frame(height: Self.height, alignment: .bottom)
        .overlay(alignment: .bottom) {
            Rectangle()
                .fill(HistoryPalette.grid(dark: dark, increasedContrast: increased))
                .frame(height: 0.5)
        }
        .overlay {
            if let median = stats.medianScore {
                GeometryReader { proxy in
                    let x = CGFloat(median.clamped(to: 0...100) / 100) * proxy.size.width
                    Rectangle()
                        .fill(TaskPerformancePalette.tick(increasedContrast: increased))
                        .frame(width: 1, height: Self.height + 3)
                        .position(x: min(max(x, 0.5), proxy.size.width - 0.5), y: Self.height / 2 - 1)
                }
            }
        }
        .animation(TaskPerformanceMotion.bars(reduceMotion: reduceMotion), value: counts)
    }

    private static func normalized(_ histogram: [Int]) -> [Int] {
        let bins = histogram.prefix(10).map { max(0, $0) }
        return bins + Array(repeating: 0, count: 10 - bins.count)
    }
}

/// A compared value: 13 pt tabular digits that crossfade when they change.
struct TaskValueText: View {
    let text: String
    let emphasis: TaskValueEmphasis
    var font = TaskPerformanceFont.value
    var tooltip: String?

    var body: some View {
        Text(text)
            .font(font)
            .fontWeight(emphasis.weight)
            .foregroundStyle(emphasis.style)
            .lineLimit(1)
            .minimumScaleFactor(0.75)
            .fixedSize(horizontal: false, vertical: true)
            .contentTransition(.opacity)
            .animation(TaskPerformanceMotion.fade, value: text)
            .animation(TaskPerformanceMotion.fade, value: emphasis)
            .help(tooltip ?? "")
    }
}
