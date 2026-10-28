import AgentSpeedOrbModel
import AppKit

/// Neon's sub-gauge, LED 副錶 (DESIGN 2c Live metrics): 智力 in lit LED digits over ghost segments, a 12-segment
/// meter, the 思考 / 費用 table under it; right-aligned at the side inset and centred in the band less the LED bar.
@MainActor
final class BoardNeonGauge {
    let size: BoardSize
    let palette: BoardPalette
    let l10n: Localizer
    let metrics: BoardGaugeMetrics
    private(set) var table: BoardGaugeTable?
    private var tableKey: [String] = []
    private var numberCell = BoardCellWidth()

    init(size: BoardSize, palette: BoardPalette, l10n: Localizer) {
        self.size = size
        self.palette = palette
        self.l10n = l10n
        metrics = BoardGaugeMetrics(.neon, size)
    }

    private var nominal: CGFloat { CGFloat(BoardGeometry(.neon, size).gaugeWidth) }

    private func prepare(_ tick: BoardMetricsTick, readings: BoardGaugeReadings, now: TimeInterval) -> CGFloat {
        let cells = BoardGaugeReadings.cells(tick, l10n: l10n)
        let key = cells.labels + ["|"] + cells.units
        if table == nil || key != tableKey {
            tableKey = key
            table = cells.labels.isEmpty ? nil
                : BoardGaugeTable(style: .neon, size: size, palette: palette, labels: cells.labels, units: cells.units)
            numberCell.reset(base: Double(table?.numberBase ?? 0))
        }
        guard let table else { return 0 }
        let need = [readings.thinking, readings.cost].compactMap { $0 }.map { Double(table.need($0)) }.max() ?? 0
        return CGFloat(numberCell.fit(need, at: now))
    }

    var narrowsAt: TimeInterval? { numberCell.narrowsAt }

    func width(_ tick: BoardMetricsTick, now: TimeInterval) -> CGFloat {
        let readings = BoardGaugeReadings(tick, l10n: l10n)
        let cell = prepare(tick, readings: readings, now: now)
        return max(nominal, table.map { $0.width(numberCell: cell) } ?? 0).rounded(.up)
    }

    func items(_ tick: BoardMetricsTick, in frame: BoardGaugeFrame) -> [BoardContentItem] {
        let readings = BoardGaugeReadings(tick, l10n: l10n)
        let cell = prepare(tick, readings: readings, now: frame.now)
        let width = max(nominal, table.map { $0.width(numberCell: cell) } ?? 0).rounded(.up)
        let center = frame.right - width / 2
        let stack = metrics.stack(intelligence: readings.intelligenceOn, thinking: readings.thinking != nil,
                                  cost: readings.cost != nil)
        let top = frame.top + (frame.height - CGFloat(stack.height)) / 2
        let memory = frame.memory
        var items: [BoardContentItem] = []
        if let main = stack.main {
            items += digits(tick, readings: readings, center: center, top: top + CGFloat(main), memory: memory)
        }
        if let table {
            let left = center - table.width(numberCell: cell) / 2
            for line in stack.rows {
                let (reading, key) = line.key == .thinking ? (readings.thinking, "think") : (readings.cost, "cost")
                guard let reading else { continue }
                items += table.items(reading, key: "gauge." + key, left: left, numberCell: cell,
                                     baseline: top + CGFloat(line.baseline), previous: memory.previousMetric(key),
                                     fresh: memory.fresh)
            }
        }
        return items
    }

    private func digits(_ tick: BoardMetricsTick, readings: BoardGaugeReadings, center: CGFloat, top: CGFloat,
                        memory: BoardTickMemory) -> [BoardContentItem] {
        let palette = self.palette, metrics = self.metrics
        let board = CGRect(x: 0, y: 0, width: CGFloat(size.side), height: CGFloat(size.side))
        let theme = AnyHashable([AnyHashable(palette.dark), AnyHashable(palette.highContrast), AnyHashable(size.rawValue)])
        var items: [BoardContentItem] = []

        let labelFont = BoardFont(size: CGFloat(metrics.mainLabelSize), weight: .semibold, tracking: CGFloat(metrics.mainLabelTracking))
        let label = BoardLabel(l10n("intel.glyph"), labelFont, color: palette.ink3)
        let labelBaseline = top + CGFloat(metrics.mainLabelBaseline)
        let labelX = label.centredX(center, tracking: labelFont.tracking)
        var caption = BoardContentItem.part("gauge.iq.label", of: board, rect: label.box(x: labelX, baseline: labelBaseline),
                                            revision: AnyHashable([AnyHashable(label.text), theme])) { context in
            label.draw(in: context, x: labelX, baseline: labelBaseline)
        }
        caption.motion = .glide(fade: 0.25)
        caption.group = .hero
        items.append(caption)

        let numberFont = BoardFont(family: .mono, size: CGFloat(metrics.mainNumberSize), weight: .bold, tracking: -0.03)
        let quiet = readings.intelligenceQuiet
        let baseline = top + CGFloat(metrics.mainNumberBaseline)
        let number = BoardRollingText(key: "gauge.iq.n", text: readings.intelligence, font: numberFont,
                                      color: quiet ? palette.quiet : palette.led, center: center, baseline: baseline)
        // Ghost segments: as many dim 8s as the number has digits (none with Increase Contrast).
        let ghostText = String(readings.intelligence.map { $0.isNumber ? "8" : " " })
        if palette.glows, readings.intelligence.contains(where: \.isNumber) {
            let ghost = BoardLabel(ghostText, numberFont, color: palette.ghost)
            let x = number.x
            var item = BoardContentItem.part("gauge.iq.ghost", of: board, rect: ghost.box(x: x, baseline: baseline),
                                             revision: AnyHashable([AnyHashable(ghostText), AnyHashable(x), AnyHashable(baseline), theme])) { context in
                ghost.draw(in: context, x: x, baseline: baseline)
            }
            item.motion = .glide(fade: BoardRollingText.crossfade)
            item.group = .hero
            items.append(item)
        }
        if palette.dark, palette.glows, !quiet {
            var glow = BoardContentItem.glow("gauge.iq.glow", label: number.label, x: number.x, baseline: baseline, layers: [
                BoardGlow.Layer(color: RGBColor(red: 120 / 255, green: 160 / 255, blue: 1, alpha: 0.4), blur: 12),
                BoardGlow.Layer(color: RGBColor(red: 150 / 255, green: 190 / 255, blue: 1, alpha: 0.75), blur: 4),
            ])
            glow.motion = .glide(fade: 0.25)
            items.append(glow)
        }
        items += number.items(previous: memory.previousMetric("intel"), fresh: memory.fresh,
                              motion: .glide(fade: BoardRollingText.crossfade))

        // The meter: 12 segments, 3 tall and 2 apart, lit from the left (8 index points each); a new count crossfades.
        let lit = BoardIntelligenceScale.segments(tick.intelligence)
        let meterWidth = CGFloat(metrics.meterWidth)
        let segments = BoardGaugeMetrics.meterSegments
        let meter = CGRect(x: center - meterWidth / 2, y: top + CGFloat(metrics.meterTop), width: meterWidth,
                           height: CGFloat(BoardGaugeMetrics.meterHeight))
        let glows = palette.dark && palette.glows
        let unlit = palette.highContrast ? palette.ink3 : palette.unlit
        var item = BoardContentItem.part("gauge.meter", of: board, rect: meter.insetBy(dx: -8, dy: -8),
                                         revision: AnyHashable([AnyHashable(lit), theme])) { context in
            let gap: CGFloat = 2
            let width = (meter.width - gap * CGFloat(segments - 1)) / CGFloat(segments)
            func segment(_ index: Int) -> CGRect {
                CGRect(x: meter.minX + CGFloat(index) * (width + gap), y: meter.minY, width: width, height: meter.height)
            }
            if glows, lit > 0 {
                BoardGlow.fill(palette.mind, blur: 5, in: context) { context in
                    for index in 0..<lit { BoardDrawing.fill(segment(index), radius: 1, color: palette.mind, in: context) }
                }
            }
            for index in 0..<segments {
                BoardDrawing.fill(segment(index), radius: 1, color: index < lit ? palette.mind : unlit, in: context)
            }
        }
        item.motion = .glide(fade: 0.25)
        item.group = .hero
        if memory.fresh, let previous = memory.previousMetric("intel"),
           BoardIntelligenceScale.segments(Int(previous)) != lit {
            item.accents.append(.crossfade(BoardContentStack.sweepSeconds))
        }
        items.append(item)
        return items
    }
}
