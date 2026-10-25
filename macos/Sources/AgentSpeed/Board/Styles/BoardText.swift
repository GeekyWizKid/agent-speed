import AppKit
import CoreText

/// A type style of the prototypes' CSS: family, size, weight, letter spacing (em) and tabular digits.
struct BoardFont: Hashable {
    enum Family: Hashable {
        /// SF Pro (`--ui`, `--num`, `--disp`).
        case system
        /// SF Mono (`--mono`, neon's `--num`).
        case mono
    }

    var family = Family.system
    var size: CGFloat
    var weight: NSFont.Weight = .regular
    /// CSS `letter-spacing` in em.
    var tracking: CGFloat = 0
    var tabular = false

    @MainActor private static var cache: [BoardFont: NSFont] = [:]

    @MainActor
    var font: NSFont {
        if let font = Self.cache[self] { return font }
        let font = switch family {
        case .system: tabular ? NSFont.monospacedDigitSystemFont(ofSize: size, weight: weight)
            : NSFont.systemFont(ofSize: size, weight: weight)
        case .mono: NSFont.monospacedSystemFont(ofSize: size, weight: weight)
        }
        Self.cache[self] = font
        return font
    }
}

/// One line of text laid out with Core Text, drawn into the board's y-down contexts.
@MainActor
struct BoardLabel {
    let line: CTLine
    let width: CGFloat
    let font: NSFont
    private let attributes: [NSAttributedString.Key: Any]

    init(_ text: String, _ style: BoardFont, color: RGBColor) {
        var attributes: [NSAttributedString.Key: Any] = [
            .font: style.font,
            NSAttributedString.Key(kCTForegroundColorAttributeName as String): color.cgColor,
        ]
        if style.tracking != 0 { attributes[.kern] = style.tracking * style.size }
        self.init(line: CTLineCreateWithAttributedString(NSAttributedString(string: text, attributes: attributes)),
                  font: style.font, attributes: attributes)
    }

    private init(line: CTLine, font: NSFont, attributes: [NSAttributedString.Key: Any]) {
        self.line = line
        self.font = font
        self.attributes = attributes
        width = CGFloat(CTLineGetTypographicBounds(line, nil, nil, nil))
    }

    var isEmpty: Bool { width <= 0 }

    /// The line cut to `limit` with an ellipsis (CSS `text-overflow: ellipsis`); empty when not even that fits.
    func truncated(to limit: CGFloat) -> BoardLabel {
        guard width > limit + 0.5 else { return self }
        let token = CTLineCreateWithAttributedString(NSAttributedString(string: "…", attributes: attributes))
        let cut = limit > 0 ? CTLineCreateTruncatedLine(line, Double(limit), .end, token) : nil
        return BoardLabel(line: cut ?? CTLineCreateWithAttributedString(NSAttributedString(string: "")), font: font,
                          attributes: attributes)
    }

    /// The baseline that centers the line box on `midY` (CSS `align-items: center`).
    func baseline(centeredOn midY: CGFloat) -> CGFloat {
        midY + (font.ascender + font.descender) / 2
    }

    /// The baseline of a CSS `line-height: 1` box whose top is at `top`.
    func baseline(lineBoxTop top: CGFloat) -> CGFloat {
        top + (font.pointSize - (font.ascender - font.descender)) / 2 + font.ascender
    }

    func draw(in context: CGContext, x: CGFloat, baseline: CGFloat) {
        context.saveGState()
        context.textMatrix = CGAffineTransform(scaleX: 1, y: -1)
        context.textPosition = CGPoint(x: x, y: baseline)
        CTLineDraw(line, context)
        context.restoreGState()
    }

    /// Draws the line with a glow (CSS `text-shadow: 0 0 blur color`).
    func draw(in context: CGContext, x: CGFloat, baseline: CGFloat, glow: RGBColor?, blur: CGFloat) {
        guard let glow, blur > 0 else { return draw(in: context, x: x, baseline: baseline) }
        context.saveGState()
        BoardDrawing.setGlow(glow, blur: blur, in: context)
        draw(in: context, x: x, baseline: baseline)
        context.restoreGState()
    }

    /// Draws only the glow of the glyphs: the glyphs are set far outside the context and their shadow cast back.
    func drawGlowOnly(in context: CGContext, x: CGFloat, baseline: CGFloat, glow: RGBColor, blur: CGFloat) {
        let away: CGFloat = 4000
        let scale = abs(context.userSpaceToDeviceSpaceTransform.a)
        context.saveGState()
        context.setShadow(offset: CGSize(width: away * scale, height: 0), blur: blur * scale, color: glow.cgColor)
        draw(in: context, x: x - away, baseline: baseline)
        context.restoreGState()
    }

    /// Fills the glyphs with a linear gradient from `start` to `end` (CSS `background-clip: text`).
    func drawGradient(in context: CGContext, x: CGFloat, baseline: CGFloat, colors: [RGBColor], start: CGPoint, end: CGPoint) {
        guard let gradient = CGGradient(colorsSpace: CGColorSpace(name: CGColorSpace.sRGB),
                                        colors: colors.map(\.cgColor) as CFArray, locations: nil) else { return }
        context.saveGState()
        context.addPath(outline(x: x, baseline: baseline))
        context.clip()
        context.drawLinearGradient(gradient, start: start, end: end, options: [.drawsBeforeStartLocation, .drawsAfterEndLocation])
        context.restoreGState()
    }

    /// The glyphs' outlines at `x` and `baseline` (y down).
    func outline(x: CGFloat, baseline: CGFloat) -> CGPath {
        let path = CGMutablePath()
        for run in CTLineGetGlyphRuns(line) as? [CTRun] ?? [] {
            let count = CTRunGetGlyphCount(run)
            guard count > 0,
                  let font = (CTRunGetAttributes(run) as NSDictionary)[kCTFontAttributeName as String] else { continue }
            var glyphs = [CGGlyph](repeating: 0, count: count)
            var positions = [CGPoint](repeating: .zero, count: count)
            CTRunGetGlyphs(run, CFRange(location: 0, length: count), &glyphs)
            CTRunGetPositions(run, CFRange(location: 0, length: count), &positions)
            for index in 0..<count {
                guard let glyph = CTFontCreatePathForGlyph(font as! CTFont, glyphs[index], nil) else { continue }
                let transform = CGAffineTransform(translationX: x + positions[index].x, y: baseline - positions[index].y)
                    .scaledBy(x: 1, y: -1)
                path.addPath(glyph, transform: transform)
            }
        }
        return path
    }
}
