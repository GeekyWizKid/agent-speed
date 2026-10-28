import AgentSpeedCore
import AppKit

/// The menu bar intelligence block (DESIGN 10, Menu bar and taskbar): after the value and the money block,
/// `智力 125` over `思考 10 tok/s` for every enabled source, in the traffic block's 9 pt tabular text. Labels sit
/// left-aligned in a cell as wide as the wider label, both numbers end on one edge of a shared `8.88k` cell, and only
/// the thinking line carries the unit, so the block's width depends on the language alone. With nothing generated in
/// the window it stays, both numbers `–`, like a system meter: a block that came and went would shift every item to
/// its left. It needs neither network measuring nor any helper.
struct StatusItemIntelligence: Equatable {
    static let size: CGFloat = 9
    static let gap: CGFloat = 8
    static let pitch: CGFloat = 10
    static let labelGap: CGFloat = 3
    static let unitGap: CGFloat = 2.5
    /// The widest METRICS 7 form below 1 M tok/s; `888` fits in it.
    static let widestNumber = "8.88k"

    struct Line: Equatable {
        var label: String
        var number: String
        /// No value, or a zero thinking rate: the number takes the secondary color.
        var dim: Bool
    }

    var intelligence: Line
    var thinking: Line
    var unit: String
    /// Room for the numbers: the `8.88k` cell, wider only while the width governor holds a longer text.
    var cell: CGFloat
    var spoken: String

    static var font: NSFont { .monospacedDigitSystemFont(ofSize: size, weight: .medium) }

    @MainActor
    init?(model: AppModel) {
        let settings = model.settings
        guard settings.barTraffic == .intelligence, settings.barMode != .glyph else { return nil }
        self.init(model.snapshot.intelligence, l10n: model.l10n)
    }

    @MainActor
    init(_ sums: IntelligenceSums, l10n: Localizer) {
        let index = sums.shownIndex
        let thinking = index.map { _ in SpeedFormat.speed(sums.thinking) }
        intelligence = Line(label: l10n("intel.glyph"), number: index.map(String.init) ?? "–", dim: index == nil)
        self.thinking = Line(label: l10n("intel.thinking.glyph"), number: thinking ?? "–",
                             dim: thinking == nil || sums.thinking < LiveSpeedValue.visibleMinimum)
        unit = l10n("unit.tokS")
        cell = Self.width(Self.widestNumber)
        spoken = index.map { l10n("a11y.orb.intel", String($0), thinking ?? "0") } ?? l10n("intel.none")
        cell = max(cell, numberWidth)
    }

    private static func width(_ text: String) -> CGFloat {
        ceil((text as NSString).size(withAttributes: [.font: font]).width)
    }

    /// Drawn width of the wider number. Digits are tabular, so it only changes with the digit count or format.
    var numberWidth: CGFloat { max(Self.width(intelligence.number), Self.width(thinking.number)) }

    private var labelWidth: CGFloat { max(Self.width(intelligence.label), Self.width(thinking.label)) }

    /// The block's width, gap included.
    var width: CGFloat {
        Self.gap + labelWidth + Self.labelGap + cell + Self.unitGap + Self.width(unit)
    }

    func draw(at x: CGFloat, height: CGFloat, palette: StatusItemPalette) {
        let font = Self.font
        let first = (height / 2 - Self.pitch / 2 + font.capHeight / 2).rounded()
        let left = x + Self.gap
        let numberRight = left + labelWidth + Self.labelGap + cell
        for (line, baseline, unit) in [(intelligence, first, nil), (thinking, first + Self.pitch, unit)] {
            func draw(_ text: String, at x: CGFloat, color: RGBColor) {
                NSAttributedString(string: text, attributes: [.font: font, .foregroundColor: color.nsColor])
                    .draw(at: CGPoint(x: x, y: baseline - font.ascender))
            }
            draw(line.label, at: left, color: palette.secondary)
            let numberWidth = (line.number as NSString).size(withAttributes: [.font: font]).width
            draw(line.number, at: numberRight - numberWidth, color: line.dim ? palette.secondary : palette.primary.opacity(0.85))
            if let unit { draw(unit, at: numberRight + Self.unitGap, color: palette.secondary) }
        }
    }
}
