import SwiftUI

/// A dimension's bar (DESIGN 12, *Score card*): 4 pt, radius 2, filled `value / max` in the score ink; a dotted track
/// with no fill while there is no verification result; the dotted outline of the held credit while `pending`.
struct TaskDimensionBar: View {
    let row: TaskDimensionRow
    var height: CGFloat = TaskCardLayout.barHeight

    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.colorSchemeContrast) private var contrast
    @Environment(\.taskReduceMotion) private var reduceMotion

    var body: some View {
        let dark = colorScheme == .dark
        let increased = contrast == .increased
        let ink = TaskScorePalette.ink(dark: dark, increasedContrast: increased)
        GeometryReader { proxy in
            let width = proxy.size.width
            if row.isUnproven {
                ProofMarkDots(diameter: height - 1, color: TaskScorePalette.dots(increasedContrast: increased), pitch: 5 / 3)
            } else {
                ZStack(alignment: .leading) {
                    Capsule(style: .continuous)
                        .fill(TaskScorePalette.track(dark: dark, increasedContrast: increased))
                    if case .pending = row.value {
                        ProofMarkDots(diameter: height, color: ink.opacity(increased ? 1 : 0.9), pitch: 1.25)
                            .frame(width: max(height, width * row.fraction))
                    } else if row.fraction > 0 {
                        Capsule(style: .continuous)
                            .fill(ink)
                            .frame(width: max(height, width * row.fraction))
                    }
                }
                .animation(reduceMotion ? nil : .spring(response: 0.35, dampingFraction: 0.86), value: row.fraction)
            }
        }
        .frame(height: height)
        .accessibilityHidden(true)
    }
}

/// The composition bar under the status line: 100 points across, the five dimensions stacked V C R E K as long as
/// their values with 2 pt gaps between them, 6 pt tall, radius 3. While `pending`, V's place holds the dotted outline
/// of the credit a passing re-run restores.
struct TaskCompositionBar: View {
    let segments: [TaskCompositionSegment]
    var height: CGFloat = TaskCardLayout.compositionHeight
    var gap: CGFloat = 2

    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.colorSchemeContrast) private var contrast

    var body: some View {
        let dark = colorScheme == .dark
        let increased = contrast == .increased
        let ink = TaskScorePalette.ink(dark: dark, increasedContrast: increased)
        Canvas { context, size in
            let track = Path(roundedRect: CGRect(origin: .zero, size: size), cornerRadius: height / 2, style: .continuous)
            context.fill(track, with: .color(TaskScorePalette.track(dark: dark, increasedContrast: increased)))
            context.clip(to: track)
            var x: CGFloat = 0
            for (index, segment) in segments.enumerated() {
                let full = size.width * CGFloat(segment.points) / 100
                let last = index == segments.count - 1
                let width = max(0, full - (last ? 0 : gap))
                if segment.held {
                    let diameter = height - 2
                    let count = max(1, Int(((width - diameter) / (diameter * 4 / 3)).rounded(.down)) + 1)
                    let step = count > 1 ? (width - diameter) / CGFloat(count - 1) : 0
                    var dots = Path()
                    for dot in 0..<count {
                        dots.addEllipse(in: CGRect(x: x + CGFloat(dot) * step, y: (size.height - diameter) / 2,
                                                   width: diameter, height: diameter))
                    }
                    context.fill(dots, with: .color(ink.opacity(increased ? 1 : 0.9)))
                } else {
                    context.fill(Path(CGRect(x: x, y: 0, width: width, height: size.height)), with: .color(ink))
                }
                x += full
            }
        }
        .frame(height: height)
        .accessibilityHidden(true)
    }
}
