import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit

/// The menu bar intelligence block (DESIGN 10, Menu bar and taskbar): after the value and the money block,
/// `智力 86` over `思考 128 tok/s` for every enabled source, in the traffic block's 9 pt tabular text. 智力 is the
/// highest task score of all their agents (DESIGN 12, METRICS 6.9 *Sets and groups*): live in the label color at 85 %,
/// resting and `–` in the secondary color, with the proof mark under an unverified or pending score. Labels sit
/// left-aligned in a cell as wide as the wider label, both numbers end on one edge of a shared `8.88k` cell, and only
/// the thinking line carries the unit, so the block's width depends on the language alone. The block stays while
/// nothing is generated, like a system meter: a block that came and went would shift every item to its left. Once the
/// thinking rate has read 0 for 3 s under a score only `智力 86` is drawn, centred in the block, in the same cells.
/// Every change replaces characters in their cells: a status item stays still. It needs neither network measuring nor
/// any helper.
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
        /// Resting, `–` or a zero thinking rate: the number takes the secondary color.
        var dim: Bool
        /// An unverified or pending score: the proof mark goes under the number (DESIGN 12, Proof mark).
        var unproven = false
    }

    var intelligence: Line
    var thinking: Line
    /// The thinking line is hidden: line 1 alone, centred vertically (DESIGN 10, Thinking hidden at zero).
    var alone: Bool
    var unit: String
    /// Room for the numbers: the `8.88k` cell, wider only while the width governor holds a longer text.
    var cell: CGFloat
    /// `a11y.orb.intel` with the score words, `a11y.orb.intel.only` while alone, `intel.none` without a score.
    var spoken: String

    static var font: NSFont { .monospacedDigitSystemFont(ofSize: size, weight: .medium) }

    @MainActor
    init?(model: AppModel) {
        let settings = model.settings
        guard settings.barTraffic == .intelligence, settings.barMode != .glyph else { return nil }
        let snapshot = model.snapshot
        self.init(snapshot.intelligence, agents: snapshot.agents, l10n: model.l10n, thinkingHidden: model.barThinking.hidden)
    }

    /// `sums` of all enabled sources and their `agents` (the supplying agent's state and N). `thinkingHidden`: the hold
    /// has passed (`StatusItemThinkingHold`); the line goes only while `sums` still reads 0.
    @MainActor
    init(_ sums: IntelligenceSums, agents: [AgentStatus] = [], l10n: Localizer, thinkingHidden: Bool = false) {
        let reading = TaskSetReading(score: sums.score, agents: agents)
        let score = reading.shown.map(String.init)
        // Nothing generated in the window (`Σ w = 0`): the thinking number is `–` (DESIGN 10, Quiet state).
        let thinking = sums.output > 0 ? SpeedFormat.speed(sums.thinking) : nil
        intelligence = Line(label: l10n("intel.glyph"), number: score ?? "–", dim: score == nil || reading.isResting,
                            unproven: reading.isUnproven)
        self.thinking = Line(label: l10n("intel.thinking.glyph"), number: thinking ?? "–",
                             dim: thinking == nil || sums.thinking < LiveSpeedValue.visibleMinimum)
        let alone = thinkingHidden && StatusItemThinkingHold.readsZero(sums)
        self.alone = alone
        unit = l10n("unit.tokS")
        cell = Self.width(Self.widestNumber)
        spoken = TaskScoreText.words(TaskScoreValue(reading), l10n).map { words in
            alone ? l10n("a11y.orb.intel.only", words) : l10n("a11y.orb.intel", words, thinking ?? "0")
        } ?? l10n("intel.none")
        cell = max(cell, numberWidth)
    }

    /// The tooltip's line `task.tooltip.top`: the score and the project of the agent that supplies it (its source
    /// without a project); nil without a score.
    @MainActor
    static func topAgent(_ score: TaskSetValue, agents: [AgentStatus], l10n: Localizer) -> String? {
        let reading = TaskSetReading(score: score, agents: agents)
        guard let shown = reading.shown, let agent = agents.first(where: { $0.id == reading.agentId }) else { return nil }
        return l10n("task.tooltip.top", String(shown), agent.project.isEmpty ? l10n(agent.source.titleKey) : agent.project)
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

    /// Baselines of line 1 and line 2; line 1 alone sits halfway between them, at the item's text centre.
    func baselines(height: CGFloat) -> (intelligence: CGFloat, thinking: CGFloat) {
        let first = (height / 2 - Self.pitch / 2 + Self.font.capHeight / 2).rounded()
        return (alone ? first + Self.pitch / 2 : first, first + Self.pitch)
    }

    func draw(at x: CGFloat, height: CGFloat, palette: StatusItemPalette) {
        let font = Self.font
        let baselines = baselines(height: height)
        let left = x + Self.gap
        let numberRight = left + labelWidth + Self.labelGap + cell
        let lines = [(intelligence, baselines.intelligence, String?.none), (thinking, baselines.thinking, unit)]
        for (line, baseline, unit) in lines.prefix(alone ? 1 : 2) {
            func draw(_ text: String, at x: CGFloat, color: RGBColor) {
                NSAttributedString(string: text, attributes: [.font: font, .foregroundColor: color.nsColor])
                    .draw(at: CGPoint(x: x, y: baseline - font.ascender))
            }
            draw(line.label, at: left, color: palette.secondary)
            let numberWidth = (line.number as NSString).size(withAttributes: [.font: font]).width
            let color = line.dim ? palette.secondary : palette.primary.opacity(0.85)
            draw(line.number, at: numberRight - numberWidth, color: color)
            if line.unproven {
                Self.drawProofMark(x: numberRight - numberWidth, width: numberWidth, baseline: baseline, color: color,
                                   increasedContrast: palette.increasedContrast)
            }
            if let unit { draw(unit, at: numberRight + Self.unitGap, color: palette.secondary) }
        }
    }

    /// The dotted underline under the number's advance `width`, centred `ProofMark.barDepth` × the text size below
    /// its baseline (the room line 2 leaves). Each dot lands on the device pixel grid, so the 1 pt dots stay round and
    /// even in the item's bitmap instead of smearing over two pixel rows.
    private static func drawProofMark(x: CGFloat, width: CGFloat, baseline: CGFloat, color: RGBColor,
                                      increasedContrast: Bool) {
        guard let context = NSGraphicsContext.current?.cgContext else { return }
        let scale = max(1, hypot(context.ctm.a, context.ctm.b))
        func snap(_ value: CGFloat) -> CGFloat { (value * scale).rounded() / scale }
        let diameter = ProofMark.dotDiameter(textSize: size, scale: scale, increasedContrast: increasedContrast)
        let top = snap(baseline + ProofMark.barDepth * size - diameter / 2)
        context.saveGState()
        context.setFillColor((increasedContrast ? color : color.opacity(ProofMark.opacity)).cgColor)
        for center in ProofMark.dotCenters(width: width, diameter: diameter) {
            context.fillEllipse(in: CGRect(x: snap(x + center - diameter / 2), y: top, width: diameter, height: diameter))
        }
        context.restoreGState()
    }
}
