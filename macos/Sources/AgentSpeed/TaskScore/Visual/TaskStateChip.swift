import SwiftUI

/// A task state chip (DESIGN 12, *State chips*): 18 pt tall, fully rounded, a 10 pt glyph and 11 pt semibold text
/// 4 pt apart. Passing is mint, failing amber (never red); the unproven and empty states are neutral, outlined with
/// dots. Increase Contrast trades the fills for a solid 1 pt stroke in the text color.
struct TaskStateChip: View {
    let state: TaskScoreState
    let title: String

    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.colorSchemeContrast) private var contrast

    static let height: CGFloat = 18

    var body: some View {
        let dark = colorScheme == .dark
        let increased = contrast == .increased
        let colors = TaskScorePalette.chip(state, dark: dark)
        let text = colors.text
        HStack(spacing: 4) {
            glyph(text)
            Text(title)
                .font(.system(size: 11, weight: .semibold))
                .lineLimit(1)
        }
        .foregroundStyle(text)
        .padding(.horizontal, 7)
        .frame(height: Self.height)
        .background {
            if let fill = colors.fill, !increased { Capsule(style: .continuous).fill(fill) }
        }
        .overlay {
            if increased {
                Capsule(style: .continuous).strokeBorder(text, lineWidth: 1)
            } else if colors.fill == nil {
                Capsule(style: .continuous)
                    .strokeBorder(Color(nsColor: .labelColor).opacity(0.35),
                                  style: StrokeStyle(lineWidth: 1, lineCap: .round, dash: [0, 2.5]))
            }
        }
        .fixedSize()
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(title)
    }

    @ViewBuilder
    private func glyph(_ color: Color) -> some View {
        switch state {
        case .passing:
            Image(systemName: "checkmark.circle.fill").font(.system(size: 10, weight: .semibold))
        case .failing:
            Image(systemName: "exclamationmark.circle.fill").font(.system(size: 10, weight: .semibold))
        case .pending:
            Image(systemName: "clock").font(.system(size: 10, weight: .semibold))
        case .unverified:
            DottedRingGlyph(color: color).frame(width: 10, height: 10)
        case .none:
            EmptyView()
        }
    }
}

/// `unverified`'s glyph: a ring drawn as eight dots, the proof mark's language in a 10 pt circle.
struct DottedRingGlyph: View {
    var color: Color
    var dots = 8

    var body: some View {
        Canvas { context, size in
            let diameter = min(size.width, size.height) * 0.2
            let radius = min(size.width, size.height) / 2 - diameter / 2 - 0.25
            let center = CGPoint(x: size.width / 2, y: size.height / 2)
            var path = Path()
            for index in 0..<dots {
                let angle = Double(index) / Double(dots) * 2 * .pi - .pi / 2
                let point = CGPoint(x: center.x + radius * cos(angle), y: center.y + radius * sin(angle))
                path.addEllipse(in: CGRect(x: point.x - diameter / 2, y: point.y - diameter / 2,
                                           width: diameter, height: diameter))
            }
            context.fill(path, with: .color(color))
        }
        .accessibilityHidden(true)
    }
}
