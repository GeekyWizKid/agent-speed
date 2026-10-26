import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit

/// The orb's two-line readout (DESIGN 10, Floating orb readout and Intelligence readout). Network speed draws download
/// over upload, each an arrow, a number right-aligned in a cell as wide as `8.88` and a unit left-aligned in a cell as
/// wide as `MB/s`; the intelligence readout puts the index on the line nearest the value and thinking tok/s on the
/// other, with text glyphs in the arrow's cell. The lines share their left edge and are centered on the orb.
/// Characters are replaced in their cells (no animation); the block fades by the quiet rule and steps aside for the
/// new-peak pill. Drawn from the shared glyph atlas, so drawing never allocates once the digits of a size exist.
@MainActor
final class OrbLineReadout {
    /// The readout fades out (and back) around the new-peak pill (DESIGN 10).
    static let pillFade: TimeInterval = 0.15

    let textCache: OrbTextCache

    init(textCache: OrbTextCache = .shared) {
        self.textCache = textCache
    }

    /// Colors of the two kinds of text: number, and arrow, glyph and unit.
    struct Palette {
        var number: RGBColor
        var secondary: RGBColor
        var shadow: RGBColor?
        var shadowRadius: CGFloat = 0

        /// DESIGN 10: number at 80 % of the value token, arrow and unit in the secondary token; Increase Contrast 92 / 70 %.
        static func standard(dark: Bool, increaseContrast: Bool) -> Palette {
            let ink = dark ? RGBColor.white : RGBColor(hex: 0x0B0D12)
            if increaseContrast { return Palette(number: ink.opacity(0.92), secondary: ink.opacity(0.70)) }
            return Palette(number: ink.opacity(dark ? 0.78 : 0.76), secondary: (dark ? RGBColor.white : .black).opacity(0.50))
        }

        /// Under the liquid surface (2a.3 layer 10 split rule).
        static func underLiquid(dark: Bool, diameter s: CGFloat, increaseContrast: Bool) -> Palette {
            if dark {
                return Palette(number: .white.opacity(increaseContrast ? 0.97 : 0.94), secondary: .white.opacity(increaseContrast ? 0.80 : 0.72),
                               shadow: .black.opacity(0.55), shadowRadius: 0.015 * s)
            }
            let ink = RGBColor(hex: 0x0B0D12)
            return Palette(number: ink.opacity(increaseContrast ? 0.94 : 0.88), secondary: ink.opacity(increaseContrast ? 0.72 : 0.62))
        }
    }

    /// What one line draws in its three cells.
    private enum Lead {
        case arrow(down: Bool)
        case glyph(String)
    }

    private struct Line {
        var lead: Lead
        var number: String
        /// Right-aligned in the `888` cell instead of the `8.88` one (the index).
        var isInteger = false
        var unit: String?
    }

    private func numberStyle(_ t: CGFloat) -> OrbTextStyle {
        OrbTextStyle(size: t, weight: .semibold, monospacedDigits: true, padding: max(1, 0.03 * t))
    }

    private func unitStyle(_ t: CGFloat) -> OrbTextStyle {
        OrbTextStyle(size: max(OrbReadoutBuilder.textFloor * OrbLinePlacement.unitScale, OrbLinePlacement.unitScale * t),
                     weight: .medium, padding: max(1, 0.03 * t))
    }

    /// The glyphs: weight 600 at the unit's size (DESIGN 10, Intelligence readout).
    private func glyphStyle(_ t: CGFloat) -> OrbTextStyle {
        var style = unitStyle(t)
        style.weight = .semibold
        return style
    }

    // MARK: Drawing

    /// The block's opacity at the frame: the quiet rule, around the new-peak pill (where the pill shares the area).
    func alpha(_ reading: OrbLineReading, _ frame: OrbFrame, yieldsToPill: Bool) -> Double {
        var alpha = reading.alpha(at: frame.time, reduceMotion: frame.effects.reduceMotion)
        if yieldsToPill, frame.effects.celebrate, frame.peakAge >= 0 {
            let age = frame.peakAge
            let fade = frame.effects.reduceMotion ? 0.001 : Self.pillFade
            let total = OrbReadoutBuilder.peakDuration
            if age < total {
                alpha *= 1 - min(1, age / fade)
            } else if age < total + fade {
                alpha *= (age - total) / fade
            }
        }
        return alpha
    }

    /// The readout stack's shift at the frame: follows the block's quiet-rule fade (eased), so the value glides down as
    /// the block appears and back when it has faded; never moves for the new-peak pill.
    func stackShift(_ frame: OrbFrame, placement: OrbLinePlacement, layout: OrbReadoutLayout) -> CGFloat {
        guard let lines = frame.content.lines, let reading = lines.reading, !frame.unavailable,
              OrbLineMetrics.fits(placement, diameter: frame.s, format: lines.format, language: frame.content.language) else { return 0 }
        let shown = reading.alpha(at: frame.time, reduceMotion: frame.effects.reduceMotion)
        return placement.stackShift(layout: layout, diameter: frame.s) * CGFloat(Easing.smoothstep(0, 1, shown))
    }

    func add(into scene: inout OrbScene, _ frame: OrbFrame, placement: OrbLinePlacement, palette: Palette,
             clip: OrbTextClip = .none, yieldsToPill: Bool = true) {
        guard let lines = frame.content.lines, let reading = lines.reading, !frame.unavailable else { return }
        let alpha = alpha(reading, frame, yieldsToPill: yieldsToPill) * frame.alpha
        guard alpha > 0.01 else { return }
        let language = frame.content.language
        let t = placement.textSize(diameter: frame.s)
        let cells = OrbLineMetrics.cells(size: t, format: lines.format, language: language)
        let width = cells.lineWidth(size: t)
        guard width <= placement.fit * frame.s else { return }
        let left = frame.center.x - width / 2
        let baselines = placement.baselines(center: frame.center.y + frame.readoutShift, diameter: frame.s)
        let gap = OrbLinePlacement.gap * t
        let numberLeft = left + cells.lead + gap
        let (nearest, other) = Self.lines(reading.content, placement: placement, language: language)
        let scale = scene.backingScale
        func draw(_ texture: OrbTextTexture, x: CGFloat, baseline: CGFloat, color: RGBColor) {
            scene.add(OrbTextDraw(texture, x: x, baseline: baseline, color: color.opacity(alpha),
                                  glow: palette.shadow?.opacity(alpha), glowRadius: palette.shadowRadius, clip: clip))
        }
        for (line, baseline) in [(nearest, baselines.nearest), (other, baselines.other)] {
            switch line.lead {
            case .arrow(let down):
                if let arrow = textCache.arrowTexture(down: down, size: t, scale: scale) {
                    draw(arrow, x: left, baseline: baseline, color: palette.secondary)
                }
            case .glyph(let text):
                if let glyph = textCache.texture(text, style: glyphStyle(t), language: language, scale: scale) {
                    draw(glyph, x: left + (cells.lead - glyph.advance) / 2, baseline: baseline, color: palette.secondary)
                }
            }
            let digits = line.number.compactMap { textCache.texture($0, style: numberStyle(t), language: .en, scale: scale) }
            var x = numberLeft + (line.isInteger ? cells.integer : cells.number) - digits.reduce(0) { $0 + $1.advance }
            for digit in digits {
                draw(digit, x: x, baseline: baseline, color: palette.number)
                x += digit.advance
            }
            if let unit = line.unit, let texture = textCache.texture(unit, style: unitStyle(t), language: .en, scale: scale) {
                draw(texture, x: numberLeft + cells.number + gap, baseline: baseline, color: palette.secondary)
            }
        }
    }

    /// The line nearest the value and the other one: upload is nearest above the value and download below it (`needle`);
    /// the index is always nearest.
    private static func lines(_ content: OrbLineContent, placement: OrbLinePlacement,
                              language: AppLanguage) -> (nearest: Line, other: Line) {
        switch content {
        case .traffic(let rx, let tx):
            let down = Line(lead: .arrow(down: true), number: rx.number, unit: rx.unit)
            let up = Line(lead: .arrow(down: false), number: tx.number, unit: tx.unit)
            return placement.below ? (down, up) : (up, down)
        case .intelligence(let index, let thinking):
            let glyphs = OrbLineGlyphs(language)
            return (Line(lead: .glyph(glyphs.intelligence), number: String(index), isInteger: true),
                    Line(lead: .glyph(glyphs.thinking), number: thinking, unit: OrbLineMetrics.thinkingUnit))
        }
    }
}
