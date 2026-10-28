import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit

/// The orb's two-line readout (DESIGN 10, Floating orb readout and Intelligence readout). Network speed draws download
/// over upload in fixed cells sharing their left edge, the block centered on the orb; the intelligence readout draws
/// a larger `智力 133` line over a `思考 412 tok/s` line, each centred on the orb on its own. Characters are replaced in
/// place (no animation); the block fades by the quiet rule and steps aside for the new-peak pill. Drawn from the
/// shared glyph atlas, so drawing never allocates once the glyphs of a size exist.
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

    /// The ink of one block: its opacity, palette and clip.
    private struct Ink {
        var alpha: Double
        var palette: Palette
        var clip: OrbTextClip

        func add(_ texture: OrbTextTexture, x: CGFloat, baseline: CGFloat, color: RGBColor, into scene: inout OrbScene) {
            scene.add(OrbTextDraw(texture, x: x, baseline: baseline, color: color.opacity(alpha),
                                  glow: palette.shadow?.opacity(alpha), glowRadius: palette.shadowRadius, clip: clip))
        }
    }

    private func numberStyle(_ size: CGFloat) -> OrbTextStyle {
        OrbTextStyle(size: size, weight: .semibold, monospacedDigits: true, padding: max(1, 0.03 * size))
    }

    private func unitStyle(_ t: CGFloat) -> OrbTextStyle {
        OrbTextStyle(size: max(OrbReadoutBuilder.textFloor * OrbLinePlacement.unitScale, OrbLinePlacement.unitScale * t),
                     weight: .medium, padding: max(1, 0.03 * t))
    }

    /// The glyphs: weight 600 at 0.82 × their line's size (DESIGN 10, Intelligence readout).
    private func glyphStyle(_ size: CGFloat) -> OrbTextStyle {
        var style = unitStyle(size)
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
        return placement.stackShift(layout: layout, diameter: frame.s, format: lines.format) * CGFloat(Easing.smoothstep(0, 1, shown))
    }

    func add(into scene: inout OrbScene, _ frame: OrbFrame, placement: OrbLinePlacement, palette: Palette,
             clip: OrbTextClip = .none, yieldsToPill: Bool = true) {
        guard let lines = frame.content.lines, let reading = lines.reading, !frame.unavailable else { return }
        let alpha = alpha(reading, frame, yieldsToPill: yieldsToPill) * frame.alpha
        guard alpha > 0.01,
              OrbLineMetrics.fits(placement, diameter: frame.s, format: lines.format, language: frame.content.language) else { return }
        let ink = Ink(alpha: alpha, palette: palette, clip: clip)
        switch reading.content {
        case .traffic(let rx, let tx):
            addTraffic(rx: rx, tx: tx, bits: lines.format == .traffic(bits: true), into: &scene, frame, placement: placement, ink: ink)
        case .intelligence(let index, let thinking):
            addIntelligence(index: index, thinking: thinking, into: &scene, frame, placement: placement, ink: ink)
        }
    }

    /// Download over upload (upload nearest above the value, download nearest below it in `needle`), each an arrow, a
    /// number right-aligned in the `8.88` cell and a unit left-aligned in its cell; the lines share their left edge.
    private func addTraffic(rx: TrafficFormat.Parts, tx: TrafficFormat.Parts, bits: Bool, into scene: inout OrbScene,
                            _ frame: OrbFrame, placement: OrbLinePlacement, ink: Ink) {
        let t = placement.textSize(diameter: frame.s)
        let cells = OrbLineMetrics.cells(size: t, bits: bits)
        let left = frame.center.x - cells.lineWidth(size: t) / 2
        let baselines = placement.baselines(center: frame.center.y + frame.readoutShift, diameter: frame.s)
        let nearest = placement.below ? (parts: rx, down: true) : (parts: tx, down: false)
        let other = placement.below ? (parts: tx, down: false) : (parts: rx, down: true)
        addTrafficLine(nearest.parts, down: nearest.down, left: left, baseline: baselines.nearest, size: t, cells: cells, into: &scene, ink: ink)
        addTrafficLine(other.parts, down: other.down, left: left, baseline: baselines.other, size: t, cells: cells, into: &scene, ink: ink)
    }

    private func addTrafficLine(_ parts: TrafficFormat.Parts, down: Bool, left: CGFloat, baseline: CGFloat, size t: CGFloat,
                                cells: OrbLineMetrics.Cells, into scene: inout OrbScene, ink: Ink) {
        let scale = scene.backingScale
        let style = numberStyle(t)
        if let arrow = textCache.arrowTexture(down: down, size: t, scale: scale) {
            ink.add(arrow, x: left, baseline: baseline, color: ink.palette.secondary, into: &scene)
        }
        let numberLeft = left + cells.lead + OrbLinePlacement.gap * t
        var x = numberLeft + cells.number - advance(parts.number, style: style, scale: scale)
        for character in parts.number {
            guard let digit = textCache.texture(character, style: style, language: .en, scale: scale) else { continue }
            ink.add(digit, x: x, baseline: baseline, color: ink.palette.number, into: &scene)
            x += digit.advance
        }
        if let unit = textCache.texture(parts.unit, style: unitStyle(t), language: .en, scale: scale) {
            ink.add(unit, x: numberLeft + cells.number + OrbLinePlacement.gap * t, baseline: baseline,
                    color: ink.palette.secondary, into: &scene)
        }
    }

    /// The intelligence line (`p = 1.25 t`) over the thinking line, each centred on the orb on its own.
    private func addIntelligence(index: Int, thinking: String, into scene: inout OrbScene, _ frame: OrbFrame,
                                 placement: OrbLinePlacement, ink: Ink) {
        let language = frame.content.language
        let glyphs = OrbLineGlyphs(language)
        let t = placement.textSize(diameter: frame.s)
        let p = OrbLinePlacement.intelligenceScale * t
        let baselines = placement.intelligenceBaselines(center: frame.center.y + frame.readoutShift, diameter: frame.s)
        addCentredLine(glyphs.intelligence, number: String(index), unit: nil, size: p, gap: OrbLinePlacement.intelligenceGap * p,
                       baseline: baselines.intelligence, frame, into: &scene, ink: ink)
        let unit = placement.thinkingUnit ? OrbLineMetrics.thinkingUnit : nil
        addCentredLine(glyphs.thinking, number: thinking, unit: unit, size: t, gap: OrbLinePlacement.gap * t,
                       baseline: baselines.thinking, frame, into: &scene, ink: ink)
    }

    /// `glyph · gap · number (· gap · unit)` at `size`, centred by its advances. Every piece starts on a device pixel,
    /// so the line moves only when its width changes and the atlas glyphs are drawn unresampled across.
    private func addCentredLine(_ glyph: String, number: String, unit: String?, size: CGFloat, gap: CGFloat,
                                baseline: CGFloat, _ frame: OrbFrame, into scene: inout OrbScene, ink: Ink) {
        let scale = scene.backingScale
        let style = numberStyle(size)
        guard let lead = textCache.texture(glyph, style: glyphStyle(size), language: frame.content.language, scale: scale) else { return }
        let tail = unit.flatMap { textCache.texture($0, style: unitStyle(size), language: .en, scale: scale) }
        let width = lead.advance + gap + advance(number, style: style, scale: scale) + (tail.map { gap + $0.advance } ?? 0)
        func snapped(_ x: CGFloat) -> CGFloat { (x * scale).rounded() / scale }
        var x = frame.center.x - width / 2
        ink.add(lead, x: snapped(x), baseline: baseline, color: ink.palette.secondary, into: &scene)
        x += lead.advance + gap
        for character in number {
            guard let digit = textCache.texture(character, style: style, language: .en, scale: scale) else { continue }
            ink.add(digit, x: snapped(x), baseline: baseline, color: ink.palette.number, into: &scene)
            x += digit.advance
        }
        if let tail {
            ink.add(tail, x: snapped(x + gap), baseline: baseline, color: ink.palette.secondary, into: &scene)
        }
    }

    /// The advance of `text` drawn character by character from the atlas.
    private func advance(_ text: String, style: OrbTextStyle, scale: CGFloat) -> CGFloat {
        var width: CGFloat = 0
        for character in text {
            width += textCache.texture(character, style: style, language: .en, scale: scale)?.advance ?? 0
        }
        return width
    }
}
