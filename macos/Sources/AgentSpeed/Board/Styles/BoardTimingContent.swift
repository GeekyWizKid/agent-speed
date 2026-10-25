import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit

/// 計時塔 (DESIGN 2c Styles; `board-timing.html`): a racing broadcast timing tower on a hatched slab: the heat rail
/// on the top edge, a `boards.live` marker, monospaced numbers, 30 one-second bars, position blocks and intervals.
@MainActor
final class BoardTimingContent: BoardContentBuilding {
    let size: BoardSize
    let palette: BoardPalette
    let l10n: Localizer
    let rows: BoardTimingRows

    init(size: BoardSize, dark: Bool, highContrast: Bool, l10n: Localizer, capsules: BoardCapsuleDrawing) {
        self.size = size
        palette = BoardPalette(style: .timing, dark: dark, highContrast: highContrast)
        self.l10n = l10n
        rows = BoardTimingRows(size: size, palette: palette, capsules: capsules)
    }

    private var side: CGFloat { CGFloat(size.side) }
    private var pad: CGFloat { size == .small ? 12 : size == .medium ? 14 : 16 }
    private var heroHeight: CGFloat { size == .small ? 70 : size == .medium ? 78 : 86 }
    private var big: CGFloat { size == .small ? 30 : size == .medium ? 36 : 42 }
    private var rowsTop: CGFloat { pad + heroHeight + 8 }

    func layout(_ tick: BoardTick) -> BoardContentLayout {
        let models = BoardRowModels.make(tick, l10n: l10n)
        let column = BoardRowModels.capsuleColumn(models, compact: rows.compact, capsules: rows.capsules)
        var items = [slab(tick)]
        let (hero, number) = self.hero(tick)
        items.append(hero)
        var anchors: [String: (row: CGRect, rank: CGPoint)] = [:]
        for (row, model) in zip(tick.rows, models) {
            let frame = rows.frame(row, rowsTop: rowsTop, pad: pad)
            if case .group(let index) = row.kind { anchors[tick.groups[index].id] = (frame, rows.rankCenter(frame)) }
            items.append(rows.item(model, key: row.key, frame: frame, column: column))
        }
        items.append(footer(tick))
        return BoardContentLayout(items: items, groupRows: anchors, number: number)
    }

    /// The slab's 135° hatch and the heat rail along the top edge.
    private func slab(_ tick: BoardTick) -> BoardContentItem {
        let heat = (tick.heat * 100).rounded() / 100
        let palette = self.palette, side = self.side
        let revision = AnyHashable([AnyHashable("slab"), AnyHashable(heat), AnyHashable(palette.dark), AnyHashable(palette.highContrast)])
        return BoardContentItem(key: "ambience", frame: CGRect(x: 0, y: 0, width: side, height: side), motion: .none,
                                revision: revision) { context, _ in
            if !palette.highContrast {
                let hatch = (palette.dark ? RGBColor.white : RGBColor.black).opacity(0.025)
                context.setStrokeColor(hatch.cgColor)
                context.setLineWidth(2)
                let step = 7 * 2.squareRoot()
                var c = -step
                while c < 2 * side + step {
                    context.move(to: CGPoint(x: c + 1, y: -1))
                    context.addLine(to: CGPoint(x: -1, y: c + 1))
                    c += step
                }
                context.strokePath()
            }
            BoardDrawing.fill(CGRect(x: 0, y: 0, width: side, height: 3), color: palette.line, in: context)
            let width = side * heat
            guard width > 0.5 else { return }
            let rail = CGRect(x: 0, y: 0, width: width, height: 3)
            let stops: [(RGBColor, CGFloat)] = [(RGBColor(hex: 0x2DD4BF), 0), (RGBColor(hex: 0x818CF8), 0.4),
                                                 (RGBColor(hex: 0xFB7185), 0.75), (RGBColor(hex: 0xFACC15), 1)]
            BoardDrawing.glowing(palette.glows ? palette.heat(max(0.1, heat)).opacity(0.7) : nil, blur: 10, in: context) {
                BoardDrawing.gradient(rail, stops: stops, from: CGPoint(x: 0, y: 0), to: CGPoint(x: width, y: 0), in: context)
            }
        }
    }

    private func hero(_ tick: BoardTick) -> (BoardContentItem, CGRect) {
        let palette = self.palette, big = self.big, heroHeight = self.heroHeight, locale = l10n.locale
        let heat = (tick.heat * 50).rounded() / 50
        let live = BoardLabel(l10n("boards.live").uppercased(with: locale),
                              BoardFont(size: 10, weight: .bold, tracking: 0.14), color: palette.ink2)
        let number = BoardLabel(tick.valueText, BoardFont(family: .mono, size: big, weight: .heavy, tracking: -0.04), color: palette.ink)
        let unit = BoardLabel(l10n("unit.tokS").uppercased(with: locale),
                              BoardFont(family: .mono, size: 11, weight: .semibold, tracking: 0.04), color: palette.ink3)
        let title = BoardLabel(tick.title, BoardFont(size: 11, weight: .bold, tracking: 0.04), color: RGBColor(hex: 0x0B0D12))
        let counts = BoardLabel(l10n("agents.context", tick.generating, tick.tools).uppercased(with: locale),
                                BoardFont(family: .mono, size: 10.5, weight: .semibold, tracking: 0.03), color: palette.ink3)
        let column = 10 + 6 + big * 1.08 + 7 + 17
        let top = ((heroHeight - column) / 2).rounded()
        let numTop = top + 16
        let barCount = size == .small ? 20 : 30
        let frame = CGRect(x: pad, y: pad + 2, width: side - 2 * pad, height: heroHeight)
        let numberRect = CGRect(x: frame.minX, y: frame.minY + numTop, width: number.width, height: big * 1.08)
        let history = tick.history.suffix(30).map { ($0 * 10).rounded() / 10 }
        let generating = tick.generating > 0
        let revision = AnyHashable([AnyHashable(tick.valueText), AnyHashable(tick.title), AnyHashable(tick.generating),
                                    AnyHashable(tick.tools), AnyHashable(Array(history)), AnyHashable(heat), AnyHashable(palette.dark),
                                    AnyHashable(palette.highContrast), AnyHashable(l10n.language.rawValue)])
        let item = BoardContentItem(key: "hero", frame: frame, motion: .none, revision: revision) { context, box in
            let dot = CGRect(x: 0, y: top + 1.5, width: 7, height: 7)
            BoardDrawing.fillCircle(dot, color: generating ? BoardPalette.red : palette.ink3, in: context)
            live.draw(in: context, x: 13, baseline: live.baseline(lineBoxTop: top))

            let baseline = number.baseline(lineBoxTop: numTop)
            number.draw(in: context, x: 0, baseline: baseline)
            unit.draw(in: context, x: number.width + 6, baseline: baseline)

            let pillTop = numTop + big * 1.08 + 7
            let pill = CGRect(x: 0, y: pillTop, width: title.width + 14, height: 17)
            BoardDrawing.fill(pill, radius: 5, color: palette.heat(max(0.1, heat)).opacity(1), in: context)
            title.draw(in: context, x: 7, baseline: title.baseline(centeredOn: pill.midY))
            let barsWidth = CGFloat(barCount) * 5 - 2
            counts.truncated(to: max(0, box.width - barsWidth - 8 - pill.width - 7))
                .draw(in: context, x: pill.maxX + 7, baseline: counts.baseline(centeredOn: pill.midY))

            let bars = CGRect(x: box.width - barsWidth, y: (box.height - 38) / 2, width: barsWidth, height: 38)
            BoardHeroHistory.bars(Array(history), count: barCount, in: bars, color: palette.heat(max(0.1, heat)), context: context)
        }
        return (item, numberRect)
    }

    private func footer(_ tick: BoardTick) -> BoardContentItem {
        var parts = [BoardFooterPart("boards.today", value: tick.todayText, l10n: l10n, uppercase: true)]
        if let cost = tick.costText { parts.append(BoardFooterPart(value: cost)) }
        if size != .small { parts.append(BoardFooterPart("boards.peak", value: tick.peakText, l10n: l10n, uppercase: true)) }
        let palette = self.palette, pad = self.pad
        let frame = CGRect(x: 0, y: side - 28, width: side, height: 28)
        let revision = AnyHashable([tick.todayText, tick.costText ?? "", tick.peakText, palette.dark ? "d" : "l",
                                    palette.highContrast ? "c" : "", l10n.language.rawValue])
        return BoardContentItem(key: "footer", frame: frame, motion: .none, revision: revision) { context, box in
            BoardDrawing.fill(CGRect(origin: .zero, size: box), color: palette.slab2, in: context)
            BoardDrawing.fill(CGRect(x: 0, y: 0, width: box.width, height: 1), color: palette.line, in: context)
            context.saveGState()
            context.translateBy(x: pad, y: 0)
            BoardFooterPart.draw(parts, label: BoardFont(family: .mono, size: 10, weight: .bold, tracking: 0.06),
                                 strong: BoardFont(family: .mono, size: 10, weight: .bold, tracking: 0.06),
                                 labelColor: palette.ink3, strongColor: palette.ink2, width: box.width - 2 * pad,
                                 midY: box.height / 2, in: context)
            context.restoreGState()
        }
    }
}
