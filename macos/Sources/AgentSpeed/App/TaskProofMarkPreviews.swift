import AppKit
import SwiftUI

/// `--render-previews` group `taskcard`: the proof mark's drawing helpers on their own (DESIGN 12, *Proof mark*) — the
/// SwiftUI underline at the card's, dashboard's and orb floor's sizes, the CoreGraphics underline at the menu bar's
/// depth, glass's dotted arc with its hollow tip ring next to a proven arc, and neon's hollow lit segments.
struct TaskProofMarkSheet: View {
    @Environment(\.colorScheme) private var colorScheme

    var body: some View {
        let dark = colorScheme == .dark
        let ink = Color(nsColor: .labelColor)
        VStack(alignment: .leading, spacing: 22) {
            HStack(alignment: .lastTextBaseline, spacing: 26) {
                sample("86", size: 34, weight: .semibold, shown: false)
                sample("34", size: 34, weight: .semibold, shown: true)
                sample("9", size: 34, weight: .semibold, shown: true)
                sample("72", size: 12, weight: .medium, shown: true)
                sample("100", size: 12, weight: .medium, shown: true)
                sample("41", size: 7, weight: .semibold, shown: true)
            }
            HStack(alignment: .center, spacing: 28) {
                Canvas { context, size in
                    context.withCGContext { cg in
                        let font = NSFont.monospacedDigitSystemFont(ofSize: 13, weight: .semibold)
                        let line = CTLineCreateWithAttributedString(NSAttributedString(string: "86", attributes: [
                            .font: font, .foregroundColor: NSColor.labelColor,
                        ]))
                        let width = CTLineGetTypographicBounds(line, nil, nil, nil)
                        cg.saveGState()
                        cg.textMatrix = .identity
                        cg.translateBy(x: 6, y: 18)
                        cg.scaleBy(x: 1, y: -1)
                        CTLineDraw(line, cg)
                        cg.restoreGState()
                        ProofMark.drawUnderline(in: cg, origin: CGPoint(x: 6, y: 18), width: width, textSize: 13,
                                                color: NSColor.labelColor.cgColor, scale: 2, increasedContrast: false,
                                                depth: ProofMark.barDepth)
                    }
                }
                .frame(width: 44, height: 28)
                arc(fraction: 0.72, unproven: false, dark: dark)
                arc(fraction: 0.34, unproven: true, dark: dark)
                segments(lit: 7, unproven: false, dark: dark)
                segments(lit: 4, unproven: true, dark: dark)
            }
        }
        .foregroundStyle(ink)
        .padding(28)
    }

    private func sample(_ text: String, size: CGFloat, weight: Font.Weight, shown: Bool) -> some View {
        Text(text)
            .font(.system(size: size, weight: weight).monospacedDigit())
            .proofMark(shown, textSize: size, color: Color(nsColor: .labelColor))
    }

    private func arc(fraction: Double, unproven: Bool, dark: Bool) -> some View {
        let color = dark ? RGBColor(hex: 0xF5D0FE).color : RGBColor(hex: 0xC05BD9).color
        return Canvas { context, size in
            let lineWidth: CGFloat = 3
            let center = CGPoint(x: size.width / 2, y: size.height / 2)
            let radius = min(size.width, size.height) / 2 - 4
            var track = Path()
            track.addArc(center: center, radius: radius, startAngle: .degrees(150), endAngle: .degrees(390), clockwise: false)
            context.stroke(track, with: .color(.primary.opacity(0.14)), style: StrokeStyle(lineWidth: lineWidth, lineCap: .round))
            let end = 150 + 240 * fraction
            var value = Path()
            value.addArc(center: center, radius: radius, startAngle: .degrees(150), endAngle: .degrees(end), clockwise: false)
            context.stroke(value, with: .color(color),
                           style: unproven ? ProofMark.dottedArcStyle(lineWidth: lineWidth) : StrokeStyle(lineWidth: lineWidth, lineCap: .round))
            let angle = end * .pi / 180
            let tip = CGPoint(x: center.x + radius * cos(angle), y: center.y + radius * sin(angle))
            if unproven {
                context.withCGContext { cg in
                    ProofMark.drawTipRing(in: cg, at: tip, lineWidth: lineWidth, color: NSColor(color).cgColor,
                                          hole: (dark ? NSColor(white: 0.1, alpha: 1) : NSColor.white).cgColor)
                }
            } else {
                let ring = ProofMark.tipRing(at: tip, lineWidth: lineWidth).rect
                context.fill(Path(ellipseIn: ring), with: .color(.white))
            }
        }
        .frame(width: 66, height: 66)
    }

    private func segments(lit: Int, unproven: Bool, dark: Bool) -> some View {
        let color = dark ? RGBColor(hex: 0x7DD3FC).color : RGBColor(hex: 0x0284C7).color
        return Canvas { context, size in
            for index in 0..<10 {
                let rect = CGRect(x: CGFloat(index) * 9, y: 0, width: 6, height: size.height)
                let path = Path(roundedRect: rect, cornerRadius: 1.5)
                if index >= lit {
                    context.fill(path, with: .color(.primary.opacity(0.1)))
                } else if unproven {
                    context.withCGContext { cg in
                        ProofMark.strokeHollow(path.cgPath, in: cg, color: NSColor(color).cgColor)
                    }
                } else {
                    context.fill(path, with: .color(color))
                }
            }
        }
        .frame(width: 87, height: 16)
    }
}
