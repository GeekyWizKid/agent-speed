import AgentSpeedCore
import AgentSpeedOrbModel
import SwiftUI

/// A still miniature of a board style (DESIGN 2c Settings: the Boards list's style preview): the style's panel fill,
/// a hero value with its heat title, and four ranked rows with source accents, in the style's own language — glass
/// an aurora behind frosted rows and glowing share lines, timing position blocks under a heat rail, neon a lit tube
/// and an LED bar. Drawn once with Core Graphics paths; nothing animates.
struct BoardStyleThumbnail: View {
    let style: BoardStyle
    var size: CGFloat = 38

    @Environment(\.colorScheme) private var colorScheme

    private static let rows: [(source: SourceKind, name: Double, share: Double)] = [
        (.claudeCode, 0.42, 0.92), (.codex, 0.34, 0.64), (.gemini, 0.38, 0.4), (.opencode, 0.28, 0.22),
    ]

    var body: some View {
        let dark = colorScheme == .dark
        let corner = size * CGFloat(style.cornerRadius) / 340 * 1.25
        Canvas { context, canvas in
            let side = canvas.width
            let shape = Path(roundedRect: CGRect(origin: .zero, size: canvas), cornerRadius: corner, style: .continuous)
            context.clip(to: shape)
            drawBackdrop(&context, side: side, dark: dark)
            drawHero(&context, side: side, dark: dark)
            drawRows(&context, side: side, dark: dark)
            context.stroke(shape, with: .color(edge(dark)), lineWidth: style == .neon ? max(1, side * 0.022) : 0.5)
        }
        .frame(width: size, height: size)
        .shadow(color: style == .neon && dark ? Color(red: 0.3, green: 0.75, blue: 1).opacity(0.35) : .black.opacity(dark ? 0.3 : 0.12),
                radius: size * 0.06, y: size * 0.02)
        .accessibilityHidden(true)
    }

    private func ink(_ dark: Bool, _ level: Double) -> Color {
        dark ? Color.white.opacity(level) : Color(red: 0.07, green: 0.08, blue: 0.12).opacity(level)
    }

    private func edge(_ dark: Bool) -> Color {
        switch style {
        case .glass: dark ? .white.opacity(0.16) : .black.opacity(0.09)
        case .timing: dark ? .white.opacity(0.1) : .black.opacity(0.08)
        case .neon: Color(red: 0.25, green: 0.78, blue: 1).opacity(dark ? 0.9 : 0.75)
        }
    }

    private var heat: Color { Color(red: 0.96, green: 0.55, blue: 0.14) }

    private func drawBackdrop(_ context: inout GraphicsContext, side: CGFloat, dark: Bool) {
        let bounds = CGRect(x: 0, y: 0, width: side, height: side)
        switch style {
        case .glass:
            let stage = dark
                ? [Color(red: 0.05, green: 0.06, blue: 0.13), Color(red: 0.11, green: 0.06, blue: 0.19), Color(red: 0.03, green: 0.08, blue: 0.1)]
                : [Color(red: 0.87, green: 0.9, blue: 0.95), Color(red: 0.95, green: 0.89, blue: 0.93), Color(red: 0.86, green: 0.94, blue: 0.91)]
            context.fill(Path(bounds), with: .linearGradient(Gradient(colors: stage), startPoint: .zero,
                                                             endPoint: CGPoint(x: side, y: side)))
            let aurora = CGRect(x: side * 0.35, y: -side * 0.25, width: side * 0.9, height: side * 0.7)
            context.fill(Path(ellipseIn: aurora), with: .radialGradient(
                Gradient(colors: [heat.opacity(dark ? 0.55 : 0.4), heat.opacity(0)]),
                center: CGPoint(x: aurora.midX, y: aurora.midY), startRadius: 0, endRadius: aurora.width / 2))
            context.fill(Path(bounds), with: .color(dark ? Color(red: 0.08, green: 0.09, blue: 0.13).opacity(0.5)
                                                         : Color(red: 0.99, green: 0.99, blue: 1).opacity(0.55)))
        case .timing:
            context.fill(Path(bounds), with: .color(dark ? Color(red: 0.05, green: 0.055, blue: 0.08) : Color(red: 0.984, green: 0.984, blue: 0.99)))
            let rail = CGRect(x: 0, y: 0, width: side, height: max(1.5, side * 0.035))
            context.fill(Path(rail), with: .linearGradient(
                Gradient(colors: [Color(red: 0.94, green: 0.27, blue: 0.27), heat, heat.opacity(0.2)]),
                startPoint: .zero, endPoint: CGPoint(x: side, y: 0)))
        case .neon:
            context.fill(Path(bounds), with: .color(dark ? Color(red: 0.02, green: 0.024, blue: 0.047) : Color(red: 0.97, green: 0.973, blue: 0.99)))
            let grid = ink(dark, dark ? 0.04 : 0.035)
            var lines = Path()
            for step in stride(from: side / 8, to: side, by: side / 8) {
                lines.move(to: CGPoint(x: step, y: 0))
                lines.addLine(to: CGPoint(x: step, y: side))
            }
            context.stroke(lines, with: .color(grid), lineWidth: 0.5)
        }
    }

    private func drawHero(_ context: inout GraphicsContext, side: CGFloat, dark: Bool) {
        let pad = side * 0.1
        let number = CGRect(x: pad, y: pad * 1.15, width: side * 0.36, height: side * 0.11)
        context.fill(Path(roundedRect: number, cornerRadius: number.height * 0.3), with: .color(ink(dark, 0.92)))
        let title = CGRect(x: number.maxX + side * 0.04, y: number.minY + number.height * 0.3, width: side * 0.16,
                           height: number.height * 0.55)
        context.fill(Path(roundedRect: title, cornerRadius: title.height / 2), with: .color(heat))
        let top = number.maxY + side * 0.05
        switch style {
        case .glass:
            var spark = Path()
            let values: [CGFloat] = [0.3, 0.45, 0.35, 0.6, 0.5, 0.8, 0.7, 0.9]
            for (index, value) in values.enumerated() {
                let point = CGPoint(x: side * 0.56 + CGFloat(index) * side * 0.048, y: pad + side * 0.13 * (1 - value))
                index == 0 ? spark.move(to: point) : spark.addLine(to: point)
            }
            context.stroke(spark, with: .color(heat.opacity(0.9)), style: StrokeStyle(lineWidth: max(0.8, side * 0.018), lineCap: .round))
        case .timing:
            for index in 0..<10 {
                let height = side * 0.06 * CGFloat([0.4, 0.6, 0.5, 0.8, 0.7, 0.9, 0.6, 1, 0.85, 0.95][index])
                let bar = CGRect(x: side * 0.6 + CGFloat(index) * side * 0.031, y: pad + side * 0.12 - height,
                                 width: side * 0.02, height: height)
                context.fill(Path(bar), with: .color(index > 6 ? heat : ink(dark, 0.25)))
            }
        case .neon:
            for index in 0..<12 {
                let segment = CGRect(x: pad + CGFloat(index) * (side - 2 * pad) / 12, y: top - side * 0.015,
                                     width: (side - 2 * pad) / 12 - side * 0.012, height: side * 0.03)
                let lit = index < 8
                let color = index < 5 ? Color(red: 0.2, green: 0.83, blue: 0.6) : index < 8 ? heat : ink(dark, 0.12)
                context.fill(Path(segment), with: .color(lit ? color : color))
            }
        }
    }

    private func drawRows(_ context: inout GraphicsContext, side: CGFloat, dark: Bool) {
        let pad = side * 0.1
        let rowHeight = side * 0.135
        var y = side * 0.4
        for (rank, row) in Self.rows.enumerated() {
            let tint = row.source.tint(dark: dark)
            let mid = y + rowHeight / 2
            switch style {
            case .timing:
                let block = CGRect(x: pad * 0.6, y: y + rowHeight * 0.14, width: rowHeight * 0.72, height: rowHeight * 0.72)
                context.fill(Path(roundedRect: block, cornerRadius: block.height * 0.18),
                             with: .color(rank == 0 ? Color(red: 0.98, green: 0.8, blue: 0.08) : ink(dark, dark ? 0.12 : 0.1)))
                context.fill(Path(CGRect(x: block.maxX + side * 0.02, y: y + rowHeight * 0.14, width: side * 0.012,
                                         height: rowHeight * 0.72)), with: .color(tint))
            default:
                let dot = CGRect(x: pad, y: mid - side * 0.022, width: side * 0.044, height: side * 0.044)
                context.fill(Path(ellipseIn: dot), with: .color(tint))
            }
            let nameX = style == .timing ? pad * 0.6 + rowHeight + side * 0.03 : pad + side * 0.08
            let name = CGRect(x: nameX, y: mid - side * 0.022, width: side * row.name, height: side * 0.044)
            context.fill(Path(roundedRect: name, cornerRadius: name.height / 2), with: .color(ink(dark, rank == 0 ? 0.72 : 0.5)))
            let value = CGRect(x: side - pad - side * 0.13, y: mid - side * 0.024, width: side * 0.13, height: side * 0.048)
            context.fill(Path(roundedRect: value, cornerRadius: value.height / 2), with: .color(ink(dark, rank == 0 ? 0.85 : 0.6)))
            if style != .timing {
                let line = CGRect(x: nameX, y: y + rowHeight - side * 0.016, width: (side - pad - nameX) * row.share,
                                  height: max(0.75, side * 0.012))
                context.fill(Path(roundedRect: line, cornerRadius: line.height / 2), with: .color(tint.opacity(style == .neon ? 0.95 : 0.75)))
            }
            y += rowHeight
        }
    }
}
