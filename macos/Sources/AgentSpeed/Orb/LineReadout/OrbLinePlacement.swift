import AgentSpeedOrbModel
import AppKit

/// Where a style puts the two-line readout (DESIGN 10, Floating orb readout), in diameters from the center.
struct OrbLinePlacement: Equatable {
    /// Baseline of the line nearest the value.
    var nearestBaseline: CGFloat
    /// The lines sit below the value (`needle`, ↓ line nearest); otherwise above it (↑ line nearest).
    var below: Bool
    /// Text size `t` = max(7 pt, size × S).
    var size: CGFloat
    /// A line must fit in this × S, else the readout is not drawn.
    var fit: CGFloat
    /// The readout stack moves down while the block shows, so block, value, unit and title stay centered as one stack;
    /// `reactor` and `needle` keep their readout in a fixed window or hub.
    var recenters = false

    static let led = OrbLinePlacement(nearestBaseline: -0.245, below: false, size: 0.050, fit: 0.44, recenters: true)
    static let liquid = led
    static let reactor = OrbLinePlacement(nearestBaseline: -0.175, below: false, size: 0.046, fit: 0.32)
    static let needle = OrbLinePlacement(nearestBaseline: 0.108, below: true, size: 0.046, fit: 0.285)
    static let minimal = OrbLinePlacement(nearestBaseline: -0.228, below: false, size: 0.054, fit: 0.50, recenters: true)

    static func of(_ style: OrbStyleKind) -> OrbLinePlacement {
        switch style {
        case .led: .led
        case .needle: .needle
        case .liquid: .liquid
        case .reactor: .reactor
        case .minimal: .minimal
        }
    }

    /// Line pitch, × t.
    static let pitch: CGFloat = 1.32
    /// Gaps between arrow, number and unit, × t.
    static let gap: CGFloat = 0.16
    /// Arrow box, × t.
    static let arrowWidth: CGFloat = 0.50
    static let arrowHeight: CGFloat = 0.66
    /// Unit size, × t.
    static let unitScale: CGFloat = 0.82

    /// Never below the 7 pt floor of every orb label.
    func textSize(diameter s: CGFloat) -> CGFloat { max(7, size * s) }

    /// Cap height of the orb fonts, × size.
    static let capHeight: CGFloat = 0.70

    /// Descent of a title line below its baseline, × its size.
    static let titleDescent: CGFloat = 0.22
    /// Clearance kept between the title and the agent dots, × S.
    static let dotsClearance: CGFloat = 0.05

    /// How far the readout stack (value, unit, title, pill and this block) moves down while the block shows above the
    /// value, so the whole stack stays centered (DESIGN 10): half the block's height over the value's cap top, but never
    /// so far that the title comes closer to the agent dots than the clearance.
    func stackShift(layout: OrbReadoutLayout, diameter s: CGFloat) -> CGFloat {
        guard recenters, !below else { return 0 }
        let t = textSize(diameter: s)
        let blockTop = nearestBaseline * s - Self.pitch * t - Self.capHeight * t
        let valueTop = (layout.valueBaseline - Self.capHeight * layout.valueSize) * s
        let centered = max(0, (valueTop - blockTop) / 2)
        guard let dots = layout.dots else { return centered }
        let titleSize = max(7, layout.titleSize * s)
        let titleBottom = layout.titleOffset * s + Self.titleDescent * titleSize
        let dotsTop = dots * s - OrbAgentDotsLayout.dotDiameter(s) / 2
        return min(centered, max(0, dotsTop - Self.dotsClearance * s - titleBottom))
    }

    /// Baselines of the line nearest the value and of the other one.
    func baselines(center y: CGFloat, diameter s: CGFloat) -> (nearest: CGFloat, other: CGFloat) {
        let nearest = y + nearestBaseline * s
        let pitch = Self.pitch * textSize(diameter: s)
        return (nearest, below ? nearest + pitch : nearest - pitch)
    }
}

/// The intelligence readout's glyphs in a language (`intel.glyph`, `intel.thinking.glyph`).
struct OrbLineGlyphs: Hashable {
    var intelligence: String
    var thinking: String

    init(_ language: AppLanguage) {
        intelligence = Localizer.string("intel.glyph", language: language)
        thinking = Localizer.string("intel.thinking.glyph", language: language)
    }
}

/// Widths of the readout's fixed cells, measured with the platform font once per size (DESIGN 10: the readout shows only
/// where a line fits, computed per size, never truncated or shrunk below 7 pt). Both readouts share the table: a lead
/// cell (the arrow, or the intelligence glyphs widened to their advance), the `8.88` number cell (the index: an `888`
/// cell at its left) and the unit cell (`MB/s`, `Mb/s` for bits; `tok/s` for thinking).
@MainActor
enum OrbLineMetrics {
    struct Cells: Equatable {
        var lead: CGFloat
        var number: CGFloat
        /// The `888` cell of an integer line, inside the number cell.
        var integer: CGFloat
        var unit: CGFloat

        /// lead, gap, number, gap, unit.
        func lineWidth(size t: CGFloat) -> CGFloat { lead + 2 * OrbLinePlacement.gap * t + number + unit }
    }

    private struct Key: Hashable {
        var size: CGFloat
        var format: OrbLineFormat
        var language: AppLanguage?
    }

    private static var cache: [Key: Cells] = [:]

    /// The thinking line's unit (DESIGN 10, Intelligence readout).
    static let thinkingUnit = "tok/s"

    static func numberFont(_ t: CGFloat) -> NSFont { .monospacedDigitSystemFont(ofSize: t, weight: .semibold) }
    static func unitFont(_ t: CGFloat) -> NSFont { .systemFont(ofSize: OrbLinePlacement.unitScale * t, weight: .medium) }
    static func glyphFont(_ t: CGFloat) -> NSFont { .systemFont(ofSize: OrbLinePlacement.unitScale * t, weight: .semibold) }

    static func cells(size t: CGFloat, format: OrbLineFormat, language: AppLanguage) -> Cells {
        let key = Key(size: t, format: format, language: format == .intelligence ? language : nil)
        if let cached = cache[key] { return cached }
        func width(_ text: String, _ font: NSFont) -> CGFloat {
            let line = CTLineCreateWithAttributedString(NSAttributedString(string: text, attributes: [.font: font]))
            return CGFloat(CTLineGetTypographicBounds(line, nil, nil, nil))
        }
        let arrow = OrbLinePlacement.arrowWidth * t
        let number = width("8.88", numberFont(t))
        let integer = width("888", numberFont(t))
        let glyphs = OrbLineGlyphs(language)
        let cells = switch format {
        case .traffic(let bits):
            Cells(lead: arrow, number: number, integer: integer, unit: width(bits ? "Mb/s" : "MB/s", unitFont(t)))
        case .intelligence:
            Cells(lead: max(arrow, width(glyphs.intelligence, glyphFont(t)), width(glyphs.thinking, glyphFont(t))),
                  number: number, integer: integer, unit: max(width("MB/s", unitFont(t)), width(thinkingUnit, unitFont(t))))
        }
        if cache.count > 128 { cache.removeAll(keepingCapacity: true) }
        cache[key] = cells
        return cells
    }

    static func fits(_ placement: OrbLinePlacement, diameter s: CGFloat, format: OrbLineFormat, language: AppLanguage) -> Bool {
        let t = placement.textSize(diameter: s)
        return cells(size: t, format: format, language: language).lineWidth(size: t) <= placement.fit * s
    }
}
